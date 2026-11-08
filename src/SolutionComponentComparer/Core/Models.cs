using System;
using System.Collections.Generic;

namespace SolutionComponentComparer.Core
{
    /// <summary>Role a solution plays in a comparison.</summary>
    public enum SolutionRole
    {
        None = 0,
        Source = 1,
        Target = 2
    }

    /// <summary>A solution taking part in a comparison.</summary>
    public class SolutionRef
    {
        public Guid Id { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
        public string Version { get; set; }
        public bool IsManaged { get; set; }
        public SolutionRole Role { get; set; }
    }

    /// <summary>One row of the solutioncomponent table: "component X is in solution Y".</summary>
    public class ComponentMembership
    {
        public Guid SolutionId { get; set; }
        public Guid ObjectId { get; set; }
        public int ComponentType { get; set; }

        /// <summary>rootcomponentbehavior: 0 = include all subcomponents, 1 = do not include subcomponents, 2 = shell only, null = n/a.</summary>
        public int? RootBehavior { get; set; }

        /// <summary>Formatted label of componenttype as returned by the server (may be null).</summary>
        public string TypeLabel { get; set; }
    }

    /// <summary>Identity of a component: (componenttype, objectid).</summary>
    public struct ComponentKey : IEquatable<ComponentKey>
    {
        public ComponentKey(int type, Guid objectId)
        {
            Type = type;
            ObjectId = objectId;
        }

        public int Type { get; private set; }
        public Guid ObjectId { get; private set; }

        public bool Equals(ComponentKey other)
        {
            return Type == other.Type && ObjectId == other.ObjectId;
        }

        public override bool Equals(object obj)
        {
            return obj is ComponentKey && Equals((ComponentKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked { return (Type * 397) ^ ObjectId.GetHashCode(); }
        }

        public override string ToString()
        {
            return Type + ":" + ObjectId;
        }
    }

    /// <summary>Human readable description of a component.</summary>
    public class ComponentInfo
    {
        public ComponentInfo()
        {
            ParentEntities = new List<string>();
        }

        public string TypeName { get; set; }

        /// <summary>Logical / schema / record name.</summary>
        public string Name { get; set; }

        /// <summary>Display label, when there is one.</summary>
        public string Label { get; set; }

        /// <summary>For tables (type 1): the table logical name.</summary>
        public string EntityLogicalName { get; set; }

        /// <summary>Tables this component belongs to (attribute/form/view/relationship...).</summary>
        public List<string> ParentEntities { get; private set; }
    }

    public interface IComponentNameProvider
    {
        ComponentInfo Describe(int componentType, Guid objectId, string typeLabelFromServer);
    }

    public enum ComparisonStatus
    {
        /// <summary>In a source solution, not in any target solution. This is the one that matters before deleting.</summary>
        MissingInTarget = 0,

        /// <summary>In both, but sources include more of it (e.g. table with all subcomponents vs. table metadata only).</summary>
        BehaviorDiffers = 1,

        /// <summary>In a target solution, not in any source solution.</summary>
        OnlyInTarget = 2,

        /// <summary>Not listed in targets by itself, but its table is in a target with "include all subcomponents".</summary>
        CoveredByTargetTable = 3,

        /// <summary>Not listed in sources by itself, but its table is in a source with "include all subcomponents".</summary>
        CoveredBySourceTable = 4,

        /// <summary>In at least one source and at least one target.</summary>
        InBoth = 5,

        /// <summary>No target solutions selected: plain component list.</summary>
        Listed = 6
    }

    public class ComparisonRow
    {
        public ComparisonStatus Status { get; set; }
        public string StatusText { get; set; }
        public int ComponentType { get; set; }
        public string TypeName { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string ParentTable { get; set; }
        public int SourceCount { get; set; }
        public string SourceSolutions { get; set; }
        public int TargetCount { get; set; }
        public string TargetSolutions { get; set; }
        public string SourceBehavior { get; set; }
        public string TargetBehavior { get; set; }

        /// <summary>
        /// For "missing in target" rows: other solutions (not selected, excluding Default/Active) that also contain the component.
        /// Empty means nothing else holds it.
        /// </summary>
        public string OtherSolutions { get; set; }

        public Guid ObjectId { get; set; }
    }

    public class DuplicateRow
    {
        public int ComponentType { get; set; }
        public string TypeName { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string ParentTable { get; set; }
        public int SolutionCount { get; set; }
        public int SourceCount { get; set; }
        public int TargetCount { get; set; }
        public string Solutions { get; set; }
        public Guid ObjectId { get; set; }
    }

    public class ComparisonResult
    {
        public ComparisonResult()
        {
            Rows = new List<ComparisonRow>();
            Duplicates = new List<DuplicateRow>();
            SourceSolutions = new List<SolutionRef>();
            TargetSolutions = new List<SolutionRef>();
            StatusCounts = new Dictionary<ComparisonStatus, int>();
            GeneratedOn = DateTime.Now;
        }

        public string EnvironmentName { get; set; }
        public DateTime GeneratedOn { get; set; }
        public List<SolutionRef> SourceSolutions { get; private set; }
        public List<SolutionRef> TargetSolutions { get; private set; }
        public List<ComparisonRow> Rows { get; private set; }
        public List<DuplicateRow> Duplicates { get; private set; }
        public Dictionary<ComparisonStatus, int> StatusCounts { get; private set; }

        public int Count(ComparisonStatus status)
        {
            int n;
            return StatusCounts.TryGetValue(status, out n) ? n : 0;
        }
    }
}
