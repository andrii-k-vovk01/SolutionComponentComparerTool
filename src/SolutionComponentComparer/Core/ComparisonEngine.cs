using System;
using System.Collections.Generic;
using System.Linq;

namespace SolutionComponentComparer.Core
{
    /// <summary>
    /// Pure comparison logic. No Dataverse dependency, so it can be unit tested.
    /// </summary>
    public static class ComparisonEngine
    {
        public static string StatusText(ComparisonStatus status)
        {
            switch (status)
            {
                case ComparisonStatus.MissingInTarget: return "Missing in target";
                case ComparisonStatus.BehaviorDiffers: return "In both, target has less";
                case ComparisonStatus.OnlyInTarget: return "Only in target";
                case ComparisonStatus.CoveredByTargetTable: return "Covered by target table";
                case ComparisonStatus.CoveredBySourceTable: return "Covered by source table";
                case ComparisonStatus.InBoth: return "In both";
                case ComparisonStatus.Listed: return "Listed";
                default: return status.ToString();
            }
        }

        public static ComparisonResult Compare(
            IList<SolutionRef> solutions,
            IList<ComponentMembership> memberships,
            IComponentNameProvider names)
        {
            if (solutions == null) throw new ArgumentNullException("solutions");
            if (memberships == null) throw new ArgumentNullException("memberships");
            if (names == null) throw new ArgumentNullException("names");

            var result = new ComparisonResult();
            var solutionById = new Dictionary<Guid, SolutionRef>();
            foreach (var s in solutions)
            {
                if (s.Role == SolutionRole.None || solutionById.ContainsKey(s.Id)) continue;
                solutionById[s.Id] = s;
                if (s.Role == SolutionRole.Source) result.SourceSolutions.Add(s);
                else result.TargetSolutions.Add(s);
            }

            bool hasTargets = result.TargetSolutions.Count > 0;

            // Group memberships by component.
            var groups = new Dictionary<ComponentKey, List<ComponentMembership>>();
            foreach (var m in memberships)
            {
                if (!solutionById.ContainsKey(m.SolutionId)) continue;
                var key = new ComponentKey(m.ComponentType, m.ObjectId);
                List<ComponentMembership> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<ComponentMembership>();
                    groups[key] = list;
                }
                list.Add(m);
            }

            // Describe every component once.
            var infos = new Dictionary<ComponentKey, ComponentInfo>();
            foreach (var g in groups)
            {
                var label = g.Value.Select(x => x.TypeLabel).FirstOrDefault(x => !string.IsNullOrEmpty(x));
                var info = names.Describe(g.Key.Type, g.Key.ObjectId, label) ?? new ComponentInfo();
                if (string.IsNullOrEmpty(info.TypeName)) info.TypeName = ComponentTypeCatalog.GetTypeName(g.Key.Type, label);
                if (string.IsNullOrEmpty(info.Name)) info.Name = "(unresolved)";
                infos[g.Key] = info;
            }

            // Tables included with "all subcomponents" on each side.
            var sourceFullTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var targetFullTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                if (g.Key.Type != ComponentTypeCatalog.Entity) continue;
                var logicalName = infos[g.Key].EntityLogicalName;
                if (string.IsNullOrEmpty(logicalName)) continue;
                foreach (var m in g.Value)
                {
                    if (m.RootBehavior != 0) continue;
                    if (solutionById[m.SolutionId].Role == SolutionRole.Source) sourceFullTables.Add(logicalName);
                    else targetFullTables.Add(logicalName);
                }
            }

            foreach (var g in groups)
            {
                var info = infos[g.Key];
                var src = g.Value.Where(m => solutionById[m.SolutionId].Role == SolutionRole.Source).ToList();
                var tgt = g.Value.Where(m => solutionById[m.SolutionId].Role == SolutionRole.Target).ToList();

                ComparisonStatus status;
                if (!hasTargets)
                {
                    status = ComparisonStatus.Listed;
                }
                else if (src.Count > 0 && tgt.Count > 0)
                {
                    int srcBreadth = src.Max(m => ComponentTypeCatalog.BehaviorBreadth(m.RootBehavior));
                    int tgtBreadth = tgt.Max(m => ComponentTypeCatalog.BehaviorBreadth(m.RootBehavior));
                    status = srcBreadth > tgtBreadth ? ComparisonStatus.BehaviorDiffers : ComparisonStatus.InBoth;
                }
                else if (src.Count > 0)
                {
                    status = info.ParentEntities.Any(targetFullTables.Contains)
                        ? ComparisonStatus.CoveredByTargetTable
                        : ComparisonStatus.MissingInTarget;
                }
                else
                {
                    status = info.ParentEntities.Any(sourceFullTables.Contains)
                        ? ComparisonStatus.CoveredBySourceTable
                        : ComparisonStatus.OnlyInTarget;
                }

                result.Rows.Add(new ComparisonRow
                {
                    Status = status,
                    StatusText = StatusText(status),
                    ComponentType = g.Key.Type,
                    TypeName = info.TypeName,
                    Name = info.Name,
                    Label = info.Label ?? string.Empty,
                    ParentTable = string.Join(", ", info.ParentEntities.Distinct(StringComparer.OrdinalIgnoreCase)),
                    SourceCount = src.Select(m => m.SolutionId).Distinct().Count(),
                    SourceSolutions = JoinSolutionNames(src, solutionById),
                    TargetCount = tgt.Select(m => m.SolutionId).Distinct().Count(),
                    TargetSolutions = JoinSolutionNames(tgt, solutionById),
                    SourceBehavior = JoinBehaviors(src),
                    TargetBehavior = JoinBehaviors(tgt),
                    ObjectId = g.Key.ObjectId
                });

                int count;
                result.StatusCounts.TryGetValue(status, out count);
                result.StatusCounts[status] = count + 1;

                // Duplicates: same component in two or more of the selected solutions.
                var distinctSolutions = g.Value.Select(m => m.SolutionId).Distinct().ToList();
                if (distinctSolutions.Count > 1)
                {
                    result.Duplicates.Add(new DuplicateRow
                    {
                        ComponentType = g.Key.Type,
                        TypeName = info.TypeName,
                        Name = info.Name,
                        Label = info.Label ?? string.Empty,
                        ParentTable = string.Join(", ", info.ParentEntities.Distinct(StringComparer.OrdinalIgnoreCase)),
                        SolutionCount = distinctSolutions.Count,
                        SourceCount = distinctSolutions.Count(id => solutionById[id].Role == SolutionRole.Source),
                        TargetCount = distinctSolutions.Count(id => solutionById[id].Role == SolutionRole.Target),
                        Solutions = string.Join(", ", distinctSolutions
                            .Select(id => solutionById[id])
                            .OrderBy(s => s.Role)
                            .ThenBy(s => s.UniqueName, StringComparer.OrdinalIgnoreCase)
                            .Select(s => s.UniqueName + (s.Role == SolutionRole.Source ? " [S]" : " [T]"))),
                        ObjectId = g.Key.ObjectId
                    });
                }
            }

            result.Rows.Sort((a, b) =>
            {
                int c = ((int)a.Status).CompareTo((int)b.Status);
                if (c != 0) return c;
                c = string.Compare(a.TypeName, b.TypeName, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                c = string.Compare(a.ParentTable, b.ParentTable, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            result.Duplicates.Sort((a, b) =>
            {
                int c = b.TargetCount.CompareTo(a.TargetCount);
                if (c != 0) return c;
                c = string.Compare(a.TypeName, b.TypeName, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        private static string JoinSolutionNames(IEnumerable<ComponentMembership> items, Dictionary<Guid, SolutionRef> solutionById)
        {
            return string.Join(", ", items
                .Select(m => solutionById[m.SolutionId].UniqueName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        private static string JoinBehaviors(IEnumerable<ComponentMembership> items)
        {
            return string.Join(" / ", items
                .Where(m => m.RootBehavior.HasValue)
                .OrderBy(m => m.RootBehavior.Value)
                .Select(m => ComponentTypeCatalog.BehaviorText(m.RootBehavior))
                .Distinct());
        }
    }
}
