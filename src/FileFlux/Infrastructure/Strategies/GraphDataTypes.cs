using System;
using System.Collections.Generic;

namespace FileFlux.Infrastructure.Strategies
{
    // Core graph data types used across Phase 12 components
    
    public class RdfEntity
    {
        public string Value { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
    }

    public class RdfTriple
    {
        public RdfEntity Subject { get; set; } = new RdfEntity();
        public string Predicate { get; set; } = string.Empty;
        public RdfEntity Object { get; set; } = new RdfEntity();
        public double Confidence { get; set; }
        public string Source { get; set; } = string.Empty;
    }


    public class HierarchicalStructure
    {
        public string RootEntity { get; set; } = string.Empty;
        public List<HierarchyLevel> Levels { get; set; } = new List<HierarchyLevel>();
        public string StructureType { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public class HierarchyLevel
    {
        public int Level { get; set; }
        public List<string> Entities { get; set; } = new List<string>();
        public string RelationType { get; set; } = string.Empty;
    }

    public class TemporalRelationship
    {
        public string Event1 { get; set; } = string.Empty;
        public string Event2 { get; set; } = string.Empty;
        public string RelationType { get; set; } = string.Empty; // "before", "after", "during", "overlaps"
        public DateTime? Timestamp1 { get; set; }
        public DateTime? Timestamp2 { get; set; }
        public double Confidence { get; set; }
    }

    public class SpatialRelationship
    {
        public string Entity1 { get; set; } = string.Empty;
        public string Entity2 { get; set; } = string.Empty;
        public string RelationType { get; set; } = string.Empty; // "near", "in", "on", "north_of", etc.
        public double Confidence { get; set; }
        public Dictionary<string, object> SpatialProperties { get; set; } = new Dictionary<string, object>();
    }

    public class CausalRelationship
    {
        public string Cause { get; set; } = string.Empty;
        public string Effect { get; set; } = string.Empty;
        public string CausalType { get; set; } = string.Empty; // "direct", "indirect", "enabling", "preventing"
        public double Confidence { get; set; }
        public string Evidence { get; set; } = string.Empty;
    }

    public class GraphMetrics
    {
        public int NodeCount { get; set; }
        public int EdgeCount { get; set; }
        public double Density { get; set; }
        public double Connectivity { get; set; }
        public int ComponentCount { get; set; }
        public double AveragePathLength { get; set; }
        public double ClusteringCoefficient { get; set; }
    }
}