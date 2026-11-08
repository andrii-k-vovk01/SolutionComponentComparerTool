using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Microsoft.Xrm.Sdk.Query;
using SolutionComponentComparer.Core;

namespace SolutionComponentComparer.Data
{
    public class SolutionInfo
    {
        public Guid Id { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
        public string Version { get; set; }
        public bool IsManaged { get; set; }
        public bool IsVisible { get; set; }
        public string Publisher { get; set; }
        public string PublisherPrefix { get; set; }
        public DateTime? InstalledOn { get; set; }
    }

    public class WhereUsedRow
    {
        public string Role { get; set; }
        public string UniqueName { get; set; }
        public string DisplayName { get; set; }
        public string Version { get; set; }
        public bool Managed { get; set; }
        public string Behavior { get; set; }
        public string Publisher { get; set; }
    }

    /// <summary>Lightweight metadata index used to name metadata-based components.</summary>
    public class MetadataIndex
    {
        public MetadataIndex()
        {
            EntitiesById = new Dictionary<Guid, EntityEntry>();
            EntitiesByName = new Dictionary<string, EntityEntry>(StringComparer.OrdinalIgnoreCase);
            Attributes = new Dictionary<Guid, ChildEntry>();
            Relationships = new Dictionary<Guid, ChildEntry>();
            Keys = new Dictionary<Guid, ChildEntry>();
            OptionSets = new Dictionary<Guid, ChildEntry>();
        }

        public bool HasChildren { get; set; }
        public Dictionary<Guid, EntityEntry> EntitiesById { get; private set; }
        public Dictionary<string, EntityEntry> EntitiesByName { get; private set; }
        public Dictionary<Guid, ChildEntry> Attributes { get; private set; }
        public Dictionary<Guid, ChildEntry> Relationships { get; private set; }
        public Dictionary<Guid, ChildEntry> Keys { get; private set; }
        public Dictionary<Guid, ChildEntry> OptionSets { get; private set; }

        public class EntityEntry
        {
            public Guid Id { get; set; }
            public string LogicalName { get; set; }
            public string DisplayName { get; set; }
            public string PrimaryIdAttribute { get; set; }
            public string PrimaryNameAttribute { get; set; }
        }

        public class ChildEntry
        {
            public ChildEntry()
            {
                Entities = new List<string>();
            }

            public string Name { get; set; }
            public string Label { get; set; }
            public List<string> Entities { get; private set; }
        }
    }

    /// <summary>All read-only Dataverse access of the tool. Nothing here creates, updates or deletes.</summary>
    public class DataverseRepository
    {
        private const int PageSize = 5000;
        private const int InChunk = 200;
        private readonly IOrganizationService _service;

        public DataverseRepository(IOrganizationService service)
        {
            if (service == null) throw new ArgumentNullException("service");
            _service = service;
        }

        // ------------------------------------------------------------ solutions

        public List<SolutionInfo> LoadSolutions()
        {
            var query = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("uniquename", "friendlyname", "version", "ismanaged", "isvisible", "installedon", "publisherid")
            };
            var pub = query.AddLink("publisher", "publisherid", "publisherid", JoinOperator.LeftOuter);
            pub.EntityAlias = "pub";
            pub.Columns = new ColumnSet("friendlyname", "customizationprefix");
            query.AddOrder("friendlyname", OrderType.Ascending);

            return RetrieveAll(query, null).Select(e => new SolutionInfo
            {
                Id = e.Id,
                UniqueName = e.GetAttributeValue<string>("uniquename"),
                FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                Version = e.GetAttributeValue<string>("version"),
                IsManaged = e.GetAttributeValue<bool>("ismanaged"),
                IsVisible = e.GetAttributeValue<bool>("isvisible"),
                InstalledOn = e.GetAttributeValue<DateTime?>("installedon"),
                Publisher = Aliased<string>(e, "pub.friendlyname"),
                PublisherPrefix = Aliased<string>(e, "pub.customizationprefix")
            }).ToList();
        }

        // ------------------------------------------------------------ components

        public List<ComponentMembership> LoadMemberships(IList<Guid> solutionIds, Action<string> progress)
        {
            var result = new List<ComponentMembership>();
            var ids = solutionIds.Distinct().ToList();
            for (int i = 0; i < ids.Count; i += 50)
            {
                var chunk = ids.Skip(i).Take(50).Cast<object>().ToArray();
                var query = new QueryExpression("solutioncomponent")
                {
                    ColumnSet = new ColumnSet("solutionid", "objectid", "componenttype", "rootcomponentbehavior")
                };
                query.Criteria.AddCondition("solutionid", ConditionOperator.In, chunk);
                query.AddOrder("solutioncomponentid", OrderType.Ascending);

                int before = result.Count;
                foreach (var e in RetrieveAll(query, n =>
                {
                    if (progress != null) progress("Loading solution components... " + (before + n).ToString("N0") + " rows");
                }))
                {
                    var solution = e.GetAttributeValue<EntityReference>("solutionid");
                    var type = e.GetAttributeValue<OptionSetValue>("componenttype");
                    if (solution == null || type == null) continue;
                    var behavior = e.GetAttributeValue<OptionSetValue>("rootcomponentbehavior");
                    string label;
                    e.FormattedValues.TryGetValue("componenttype", out label);
                    result.Add(new ComponentMembership
                    {
                        SolutionId = solution.Id,
                        ObjectId = e.GetAttributeValue<Guid>("objectid"),
                        ComponentType = type.Value,
                        RootBehavior = behavior == null ? (int?)null : behavior.Value,
                        TypeLabel = label
                    });
                }
            }
            return result;
        }

        public List<WhereUsedRow> LoadWhereUsed(int componentType, Guid objectId, Func<Guid, string> roleOf)
        {
            var query = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("solutionid", "rootcomponentbehavior")
            };
            query.Criteria.AddCondition("objectid", ConditionOperator.Equal, objectId);
            query.Criteria.AddCondition("componenttype", ConditionOperator.Equal, componentType);
            var sol = query.AddLink("solution", "solutionid", "solutionid");
            sol.EntityAlias = "sol";
            sol.Columns = new ColumnSet("uniquename", "friendlyname", "version", "ismanaged");
            var pub = sol.AddLink("publisher", "publisherid", "publisherid", JoinOperator.LeftOuter);
            pub.EntityAlias = "pub";
            pub.Columns = new ColumnSet("friendlyname");

            return RetrieveAll(query, null).Select(e =>
            {
                var solutionRef = e.GetAttributeValue<EntityReference>("solutionid");
                var behavior = e.GetAttributeValue<OptionSetValue>("rootcomponentbehavior");
                return new WhereUsedRow
                {
                    Role = solutionRef != null && roleOf != null ? roleOf(solutionRef.Id) : string.Empty,
                    UniqueName = Aliased<string>(e, "sol.uniquename"),
                    DisplayName = Aliased<string>(e, "sol.friendlyname"),
                    Version = Aliased<string>(e, "sol.version"),
                    Managed = Aliased<bool>(e, "sol.ismanaged"),
                    Behavior = ComponentTypeCatalog.BehaviorText(behavior == null ? (int?)null : behavior.Value),
                    Publisher = Aliased<string>(e, "pub.friendlyname")
                };
            })
            .OrderBy(r => r.Role == "Source" ? 0 : r.Role == "Target" ? 1 : 2)
            .ThenBy(r => r.UniqueName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        }

        /// <summary>
        /// For the given components, lists the solutions outside <paramref name="excludeSolutionIds"/> that also contain them.
        /// The Default and Active solutions are ignored (they contain every unmanaged component).
        /// </summary>
        public Dictionary<ComponentKey, List<string>> LoadOtherSolutions(IList<ComponentKey> components, ICollection<Guid> excludeSolutionIds, Action<string> progress)
        {
            var result = new Dictionary<ComponentKey, List<string>>();
            var wanted = new HashSet<ComponentKey>(components);
            var ids = components.Select(c => c.ObjectId).Distinct().ToList();
            for (int i = 0; i < ids.Count; i += InChunk)
            {
                if (progress != null) progress("Checking other solutions for missing components... " + i.ToString("N0") + " / " + ids.Count.ToString("N0"));
                var chunk = ids.Skip(i).Take(InChunk).Cast<object>().ToArray();
                var query = new QueryExpression("solutioncomponent")
                {
                    ColumnSet = new ColumnSet("objectid", "componenttype", "solutionid")
                };
                query.Criteria.AddCondition("objectid", ConditionOperator.In, chunk);
                var sol = query.AddLink("solution", "solutionid", "solutionid");
                sol.EntityAlias = "sol";
                sol.Columns = new ColumnSet("uniquename");
                sol.LinkCriteria.AddCondition("uniquename", ConditionOperator.NotIn, "Default", "Active");

                foreach (var e in RetrieveAll(query, null))
                {
                    var solution = e.GetAttributeValue<EntityReference>("solutionid");
                    var type = e.GetAttributeValue<OptionSetValue>("componenttype");
                    if (solution == null || type == null || excludeSolutionIds.Contains(solution.Id)) continue;
                    var key = new ComponentKey(type.Value, e.GetAttributeValue<Guid>("objectid"));
                    if (!wanted.Contains(key)) continue;
                    List<string> list;
                    if (!result.TryGetValue(key, out list))
                    {
                        list = new List<string>();
                        result[key] = list;
                    }
                    var name = Aliased<string>(e, "sol.uniquename");
                    if (!string.IsNullOrEmpty(name) && !list.Contains(name)) list.Add(name);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ metadata

        public MetadataIndex LoadMetadataIndex(bool includeChildren)
        {
            try
            {
                return LoadMetadataIndexCore(includeChildren, true);
            }
            catch (Exception)
            {
                // Older platforms may not support "Keys" in RetrieveMetadataChanges: retry without it.
                return LoadMetadataIndexCore(includeChildren, false);
            }
        }

        private MetadataIndex LoadMetadataIndexCore(bool includeChildren, bool withKeys)
        {
            var entityProps = new MetadataPropertiesExpression("LogicalName", "MetadataId", "DisplayName", "PrimaryIdAttribute", "PrimaryNameAttribute");
            var query = new EntityQueryExpression { Properties = entityProps };
            if (includeChildren)
            {
                entityProps.PropertyNames.Add("Attributes");
                entityProps.PropertyNames.Add("OneToManyRelationships");
                entityProps.PropertyNames.Add("ManyToOneRelationships");
                entityProps.PropertyNames.Add("ManyToManyRelationships");
                query.AttributeQuery = new AttributeQueryExpression { Properties = new MetadataPropertiesExpression("LogicalName", "MetadataId") };
                query.RelationshipQuery = new RelationshipQueryExpression { Properties = new MetadataPropertiesExpression("SchemaName", "MetadataId") };
                if (withKeys)
                {
                    entityProps.PropertyNames.Add("Keys");
                    query.KeyQuery = new EntityKeyQueryExpression { Properties = new MetadataPropertiesExpression("LogicalName", "MetadataId") };
                }
            }

            var response = (RetrieveMetadataChangesResponse)_service.Execute(new RetrieveMetadataChangesRequest { Query = query });
            var index = new MetadataIndex { HasChildren = includeChildren };

            foreach (var em in response.EntityMetadata)
            {
                if (!em.MetadataId.HasValue || string.IsNullOrEmpty(em.LogicalName)) continue;
                var entry = new MetadataIndex.EntityEntry
                {
                    Id = em.MetadataId.Value,
                    LogicalName = em.LogicalName,
                    DisplayName = LabelText(em.DisplayName),
                    PrimaryIdAttribute = em.PrimaryIdAttribute,
                    PrimaryNameAttribute = em.PrimaryNameAttribute
                };
                index.EntitiesById[entry.Id] = entry;
                index.EntitiesByName[entry.LogicalName] = entry;

                if (!includeChildren) continue;

                if (em.Attributes != null)
                {
                    foreach (var a in em.Attributes)
                    {
                        if (a.MetadataId.HasValue) AddChild(index.Attributes, a.MetadataId.Value, a.LogicalName, em.LogicalName);
                    }
                }

                AddRelationships(index, em.OneToManyRelationships, em.LogicalName);
                AddRelationships(index, em.ManyToOneRelationships, em.LogicalName);
                AddRelationships(index, em.ManyToManyRelationships, em.LogicalName);

                if (withKeys && em.Keys != null)
                {
                    foreach (var k in em.Keys)
                    {
                        if (k.MetadataId.HasValue) AddChild(index.Keys, k.MetadataId.Value, k.LogicalName, em.LogicalName);
                    }
                }
            }

            return index;
        }

        public void LoadGlobalOptionSets(MetadataIndex index)
        {
            var response = (RetrieveAllOptionSetsResponse)_service.Execute(new RetrieveAllOptionSetsRequest());
            foreach (var os in response.OptionSetMetadata)
            {
                if (!os.MetadataId.HasValue) continue;
                index.OptionSets[os.MetadataId.Value] = new MetadataIndex.ChildEntry
                {
                    Name = os.Name,
                    Label = LabelText(os.DisplayName)
                };
            }
        }

        /// <summary>componenttype -> table logical name, from the solutioncomponentdefinition table.</summary>
        public Dictionary<int, string> LoadComponentDefinitions()
        {
            var map = new Dictionary<int, string>();
            var query = new QueryExpression("solutioncomponentdefinition") { ColumnSet = new ColumnSet(true) };
            foreach (var e in RetrieveAll(query, null))
            {
                int? type = null;
                object raw;
                if (e.Attributes.TryGetValue("solutioncomponenttype", out raw))
                {
                    if (raw is int) type = (int)raw;
                    else if (raw is OptionSetValue) type = ((OptionSetValue)raw).Value;
                }
                var entity = e.GetAttributeValue<string>("primaryentityname");
                if (type.HasValue && !string.IsNullOrEmpty(entity) && !map.ContainsKey(type.Value))
                {
                    map[type.Value] = entity;
                }
            }
            return map;
        }

        // ------------------------------------------------------------ record names

        public class RecordName
        {
            public string Name { get; set; }
            public string Parent { get; set; }
        }

        /// <summary>Reads the primary name (and owning table when known) of component records.</summary>
        public Dictionary<Guid, RecordName> LoadRecordNames(string entityName, string idAttribute, string nameAttribute, string parentAttribute, IList<Guid> ids)
        {
            var result = new Dictionary<Guid, RecordName>();
            var columns = new List<string> { nameAttribute };
            if (!string.IsNullOrEmpty(parentAttribute)) columns.Add(parentAttribute);

            for (int i = 0; i < ids.Count; i += InChunk)
            {
                var chunk = ids.Skip(i).Take(InChunk).Cast<object>().ToArray();
                var query = new QueryExpression(entityName) { ColumnSet = new ColumnSet(columns.ToArray()) };
                query.Criteria.AddCondition(idAttribute, ConditionOperator.In, chunk);
                foreach (var e in RetrieveAll(query, null))
                {
                    result[e.Id] = new RecordName
                    {
                        Name = ValueText(e, nameAttribute),
                        Parent = string.IsNullOrEmpty(parentAttribute) ? null : ValueText(e, parentAttribute)
                    };
                }
            }
            return result;
        }

        // ------------------------------------------------------------ helpers

        private IEnumerable<Entity> RetrieveAll(QueryExpression query, Action<int> onPage)
        {
            query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
            int total = 0;
            while (true)
            {
                var page = _service.RetrieveMultiple(query);
                foreach (var e in page.Entities)
                {
                    total++;
                    yield return e;
                }
                if (onPage != null) onPage(total);
                if (!page.MoreRecords) break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }

        private static void AddRelationships(MetadataIndex index, IEnumerable<RelationshipMetadataBase> relationships, string entityName)
        {
            if (relationships == null) return;
            foreach (var r in relationships)
            {
                if (r.MetadataId.HasValue) AddChild(index.Relationships, r.MetadataId.Value, r.SchemaName, entityName);
            }
        }

        private static void AddChild(Dictionary<Guid, MetadataIndex.ChildEntry> map, Guid id, string name, string entityName)
        {
            MetadataIndex.ChildEntry entry;
            if (!map.TryGetValue(id, out entry))
            {
                entry = new MetadataIndex.ChildEntry { Name = name };
                map[id] = entry;
            }
            if (!string.IsNullOrEmpty(entityName) && !entry.Entities.Contains(entityName, StringComparer.OrdinalIgnoreCase))
            {
                entry.Entities.Add(entityName);
            }
        }

        private static string LabelText(Label label)
        {
            if (label == null) return null;
            if (label.UserLocalizedLabel != null && !string.IsNullOrEmpty(label.UserLocalizedLabel.Label)) return label.UserLocalizedLabel.Label;
            var first = label.LocalizedLabels == null ? null : label.LocalizedLabels.FirstOrDefault(l => !string.IsNullOrEmpty(l.Label));
            return first == null ? null : first.Label;
        }

        private static T Aliased<T>(Entity e, string attribute)
        {
            var aliased = e.GetAttributeValue<AliasedValue>(attribute);
            if (aliased == null || aliased.Value == null) return default(T);
            return aliased.Value is T ? (T)aliased.Value : default(T);
        }

        private static string ValueText(Entity e, string attribute)
        {
            object value;
            if (!e.Attributes.TryGetValue(attribute, out value) || value == null) return null;
            var text = value as string;
            if (text != null) return text;
            var reference = value as EntityReference;
            if (reference != null) return reference.Name ?? reference.LogicalName;
            string formatted;
            if (e.FormattedValues.TryGetValue(attribute, out formatted)) return formatted;
            return Convert.ToString(value);
        }
    }
}
