using System;
using System.Collections.Generic;
using System.Linq;
using SolutionComponentComparer.Core;

namespace SolutionComponentComparer.Data
{
    /// <summary>
    /// Turns (componenttype, objectid) into readable names. Caches everything per connection,
    /// so a second comparison only fetches names it has not seen yet.
    /// </summary>
    public class ComponentNameResolver : IComponentNameProvider
    {
        private readonly Dictionary<ComponentKey, DataverseRepository.RecordName> _records = new Dictionary<ComponentKey, DataverseRepository.RecordName>();
        private Dictionary<int, string> _definitions;
        private MetadataIndex _metadata;
        private bool _optionSetsLoaded;

        public ComponentNameResolver()
        {
            Warnings = new List<string>();
        }

        /// <summary>Non-fatal problems while resolving names (shown in the XrmToolBox log).</summary>
        public List<string> Warnings { get; private set; }

        public void EnsureNames(DataverseRepository repository, IList<ComponentMembership> memberships, Action<string> progress)
        {
            Warnings.Clear();
            var types = new HashSet<int>(memberships.Select(m => m.ComponentType));

            if (_definitions == null)
            {
                Report(progress, "Loading component type definitions...");
                try
                {
                    _definitions = repository.LoadComponentDefinitions();
                }
                catch (Exception ex)
                {
                    _definitions = new Dictionary<int, string>();
                    Warnings.Add("solutioncomponentdefinition not readable, using built-in type map: " + ex.Message);
                }
            }

            bool needChildren = types.Contains(ComponentTypeCatalog.Attribute)
                || types.Contains(ComponentTypeCatalog.EntityRelationship)
                || types.Contains(ComponentTypeCatalog.EntityKey);

            if (_metadata == null || (needChildren && !_metadata.HasChildren))
            {
                Report(progress, needChildren
                    ? "Loading table metadata (tables, columns, relationships, keys)... this can take a minute"
                    : "Loading table metadata...");
                try
                {
                    var previousOptionSets = _metadata == null ? null : _metadata.OptionSets;
                    _metadata = repository.LoadMetadataIndex(needChildren);
                    if (previousOptionSets != null)
                    {
                        foreach (var kv in previousOptionSets) _metadata.OptionSets[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex)
                {
                    Warnings.Add("Metadata could not be loaded, table/column names will be missing: " + ex.Message);
                    if (_metadata == null) _metadata = new MetadataIndex();
                }
            }

            if (types.Contains(ComponentTypeCatalog.OptionSet) && !_optionSetsLoaded)
            {
                Report(progress, "Loading global choices...");
                try
                {
                    repository.LoadGlobalOptionSets(_metadata);
                    _optionSetsLoaded = true;
                }
                catch (Exception ex)
                {
                    Warnings.Add("Global choices could not be loaded: " + ex.Message);
                }
            }

            var pending = memberships
                .Where(m => !ComponentTypeCatalog.IsMetadataType(m.ComponentType))
                .Select(m => new ComponentKey(m.ComponentType, m.ObjectId))
                .Distinct()
                .Where(k => !_records.ContainsKey(k))
                .GroupBy(k => k.Type)
                .ToList();

            foreach (var group in pending)
            {
                int type = group.Key;
                var ids = group.Select(k => k.ObjectId).ToList();

                string entityName = EntityNameForType(type);
                MetadataIndex.EntityEntry entity = null;
                if (entityName != null) _metadata.EntitiesByName.TryGetValue(entityName, out entity);

                if (entityName == null || (entity != null && string.IsNullOrEmpty(entity.PrimaryNameAttribute)))
                {
                    MarkAttempted(type, ids);
                    continue;
                }

                string idAttribute = entity != null && !string.IsNullOrEmpty(entity.PrimaryIdAttribute) ? entity.PrimaryIdAttribute : entityName + "id";
                string nameAttribute = entity != null ? entity.PrimaryNameAttribute : "name";
                string parentAttribute;
                ComponentTypeCatalog.ParentEntityAttribute.TryGetValue(type, out parentAttribute);

                Report(progress, "Resolving names: " + ComponentTypeCatalog.GetTypeName(type, null) + " (" + ids.Count.ToString("N0") + ")...");
                try
                {
                    Dictionary<Guid, DataverseRepository.RecordName> names;
                    try
                    {
                        names = repository.LoadRecordNames(entityName, idAttribute, nameAttribute, parentAttribute, ids);
                    }
                    catch (Exception) when (parentAttribute != null)
                    {
                        // Parent column not available on this table/version: retry without it.
                        names = repository.LoadRecordNames(entityName, idAttribute, nameAttribute, null, ids);
                    }

                    foreach (var id in ids)
                    {
                        DataverseRepository.RecordName name;
                        names.TryGetValue(id, out name);
                        _records[new ComponentKey(type, id)] = name;
                    }
                }
                catch (Exception ex)
                {
                    Warnings.Add("Names for component type " + type + " (" + entityName + ") could not be read: " + ex.Message);
                    MarkAttempted(type, ids);
                }
            }
        }

        public ComponentInfo Describe(int componentType, Guid objectId, string typeLabelFromServer)
        {
            var info = new ComponentInfo { TypeName = TypeName(componentType, typeLabelFromServer) };
            var md = _metadata ?? new MetadataIndex();

            switch (componentType)
            {
                case ComponentTypeCatalog.Entity:
                {
                    MetadataIndex.EntityEntry e;
                    if (md.EntitiesById.TryGetValue(objectId, out e))
                    {
                        info.Name = e.LogicalName;
                        info.Label = e.DisplayName;
                        info.EntityLogicalName = e.LogicalName;
                    }
                    return info;
                }
                case ComponentTypeCatalog.Attribute:
                    return FromChild(info, md.Attributes, objectId);
                case ComponentTypeCatalog.EntityRelationship:
                    return FromChild(info, md.Relationships, objectId);
                case ComponentTypeCatalog.EntityKey:
                    return FromChild(info, md.Keys, objectId);
                case ComponentTypeCatalog.OptionSet:
                    return FromChild(info, md.OptionSets, objectId);
            }

            DataverseRepository.RecordName record;
            if (_records.TryGetValue(new ComponentKey(componentType, objectId), out record) && record != null)
            {
                info.Name = record.Name;
                if (!string.IsNullOrEmpty(record.Parent) && !string.Equals(record.Parent, "none", StringComparison.OrdinalIgnoreCase))
                {
                    info.ParentEntities.Add(record.Parent);
                }
            }
            return info;
        }

        private string TypeName(int type, string serverLabel)
        {
            string name;
            if (ComponentTypeCatalog.TypeNames.TryGetValue(type, out name)) return name;
            if (!string.IsNullOrWhiteSpace(serverLabel)) return serverLabel;

            string entityName;
            if (_definitions != null && _definitions.TryGetValue(type, out entityName))
            {
                MetadataIndex.EntityEntry e;
                if (_metadata != null && _metadata.EntitiesByName.TryGetValue(entityName, out e) && !string.IsNullOrEmpty(e.DisplayName))
                {
                    return e.DisplayName;
                }
                return entityName;
            }
            return "Type " + type;
        }

        private string EntityNameForType(int type)
        {
            string name;
            if (_definitions != null && _definitions.TryGetValue(type, out name) && !string.IsNullOrEmpty(name)) return name;
            if (ComponentTypeCatalog.EntityByType.TryGetValue(type, out name)) return name;
            return null;
        }

        private void MarkAttempted(int type, IEnumerable<Guid> ids)
        {
            foreach (var id in ids) _records[new ComponentKey(type, id)] = null;
        }

        private static ComponentInfo FromChild(ComponentInfo info, Dictionary<Guid, MetadataIndex.ChildEntry> map, Guid id)
        {
            MetadataIndex.ChildEntry child;
            if (map.TryGetValue(id, out child))
            {
                info.Name = child.Name;
                info.Label = child.Label;
                info.ParentEntities.AddRange(child.Entities);
            }
            return info;
        }

        private static void Report(Action<string> progress, string message)
        {
            if (progress != null) progress(message);
        }
    }
}
