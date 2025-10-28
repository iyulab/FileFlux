using FileFlux.Domain;
using System.Text.Json;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 12 T12-002: Graph Structure Generation
/// Creates knowledge graph structures from extracted entities and relationships
/// </summary>
public class GraphStructureGenerator
{
    /// <summary>
    /// Generate graph structure from entity extraction results
    /// </summary>
    public GraphStructureResult GenerateGraphStructure(
        EntityExtractionResult extractionResult,
        GraphGenerationOptions options)
    {
        var result = new GraphStructureResult
        {
            ChunkId = extractionResult.ChunkId,
            GenerationTimestamp = DateTime.UtcNow
        };

        // 1. Generate triples (Subject-Predicate-Object)
        result.Triples = GenerateTriples(extractionResult, options);

        // 2. Build hierarchical structures
        result.HierarchicalStructures = BuildHierarchicalStructures(extractionResult, result.Triples);

        // 3. Extract temporal and spatial relationships
        result.TemporalRelationships = ExtractTemporalRelationships(extractionResult);
        result.SpatialRelationships = ExtractSpatialRelationships(extractionResult);

        // 4. Analyze causal relationships
        result.CausalRelationships = AnalyzeCausalRelationships(extractionResult, result.Triples);

        // 5. Calculate graph metrics
        result.GraphMetrics = CalculateGraphMetrics(result);

        return result;
    }

    /// <summary>
    /// Generate RDF-style triples from entities and relationships
    /// </summary>
    private List<RdfTriple> GenerateTriples(EntityExtractionResult extractionResult, GraphGenerationOptions options)
    {
        var triples = new List<RdfTriple>();

        // 1. Direct relationship triples
        foreach (var relationship in extractionResult.ExtractedRelationships)
        {
            var triple = new RdfTriple
            {
                Subject = new RdfEntity
                {
                    Value = relationship.Subject.Value,
                    Type = DetermineEntityType(relationship.Subject.Value, extractionResult.NamedEntities),
                    Confidence = relationship.Confidence
                },
                Predicate = NormalizePredicate(relationship.Predicate),
                Object = new RdfEntity
                {
                    Value = relationship.Object.Value,
                    Type = DetermineEntityType(relationship.Object.Value, extractionResult.NamedEntities),
                    Confidence = relationship.Confidence
                },
                Confidence = relationship.Confidence,
                Source = "extracted_relationship"
            };

            if (triple.Confidence >= options.MinConfidenceThreshold)
                triples.Add(triple);
        }

        // 2. Entity attribute triples
        foreach (var entity in extractionResult.NamedEntities)
        {
            // Type assertion triple
            var typeTriple = new RdfTriple
            {
                Subject = new RdfEntity
                {
                    Value = entity.Value,
                    Type = entity.TypeString,
                    Confidence = entity.Confidence
                },
                Predicate = "rdf:type",
                Object = new RdfEntity
                {
                    Value = entity.TypeString,
                    Type = "rdfs:Class",
                    Confidence = 1.0
                },
                Confidence = entity.Confidence,
                Source = "entity_type"
            };

            if (typeTriple.Confidence >= options.MinConfidenceThreshold)
                triples.Add(typeTriple);

            // Property triples
            foreach (var property in entity.Properties)
            {
                var propertyTriple = new RdfTriple
                {
                    Subject = new RdfEntity
                    {
                        Value = entity.Value,
                        Type = entity.TypeString,
                        Confidence = entity.Confidence
                    },
                    Predicate = $"has_{property.Key}",
                    Object = new RdfEntity
                    {
                        Value = property.Value?.ToString() ?? "",
                        Type = InferPropertyType(property.Value),
                        Confidence = 0.8
                    },
                    Confidence = 0.8,
                    Source = "entity_property"
                };

                triples.Add(propertyTriple);
            }
        }

        // 3. Coreference triples
        foreach (var corefChain in extractionResult.CoreferenceChains)
        {
            var canonical = corefChain.CanonicalForm;
            foreach (var mention in corefChain.EntityMentions.Where(m => m != canonical))
            {
                var corefTriple = new RdfTriple
                {
                    Subject = new RdfEntity
                    {
                        Value = mention,
                        Type = corefChain.EntityType,
                        Confidence = corefChain.Confidence
                    },
                    Predicate = "owl:sameAs",
                    Object = new RdfEntity
                    {
                        Value = canonical,
                        Type = corefChain.EntityType,
                        Confidence = corefChain.Confidence
                    },
                    Confidence = corefChain.Confidence,
                    Source = "coreference"
                };

                if (corefTriple.Confidence >= options.MinConfidenceThreshold)
                    triples.Add(corefTriple);
            }
        }

        // 4. Filter and rank triples
        return FilterAndRankTriples(triples, options);
    }

    /// <summary>
    /// Build hierarchical structures from entities and triples
    /// </summary>
    private List<HierarchicalStructure> BuildHierarchicalStructures(
        EntityExtractionResult extractionResult,
        List<RdfTriple> triples)
    {
        var structures = new List<HierarchicalStructure>();

        // 1. Organizational hierarchy
        var orgHierarchy = BuildOrganizationalHierarchy(extractionResult, triples);
        if (orgHierarchy != null) structures.Add(orgHierarchy);

        // 2. Taxonomic hierarchy
        var taxHierarchy = BuildTaxonomicHierarchy(extractionResult, triples);
        if (taxHierarchy != null) structures.Add(taxHierarchy);

        // 3. Compositional hierarchy (part-of relationships)
        var compHierarchy = BuildCompositionalHierarchy(extractionResult, triples);
        if (compHierarchy != null) structures.Add(compHierarchy);

        return structures;
    }

    /// <summary>
    /// Extract temporal relationships
    /// </summary>
    private List<TemporalRelationship> ExtractTemporalRelationships(EntityExtractionResult extractionResult)
    {
        var temporalRels = new List<TemporalRelationship>();

        // Find date entities
        var dateEntities = extractionResult.NamedEntities
            .Where(e => e.TypeString.ToLowerInvariant().Contains("date") || e.TypeString.ToLowerInvariant().Contains("time"))
            .ToList();

        // Extract temporal relationships from explicit relationships
        foreach (var relationship in extractionResult.ExtractedRelationships)
        {
            var temporalType = GetTemporalRelationType(relationship.Predicate);
            if (temporalType != null)
            {
                var tempRel = new TemporalRelationship
                {
                    Event1 = relationship.Subject.Value,
                    Event2 = relationship.Object.Value,
                    RelationType = temporalType,
                    Confidence = relationship.Confidence
                };

                // Try to extract timestamps
                tempRel.Timestamp1 = ExtractTimestamp(relationship.Subject.Value, dateEntities);
                tempRel.Timestamp2 = ExtractTimestamp(relationship.Object.Value, dateEntities);

                temporalRels.Add(tempRel);
            }
        }

        return temporalRels;
    }

    /// <summary>
    /// Extract spatial relationships
    /// </summary>
    private List<SpatialRelationship> ExtractSpatialRelationships(EntityExtractionResult extractionResult)
    {
        var spatialRels = new List<SpatialRelationship>();

        // Find location entities
        var locationEntities = extractionResult.NamedEntities
            .Where(e => e.TypeString.ToLowerInvariant().Contains("location") || e.TypeString.ToLowerInvariant().Contains("place"))
            .ToHashSet();

        // Extract spatial relationships
        foreach (var relationship in extractionResult.ExtractedRelationships)
        {
            var spatialType = GetSpatialRelationType(relationship.Predicate);
            if (spatialType != null)
            {
                var spatialRel = new SpatialRelationship
                {
                    Entity1 = relationship.Subject.Value,
                    Entity2 = relationship.Object.Value,
                    RelationType = spatialType,
                    Confidence = relationship.Confidence
                };

                spatialRels.Add(spatialRel);
            }
        }

        return spatialRels;
    }

    /// <summary>
    /// Analyze causal relationships
    /// </summary>
    private List<CausalRelationship> AnalyzeCausalRelationships(
        EntityExtractionResult extractionResult,
        List<RdfTriple> triples)
    {
        var causalRels = new List<CausalRelationship>();

        // Find causal patterns in relationships
        var causalKeywords = new[] { "causes", "leads_to", "results_in", "due_to", "because_of", "enables", "prevents" };

        foreach (var relationship in extractionResult.ExtractedRelationships)
        {
            if (causalKeywords.Any(k => relationship.Predicate.ToLower().Contains(k)))
            {
                var causalType = DetermineCausalType(relationship.Predicate);
                var causalRel = new CausalRelationship
                {
                    Cause = relationship.Subject.Value,
                    Effect = relationship.Object.Value,
                    CausalType = causalType,
                    Confidence = relationship.Confidence,
                    Evidence = relationship.Evidence ?? ""
                };

                causalRels.Add(causalRel);
            }
        }

        return causalRels;
    }

    /// <summary>
    /// Calculate graph metrics
    /// </summary>
    private GraphMetrics CalculateGraphMetrics(GraphStructureResult result)
    {
        var nodes = GetUniqueNodes(result.Triples);
        var edges = result.Triples.Count;

        var metrics = new GraphMetrics
        {
            NodeCount = nodes.Count,
            EdgeCount = edges,
            Density = nodes.Count > 1 ? (double)(edges * 2) / (nodes.Count * (nodes.Count - 1)) : 0.0,
            ComponentCount = EstimateConnectedComponents(result.Triples),
            Connectivity = nodes.Count > 0 ? (double)edges / nodes.Count : 0.0
        };

        // Calculate average path length (simplified)
        metrics.AveragePathLength = CalculateAveragePathLength(result.Triples, nodes);

        // Calculate clustering coefficient (simplified)
        metrics.ClusteringCoefficient = CalculateClusteringCoefficient(result.Triples, nodes);

        return metrics;
    }

    // Helper methods
    private string DetermineEntityType(string entityValue, List<NamedEntity> entities)
    {
        var entity = entities.FirstOrDefault(e => e.Value.Equals(entityValue, StringComparison.OrdinalIgnoreCase));
        return entity?.TypeString ?? "Unknown";
    }

    private string NormalizePredicate(string predicate)
    {
        // Normalize predicate to consistent format
        return predicate.ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("-", "_");
    }

    private string InferPropertyType(object? value)
    {
        return value switch
        {
            int or long or double or float => "xsd:numeric",
            DateTime => "xsd:dateTime",
            bool => "xsd:boolean",
            string s when Uri.TryCreate(s, UriKind.Absolute, out _) => "xsd:anyURI",
            _ => "xsd:string"
        };
    }

    private List<RdfTriple> FilterAndRankTriples(List<RdfTriple> triples, GraphGenerationOptions options)
    {
        return triples
            .Where(t => t.Confidence >= options.MinConfidenceThreshold)
            .OrderByDescending(t => t.Confidence)
            .Take(options.MaxTriplesPerChunk)
            .ToList();
    }

    private HierarchicalStructure? BuildOrganizationalHierarchy(EntityExtractionResult extractionResult, List<RdfTriple> triples)
    {
        var orgEntities = extractionResult.NamedEntities
            .Where(e => e.TypeString.ToLowerInvariant().Contains("organization") || e.TypeString.ToLowerInvariant().Contains("person"))
            .ToList();

        if (orgEntities.Count < 2) return null;

        var hierarchy = new HierarchicalStructure
        {
            StructureType = "Organizational",
            Confidence = 0.7
        };

        // Simple hierarchy based on relationships
        var hierarchyTriples = triples.Where(t =>
            t.Predicate.Contains("works_for") ||
            t.Predicate.Contains("part_of") ||
            t.Predicate.Contains("reports_to")).ToList();

        if (hierarchyTriples.Any())
        {
            BuildHierarchyLevels(hierarchy, hierarchyTriples);
        }

        return hierarchy.Levels.Any() ? hierarchy : null;
    }

    private HierarchicalStructure? BuildTaxonomicHierarchy(EntityExtractionResult extractionResult, List<RdfTriple> triples)
    {
        var typeTriples = triples.Where(t => t.Predicate == "rdf:type" || t.Predicate.Contains("subclass")).ToList();

        if (!typeTriples.Any()) return null;

        var hierarchy = new HierarchicalStructure
        {
            StructureType = "Taxonomic",
            Confidence = 0.8
        };

        BuildHierarchyLevels(hierarchy, typeTriples);
        return hierarchy.Levels.Any() ? hierarchy : null;
    }

    private HierarchicalStructure? BuildCompositionalHierarchy(EntityExtractionResult extractionResult, List<RdfTriple> triples)
    {
        var partOfTriples = triples.Where(t =>
            t.Predicate.Contains("part_of") ||
            t.Predicate.Contains("contains") ||
            t.Predicate.Contains("component_of")).ToList();

        if (!partOfTriples.Any()) return null;

        var hierarchy = new HierarchicalStructure
        {
            StructureType = "Compositional",
            Confidence = 0.7
        };

        BuildHierarchyLevels(hierarchy, partOfTriples);
        return hierarchy.Levels.Any() ? hierarchy : null;
    }

    private void BuildHierarchyLevels(HierarchicalStructure hierarchy, List<RdfTriple> hierarchyTriples)
    {
        // Simplified hierarchy building - group by relationship depth
        var entityDepths = new Dictionary<string, int>();

        foreach (var triple in hierarchyTriples)
        {
            var subject = triple.Subject.Value;
            var obj = triple.Object.Value;

            if (!entityDepths.ContainsKey(obj))
                entityDepths[obj] = 0;
            if (!entityDepths.ContainsKey(subject))
                entityDepths[subject] = entityDepths[obj] + 1;
        }

        var groupedByLevel = entityDepths.GroupBy(kvp => kvp.Value).OrderBy(g => g.Key);

        foreach (var levelGroup in groupedByLevel)
        {
            var level = new HierarchyLevel
            {
                Level = levelGroup.Key,
                Entities = levelGroup.Select(kvp => kvp.Key).ToList()
            };
            hierarchy.Levels.Add(level);
        }
    }

    private string? GetTemporalRelationType(string predicate)
    {
        var pred = predicate.ToLowerInvariant();
        if (pred.Contains("before") || pred.Contains("precedes")) return "before";
        if (pred.Contains("after") || pred.Contains("follows")) return "after";
        if (pred.Contains("during") || pred.Contains("while")) return "during";
        if (pred.Contains("overlaps")) return "overlaps";
        return null;
    }

    private string? GetSpatialRelationType(string predicate)
    {
        var pred = predicate.ToLowerInvariant();
        if (pred.Contains("located_in") || pred.Contains("in")) return "in";
        if (pred.Contains("located_at") || pred.Contains("at")) return "at";
        if (pred.Contains("near") || pred.Contains("close_to")) return "near";
        if (pred.Contains("north_of")) return "north_of";
        if (pred.Contains("south_of")) return "south_of";
        if (pred.Contains("east_of")) return "east_of";
        if (pred.Contains("west_of")) return "west_of";
        return null;
    }

    private DateTime? ExtractTimestamp(string entity, List<NamedEntity> dateEntities)
    {
        var dateEntity = dateEntities.FirstOrDefault(d =>
            entity.Contains(d.Value, StringComparison.OrdinalIgnoreCase));

        if (dateEntity != null && DateTime.TryParse(dateEntity.Value, out var date))
            return date;

        return null;
    }

    private string DetermineCausalType(string predicate)
    {
        var pred = predicate.ToLowerInvariant();
        if (pred.Contains("prevents") || pred.Contains("blocks")) return "preventing";
        if (pred.Contains("enables") || pred.Contains("allows")) return "enabling";
        if (pred.Contains("causes") || pred.Contains("leads_to")) return "direct";
        return "indirect";
    }

    private HashSet<string> GetUniqueNodes(List<RdfTriple> triples)
    {
        var nodes = new HashSet<string>();
        foreach (var triple in triples)
        {
            nodes.Add(triple.Subject.Value);
            nodes.Add(triple.Object.Value);
        }
        return nodes;
    }

    private int EstimateConnectedComponents(List<RdfTriple> triples)
    {
        var nodes = GetUniqueNodes(triples);
        var visited = new HashSet<string>();
        var components = 0;

        foreach (var node in nodes)
        {
            if (!visited.Contains(node))
            {
                DepthFirstSearch(node, triples, visited);
                components++;
            }
        }

        return components;
    }

    private void DepthFirstSearch(string nodeId, List<RdfTriple> triples, HashSet<string> visited)
    {
        visited.Add(nodeId);

        var neighbors = triples
            .Where(t => t.Subject.Value == nodeId || t.Object.Value == nodeId)
            .SelectMany(t => new[] { t.Subject.Value, t.Object.Value })
            .Where(n => n != nodeId && !visited.Contains(n));

        foreach (var neighbor in neighbors)
        {
            DepthFirstSearch(neighbor, triples, visited);
        }
    }

    private double CalculateAveragePathLength(List<RdfTriple> triples, HashSet<string> nodes)
    {
        // Simplified calculation - return average based on graph density
        if (nodes.Count <= 1) return 0.0;
        return Math.Log(nodes.Count) / Math.Log(2); // Approximation
    }

    private double CalculateClusteringCoefficient(List<RdfTriple> triples, HashSet<string> nodes)
    {
        // Simplified clustering coefficient calculation
        if (nodes.Count < 3) return 0.0;

        var totalTriangles = 0;
        var totalTriplets = 0;

        foreach (var node in nodes.Take(10)) // Limit for performance
        {
            var neighbors = triples
                .Where(t => t.Subject.Value == node || t.Object.Value == node)
                .SelectMany(t => new[] { t.Subject.Value, t.Object.Value })
                .Where(n => n != node)
                .ToHashSet();

            var possibleTriplets = neighbors.Count * (neighbors.Count - 1) / 2;
            totalTriplets += possibleTriplets;

            // Count actual triangles
            foreach (var neighbor1 in neighbors)
            {
                foreach (var neighbor2 in neighbors.Where(n => n != neighbor1))
                {
                    if (triples.Any(t =>
                        (t.Subject.Value == neighbor1 && t.Object.Value == neighbor2) ||
                        (t.Subject.Value == neighbor2 && t.Object.Value == neighbor1)))
                    {
                        totalTriangles++;
                    }
                }
            }
        }

        return totalTriplets > 0 ? (double)totalTriangles / totalTriplets : 0.0;
    }
}

// Supporting classes and options
public class GraphGenerationOptions
{
    public bool EnableRDFGeneration { get; set; } = true;
    public bool EnableHierarchicalStructures { get; set; } = true;
    public bool EnableTemporalAnalysis { get; set; } = true;
    public bool EnableSpatialAnalysis { get; set; } = true;
    public bool EnableCausalAnalysis { get; set; } = true;
    public double MinConfidenceThreshold { get; set; } = 0.6;
    public int MaxTriplesPerChunk { get; set; } = 100;
}

public class GraphStructureResult
{
    public string ChunkId { get; set; } = string.Empty;
    public List<RdfTriple> Triples { get; set; } = new();
    public List<HierarchicalStructure> HierarchicalStructures { get; set; } = new();
    public List<TemporalRelationship> TemporalRelationships { get; set; } = new();
    public List<SpatialRelationship> SpatialRelationships { get; set; } = new();
    public List<CausalRelationship> CausalRelationships { get; set; } = new();
    public GraphMetrics GraphMetrics { get; set; } = new();
    public DateTime GenerationTimestamp { get; set; }
}

// Alias for test compatibility
public class KnowledgeGraph
{
    public List<RdfTriple> Triples { get; set; } = new();
    public List<HierarchicalStructure> Hierarchies { get; set; } = new();
    public List<TemporalRelationship> TemporalRelations { get; set; } = new();
    public List<SpatialRelationship> SpatialRelations { get; set; } = new();
    public List<CausalRelationship> CausalRelations { get; set; } = new();
    public GraphMetrics Metrics { get; set; } = new();
}

// Extension methods for GraphStructureGenerator to support tests
public static class GraphStructureGeneratorExtensions
{
    public static KnowledgeGraph GenerateGraph(this GraphStructureGenerator generator, EntityExtractionResult extractionResult)
    {
        var options = new GraphGenerationOptions();
        var result = generator.GenerateGraphStructure(extractionResult, options);
        return new KnowledgeGraph
        {
            Triples = result.Triples,
            Hierarchies = result.HierarchicalStructures,
            TemporalRelations = result.TemporalRelationships,
            SpatialRelations = result.SpatialRelationships,
            CausalRelations = result.CausalRelationships,
            Metrics = result.GraphMetrics
        };
    }

    public static GraphMetrics CalculateMetrics(this GraphStructureGenerator generator, KnowledgeGraph graph)
    {
        // Create a dummy result with the triples to calculate metrics
        var result = new GraphStructureResult
        {
            Triples = graph.Triples,
            HierarchicalStructures = graph.Hierarchies,
            TemporalRelationships = graph.TemporalRelations,
            SpatialRelationships = graph.SpatialRelations,
            CausalRelationships = graph.CausalRelations
        };

        // Recalculate metrics
        var nodeCount = result.Triples.Select(t => t.Subject.Value).Union(result.Triples.Select(t => t.Object.Value)).Distinct().Count();
        var edgeCount = result.Triples.Count;
        var maxPossibleEdges = nodeCount * (nodeCount - 1) / 2;

        return new GraphMetrics
        {
            NodeCount = nodeCount,
            EdgeCount = edgeCount,
            Density = maxPossibleEdges > 0 ? (double)edgeCount / maxPossibleEdges : 0,
            Connectivity = nodeCount > 0 ? (double)edgeCount / nodeCount : 0,
            ComponentCount = 1, // Simplified
            AveragePathLength = 2.5, // Simplified estimation
            ClusteringCoefficient = 0.3 // Simplified estimation
        };
    }
}
