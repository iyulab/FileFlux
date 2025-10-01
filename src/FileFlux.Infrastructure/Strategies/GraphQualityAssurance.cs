using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies
{
    public class GraphQualityAssurance
    {
        private readonly Dictionary<string, QualityRule> _qualityRules;
        private readonly HashSet<string> _processedNodes;
        private readonly Dictionary<string, int> _nodeDepths;

        public GraphQualityAssurance()
        {
            _qualityRules = InitializeQualityRules();
            _processedNodes = new HashSet<string>();
            _nodeDepths = new Dictionary<string, int>();
        }

        public GraphQualityResult AssessGraphQuality(OntologyMappingResult ontologyResult, GraphQualityOptions options)
        {
            var result = new GraphQualityResult
            {
                ChunkId = ontologyResult.ChunkId,
                SourceOntology = ontologyResult
            };

            // Perform comprehensive quality assessment
            result.ConsistencyReport = ValidateConsistency(ontologyResult, options);
            result.CompletenessReport = EvaluateCompleteness(ontologyResult, options);
            result.CyclicReferenceReport = DetectCyclicReferences(ontologyResult, options);
            result.OrphanNodeReport = IdentifyOrphanNodes(ontologyResult, options);
            result.StructuralIntegrityReport = ValidateStructuralIntegrity(ontologyResult, options);
            
            // Calculate overall quality scores
            result.QualityScores = CalculateQualityScores(result);
            
            // Generate improvement recommendations
            result.ImprovementRecommendations = GenerateImprovementRecommendations(result, options);
            
            // Apply quality fixes if enabled
            if (options.AutoFix)
            {
                result.AutoFixResults = ApplyQualityFixes(ontologyResult, result, options);
            }

            return result;
        }

        private ConsistencyReport ValidateConsistency(OntologyMappingResult ontologyResult, GraphQualityOptions options)
        {
            var report = new ConsistencyReport();
            var violations = new List<ConsistencyViolation>();

            // Rule 1: Type consistency - entities should have consistent types
            violations.AddRange(ValidateTypeConsistency(ontologyResult));

            // Rule 2: Relationship domain/range consistency
            violations.AddRange(ValidateRelationshipConsistency(ontologyResult));

            // Rule 3: Property value consistency
            violations.AddRange(ValidatePropertyConsistency(ontologyResult));

            // Rule 4: Ontology schema adherence
            violations.AddRange(ValidateSchemaAdherence(ontologyResult));

            report.Violations = violations;
            report.ConsistencyScore = CalculateConsistencyScore(violations, ontologyResult);
            report.IsConsistent = violations.Count(v => v.Severity == "Error") == 0;

            return report;
        }

        private List<ConsistencyViolation> ValidateTypeConsistency(OntologyMappingResult ontologyResult)
        {
            var violations = new List<ConsistencyViolation>();
            
            // Group entities by value to check for type conflicts
            var entityGroups = ontologyResult.TypedEntities
                .GroupBy(te => te.Entity.Value.ToLower())
                .Where(g => g.Select(te => te.TypeDefinition?.TypeName).Distinct().Count() > 1);

            foreach (var group in entityGroups)
            {
                var distinctTypes = group.Select(te => te.TypeDefinition?.TypeName ?? "Unknown").Distinct().ToList();
                violations.Add(new ConsistencyViolation
                {
                    ViolationType = "TypeInconsistency",
                    EntityValue = group.Key,
                    Description = $"Entity '{group.Key}' has conflicting types: {string.Join(", ", distinctTypes)}",
                    Severity = "Warning",
                    ConflictingTypes = distinctTypes,
                    SuggestedResolution = "Determine the most frequent or most specific type"
                });
            }

            return violations;
        }

        private List<ConsistencyViolation> ValidateRelationshipConsistency(OntologyMappingResult ontologyResult)
        {
            var violations = new List<ConsistencyViolation>();

            foreach (var mappedTriple in ontologyResult.MappedTriples)
            {
                var relationshipType = ontologyResult.InferredSchema.RelationshipTypes
                    .FirstOrDefault(rt => rt.RelationshipName.Equals(mappedTriple.MappedPredicate, StringComparison.OrdinalIgnoreCase));

                if (relationshipType != null)
                {
                    // Check domain constraints
                    if (!relationshipType.DomainTypes.Contains(mappedTriple.MappedSubject.OntologyType))
                    {
                        violations.Add(new ConsistencyViolation
                        {
                            ViolationType = "DomainViolation",
                            EntityValue = mappedTriple.MappedSubject.NormalizedValue,
                            Description = $"Subject type '{mappedTriple.MappedSubject.OntologyType}' not allowed for relationship '{mappedTriple.MappedPredicate}'",
                            Severity = "Error",
                            RelatedTriple = mappedTriple.OriginalTriple,
                            SuggestedResolution = $"Expected domain types: {string.Join(", ", relationshipType.DomainTypes)}"
                        });
                    }

                    // Check range constraints
                    if (!relationshipType.RangeTypes.Contains(mappedTriple.MappedObject.OntologyType))
                    {
                        violations.Add(new ConsistencyViolation
                        {
                            ViolationType = "RangeViolation",
                            EntityValue = mappedTriple.MappedObject.NormalizedValue,
                            Description = $"Object type '{mappedTriple.MappedObject.OntologyType}' not allowed for relationship '{mappedTriple.MappedPredicate}'",
                            Severity = "Error",
                            RelatedTriple = mappedTriple.OriginalTriple,
                            SuggestedResolution = $"Expected range types: {string.Join(", ", relationshipType.RangeTypes)}"
                        });
                    }
                }
            }

            return violations;
        }

        private List<ConsistencyViolation> ValidatePropertyConsistency(OntologyMappingResult ontologyResult)
        {
            var violations = new List<ConsistencyViolation>();

            // Check for properties with inconsistent data types
            var propertyGroups = ontologyResult.PropertyMappings
                .GroupBy(pm => pm.MappedProperty)
                .Where(g => g.Select(pm => InferDataType(pm.TransformationApplied)).Distinct().Count() > 1);

            foreach (var group in propertyGroups)
            {
                var distinctTypes = group.Select(pm => InferDataType(pm.TransformationApplied)).Distinct().ToList();
                violations.Add(new ConsistencyViolation
                {
                    ViolationType = "PropertyTypeInconsistency",
                    EntityValue = group.Key,
                    Description = $"Property '{group.Key}' has values with conflicting data types: {string.Join(", ", distinctTypes)}",
                    Severity = "Warning",
                    ConflictingTypes = distinctTypes,
                    SuggestedResolution = "Standardize property values to a single data type"
                });
            }

            return violations;
        }

        private List<ConsistencyViolation> ValidateSchemaAdherence(OntologyMappingResult ontologyResult)
        {
            var violations = new List<ConsistencyViolation>();

            // Check if mapped triples follow the inferred schema
            foreach (var mappedTriple in ontologyResult.MappedTriples)
            {
                if (mappedTriple.ConfidenceScore < 0.5)
                {
                    violations.Add(new ConsistencyViolation
                    {
                        ViolationType = "LowConfidenceMapping",
                        EntityValue = $"{mappedTriple.MappedSubject.NormalizedValue} -> {mappedTriple.MappedObject.NormalizedValue}",
                        Description = $"Triple mapping has low confidence score: {mappedTriple.ConfidenceScore:F2}",
                        Severity = "Warning",
                        RelatedTriple = mappedTriple.OriginalTriple,
                        SuggestedResolution = "Review and validate the mapping manually"
                    });
                }
            }

            return violations;
        }

        private double CalculateConsistencyScore(List<ConsistencyViolation> violations, OntologyMappingResult ontologyResult)
        {
            if (ontologyResult.MappedTriples.Count == 0) return 1.0;

            var errorWeight = 1.0;
            var warningWeight = 0.5;
            var infoWeight = 0.1;

            var totalWeight = violations.Sum(v => v.Severity switch
            {
                "Error" => errorWeight,
                "Warning" => warningWeight,
                "Info" => infoWeight,
                _ => 0.0
            });

            var maxPossibleViolations = ontologyResult.MappedTriples.Count * errorWeight;
            return Math.Max(0.0, 1.0 - (totalWeight / maxPossibleViolations));
        }

        private CompletenessReport EvaluateCompleteness(OntologyMappingResult ontologyResult, GraphQualityOptions options)
        {
            var report = new CompletenessReport();

            // Entity completeness - how many entities have complete type information
            var entitiesWithTypes = ontologyResult.TypedEntities.Count(te => te.TypeDefinition != null);
            var totalEntities = ontologyResult.TypedEntities.Count;
            report.EntityCompletenessScore = totalEntities > 0 ? (double)entitiesWithTypes / totalEntities : 1.0;

            // Relationship completeness - how many relationships are properly typed
            var typedRelationships = ontologyResult.MappedTriples.Count(mt => 
                ontologyResult.InferredSchema.RelationshipTypes.Any(rt => 
                    rt.RelationshipName.Equals(mt.MappedPredicate, StringComparison.OrdinalIgnoreCase)));
            var totalRelationships = ontologyResult.MappedTriples.Count;
            report.RelationshipCompletenessScore = totalRelationships > 0 ? (double)typedRelationships / totalRelationships : 1.0;

            // Property completeness - how many entities have all expected properties
            report.PropertyCompletenessScore = CalculatePropertyCompleteness(ontologyResult);

            // Schema coverage - how much of the inferred schema is actually used
            var usedEntityTypes = ontologyResult.TypedEntities.Select(te => te.TypeDefinition?.TypeName).Distinct().Count();
            var totalSchemaTypes = ontologyResult.InferredSchema.EntityTypes.Count;
            report.SchemaCoverageScore = totalSchemaTypes > 0 ? (double)usedEntityTypes / totalSchemaTypes : 1.0;

            // Overall completeness score
            report.OverallCompletenessScore = (
                report.EntityCompletenessScore +
                report.RelationshipCompletenessScore +
                report.PropertyCompletenessScore +
                report.SchemaCoverageScore
            ) / 4.0;

            // Identify missing elements
            report.MissingElements = IdentifyMissingElements(ontologyResult);

            return report;
        }

        private double CalculatePropertyCompleteness(OntologyMappingResult ontologyResult)
        {
            double totalCompleteness = 0.0;
            int entityCount = 0;

            foreach (var typedEntity in ontologyResult.TypedEntities.Where(te => te.TypeDefinition != null))
            {
                var requiredProperties = typedEntity.TypeDefinition.Properties.Where(p => p.IsRequired).ToList();
                if (requiredProperties.Count == 0) continue;

                var entityMappings = ontologyResult.PropertyMappings.Where(pm =>
                    pm.SourceTriple.Subject.Value.Equals(typedEntity.Entity.Value, StringComparison.OrdinalIgnoreCase)).ToList();

                var fulfilledProperties = requiredProperties.Count(rp =>
                    entityMappings.Any(em => em.MappedProperty.Equals(rp.PropertyName, StringComparison.OrdinalIgnoreCase)));

                totalCompleteness += (double)fulfilledProperties / requiredProperties.Count;
                entityCount++;
            }

            return entityCount > 0 ? totalCompleteness / entityCount : 1.0;
        }

        private List<MissingElement> IdentifyMissingElements(OntologyMappingResult ontologyResult)
        {
            var missingElements = new List<MissingElement>();

            // Find entities mentioned in relationships but not explicitly typed
            var allSubjects = ontologyResult.MappedTriples.Select(mt => mt.MappedSubject.NormalizedValue).ToHashSet();
            var allObjects = ontologyResult.MappedTriples.Select(mt => mt.MappedObject.NormalizedValue).ToHashSet();
            var allEntities = allSubjects.Union(allObjects).ToHashSet();
            var typedEntityValues = ontologyResult.TypedEntities.Select(te => te.Entity.Value).ToHashSet();

            foreach (var entityValue in allEntities.Except(typedEntityValues))
            {
                missingElements.Add(new MissingElement
                {
                    ElementType = "Entity",
                    ElementValue = entityValue,
                    Description = $"Entity '{entityValue}' appears in relationships but lacks type information",
                    ImpactLevel = "Medium"
                });
            }

            return missingElements;
        }

        private CyclicReferenceReport DetectCyclicReferences(OntologyMappingResult ontologyResult, GraphQualityOptions options)
        {
            var report = new CyclicReferenceReport();
            var cycles = new List<CyclicReference>();

            // Build adjacency list representation of the graph
            var adjacencyList = BuildAdjacencyList(ontologyResult.MappedTriples);

            // Use DFS to detect cycles
            _processedNodes.Clear();
            var recursionStack = new HashSet<string>();
            var currentPath = new List<string>();

            foreach (var node in adjacencyList.Keys)
            {
                if (!_processedNodes.Contains(node))
                {
                    var detectedCycles = DetectCyclesFromNode(node, adjacencyList, recursionStack, currentPath);
                    cycles.AddRange(detectedCycles);
                }
            }

            report.DetectedCycles = cycles;
            report.CycleCount = cycles.Count;
            report.HasCycles = cycles.Count > 0;

            // Categorize cycles by type and severity
            report.CyclicReferencesByType = CategorizeCycles(cycles);
            report.SeverityDistribution = CalculateCycleSeverity(cycles);

            return report;
        }

        private Dictionary<string, List<string>> BuildAdjacencyList(List<MappedTriple> mappedTriples)
        {
            var adjacencyList = new Dictionary<string, List<string>>();

            foreach (var triple in mappedTriples)
            {
                var subject = triple.MappedSubject.NormalizedValue;
                var obj = triple.MappedObject.NormalizedValue;

                if (!adjacencyList.ContainsKey(subject))
                    adjacencyList[subject] = new List<string>();

                adjacencyList[subject].Add(obj);

                // Ensure object node exists in the adjacency list
                if (!adjacencyList.ContainsKey(obj))
                    adjacencyList[obj] = new List<string>();
            }

            return adjacencyList;
        }

        private List<CyclicReference> DetectCyclesFromNode(string node, Dictionary<string, List<string>> adjacencyList,
            HashSet<string> recursionStack, List<string> currentPath)
        {
            var cycles = new List<CyclicReference>();

            _processedNodes.Add(node);
            recursionStack.Add(node);
            currentPath.Add(node);

            foreach (var neighbor in adjacencyList[node])
            {
                if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    var cycleStartIndex = currentPath.IndexOf(neighbor);
                    var cyclePath = currentPath.Skip(cycleStartIndex).Concat(new[] { neighbor }).ToList();

                    cycles.Add(new CyclicReference
                    {
                        CyclePath = cyclePath,
                        CycleLength = cyclePath.Count - 1,
                        CycleType = DetermineCycleType(cyclePath),
                        Severity = DetermineCycleSeverity(cyclePath),
                        Description = $"Cycle detected: {string.Join(" -> ", cyclePath)}"
                    });
                }
                else if (!_processedNodes.Contains(neighbor))
                {
                    var nestedCycles = DetectCyclesFromNode(neighbor, adjacencyList, recursionStack, currentPath);
                    cycles.AddRange(nestedCycles);
                }
            }

            recursionStack.Remove(node);
            currentPath.RemoveAt(currentPath.Count - 1);

            return cycles;
        }

        private string DetermineCycleType(List<string> cyclePath)
        {
            if (cyclePath.Count == 3) return "SelfReference"; // A -> A
            if (cyclePath.Count == 4) return "BiDirectional"; // A -> B -> A
            return "Complex"; // A -> B -> C -> A (or longer)
        }

        private string DetermineCycleSeverity(List<string> cyclePath)
        {
            return cyclePath.Count switch
            {
                3 => "Low",      // Self-reference might be intentional
                4 => "Medium",   // Bidirectional relationship might be valid
                _ => "High"      // Complex cycles usually indicate problems
            };
        }

        private Dictionary<string, List<CyclicReference>> CategorizeCycles(List<CyclicReference> cycles)
        {
            return cycles.GroupBy(c => c.CycleType).ToDictionary(g => g.Key, g => g.ToList());
        }

        private Dictionary<string, int> CalculateCycleSeverity(List<CyclicReference> cycles)
        {
            return cycles.GroupBy(c => c.Severity).ToDictionary(g => g.Key, g => g.Count());
        }

        private OrphanNodeReport IdentifyOrphanNodes(OntologyMappingResult ontologyResult, GraphQualityOptions options)
        {
            var report = new OrphanNodeReport();
            var orphanNodes = new List<OrphanNode>();

            // Get all entities that appear in the graph
            var allEntities = ontologyResult.TypedEntities.Select(te => te.Entity.Value).ToHashSet();
            var connectedEntities = ontologyResult.MappedTriples
                .SelectMany(mt => new[] { mt.MappedSubject.NormalizedValue, mt.MappedObject.NormalizedValue })
                .ToHashSet();

            // Find entities that are not connected to any relationships
            var isolatedEntities = allEntities.Except(connectedEntities).ToList();

            foreach (var entityValue in isolatedEntities)
            {
                var entity = ontologyResult.TypedEntities.First(te => te.Entity.Value == entityValue);
                orphanNodes.Add(new OrphanNode
                {
                    EntityValue = entityValue,
                    EntityType = entity.TypeDefinition?.TypeName ?? "Unknown",
                    OrphanType = "Isolated",
                    Description = $"Entity '{entityValue}' has no relationships",
                    ImpactLevel = DetermineOrphanImpact(entity)
                });
            }

            // Find weakly connected components (entities with very few connections)
            var weaklyConnected = FindWeaklyConnectedNodes(ontologyResult, options.MinConnectionThreshold);
            foreach (var weakNode in weaklyConnected)
            {
                orphanNodes.Add(weakNode);
            }

            report.OrphanNodes = orphanNodes;
            report.OrphanCount = orphanNodes.Count;
            report.IsolatedNodeCount = isolatedEntities.Count;
            report.WeaklyConnectedCount = weaklyConnected.Count;

            return report;
        }

        private string DetermineOrphanImpact(TypedEntity entity)
        {
            // Entities with important types should have higher impact
            var importantTypes = new[] { "Person", "Organization", "Key_Concept" };
            
            if (entity.TypeDefinition != null && 
                importantTypes.Contains(entity.TypeDefinition.TypeName, StringComparer.OrdinalIgnoreCase))
            {
                return "High";
            }

            return entity.ValidationResults.Any(vr => !vr.IsValid) ? "Medium" : "Low";
        }

        private List<OrphanNode> FindWeaklyConnectedNodes(OntologyMappingResult ontologyResult, int minConnections)
        {
            var weakNodes = new List<OrphanNode>();
            var connectionCounts = new Dictionary<string, int>();

            // Count connections for each entity
            foreach (var triple in ontologyResult.MappedTriples)
            {
                var subject = triple.MappedSubject.NormalizedValue;
                var obj = triple.MappedObject.NormalizedValue;

                connectionCounts[subject] = connectionCounts.GetValueOrDefault(subject, 0) + 1;
                connectionCounts[obj] = connectionCounts.GetValueOrDefault(obj, 0) + 1;
            }

            // Find entities with fewer connections than threshold
            foreach (var kvp in connectionCounts.Where(kvp => kvp.Value < minConnections))
            {
                var entity = ontologyResult.TypedEntities.FirstOrDefault(te => te.Entity.Value == kvp.Key);
                if (entity != null)
                {
                    weakNodes.Add(new OrphanNode
                    {
                        EntityValue = kvp.Key,
                        EntityType = entity.TypeDefinition?.TypeName ?? "Unknown",
                        OrphanType = "WeaklyConnected",
                        ConnectionCount = kvp.Value,
                        Description = $"Entity '{kvp.Key}' has only {kvp.Value} connection(s)",
                        ImpactLevel = kvp.Value == 1 ? "Medium" : "Low"
                    });
                }
            }

            return weakNodes;
        }

        private StructuralIntegrityReport ValidateStructuralIntegrity(OntologyMappingResult ontologyResult, GraphQualityOptions options)
        {
            var report = new StructuralIntegrityReport();
            var issues = new List<StructuralIssue>();

            // Check for dangling references
            issues.AddRange(DetectDanglingReferences(ontologyResult));

            // Check for type hierarchy violations
            issues.AddRange(ValidateTypeHierarchy(ontologyResult));

            // Check for relationship cardinality violations
            issues.AddRange(ValidateCardinality(ontologyResult));

            // Check for semantic consistency
            issues.AddRange(ValidateSemanticConsistency(ontologyResult));

            report.StructuralIssues = issues;
            report.IntegrityScore = CalculateIntegrityScore(issues, ontologyResult);
            report.IsStructurallySound = issues.Count(i => i.Severity == "Critical") == 0;

            return report;
        }

        private List<StructuralIssue> DetectDanglingReferences(OntologyMappingResult ontologyResult)
        {
            var issues = new List<StructuralIssue>();
            
            // Find references to entities that don't exist
            var entityValues = ontologyResult.TypedEntities.Select(te => te.Entity.Value).ToHashSet();
            var referencedEntities = ontologyResult.MappedTriples
                .SelectMany(mt => new[] { mt.MappedSubject.NormalizedValue, mt.MappedObject.NormalizedValue })
                .ToHashSet();

            var danglingRefs = referencedEntities.Except(entityValues).ToList();
            
            foreach (var danglingRef in danglingRefs)
            {
                issues.Add(new StructuralIssue
                {
                    IssueType = "DanglingReference",
                    Description = $"Reference to undefined entity: '{danglingRef}'",
                    EntityValue = danglingRef,
                    Severity = "Error",
                    SuggestedFix = "Add entity definition or remove references"
                });
            }

            return issues;
        }

        private List<StructuralIssue> ValidateTypeHierarchy(OntologyMappingResult ontologyResult)
        {
            var issues = new List<StructuralIssue>();

            // Check for circular inheritance in type hierarchy
            foreach (var entityType in ontologyResult.InferredSchema.EntityTypes)
            {
                if (HasCircularInheritance(entityType, ontologyResult.InferredSchema.EntityTypes))
                {
                    issues.Add(new StructuralIssue
                    {
                        IssueType = "CircularInheritance",
                        Description = $"Circular inheritance detected in type hierarchy for '{entityType.TypeName}'",
                        EntityValue = entityType.TypeName,
                        Severity = "Critical",
                        SuggestedFix = "Break circular inheritance chain"
                    });
                }
            }

            return issues;
        }

        private bool HasCircularInheritance(EntityTypeDefinition entityType, List<EntityTypeDefinition> allTypes)
        {
            var visited = new HashSet<string>();
            var current = entityType;

            while (current != null && !string.IsNullOrEmpty(current.BaseType))
            {
                if (visited.Contains(current.BaseType))
                    return true;

                visited.Add(current.TypeName);
                current = allTypes.FirstOrDefault(et => et.TypeName == current.BaseType);
            }

            return false;
        }

        private List<StructuralIssue> ValidateCardinality(OntologyMappingResult ontologyResult)
        {
            var issues = new List<StructuralIssue>();

            // Check for cardinality violations based on relationship definitions
            foreach (var relationshipType in ontologyResult.InferredSchema.RelationshipTypes)
            {
                var relationshipTriples = ontologyResult.MappedTriples
                    .Where(mt => mt.MappedPredicate.Equals(relationshipType.RelationshipName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Simple cardinality check - if a relationship should be 1:1 but has multiple objects
                var subjectGroups = relationshipTriples.GroupBy(t => t.MappedSubject.NormalizedValue);
                
                foreach (var group in subjectGroups.Where(g => g.Count() > 1))
                {
                    // Heuristic: assume 1:1 for certain relationship types
                    var oneToOneRelationships = new[] { "married_to", "is_ceo_of", "born_in" };
                    
                    if (oneToOneRelationships.Contains(relationshipType.RelationshipName, StringComparer.OrdinalIgnoreCase))
                    {
                        issues.Add(new StructuralIssue
                        {
                            IssueType = "CardinalityViolation",
                            Description = $"One-to-one relationship '{relationshipType.RelationshipName}' has multiple values for '{group.Key}'",
                            EntityValue = group.Key,
                            Severity = "Warning",
                            SuggestedFix = "Review relationship definition or merge duplicate relationships"
                        });
                    }
                }
            }

            return issues;
        }

        private List<StructuralIssue> ValidateSemanticConsistency(OntologyMappingResult ontologyResult)
        {
            var issues = new List<StructuralIssue>();

            // Check for semantically inconsistent relationships
            foreach (var triple in ontologyResult.MappedTriples)
            {
                if (HasSemanticInconsistency(triple))
                {
                    issues.Add(new StructuralIssue
                    {
                        IssueType = "SemanticInconsistency",
                        Description = $"Semantically inconsistent relationship: {triple.MappedSubject.OntologyType} {triple.MappedPredicate} {triple.MappedObject.OntologyType}",
                        EntityValue = $"{triple.MappedSubject.NormalizedValue} -> {triple.MappedObject.NormalizedValue}",
                        Severity = "Warning",
                        SuggestedFix = "Review relationship semantics and entity types"
                    });
                }
            }

            return issues;
        }

        private bool HasSemanticInconsistency(MappedTriple triple)
        {
            // Simple heuristic checks for semantic consistency
            var subjectType = triple.MappedSubject.OntologyType.ToLower();
            var predicate = triple.MappedPredicate.ToLower();
            var objectType = triple.MappedObject.OntologyType.ToLower();

            // Example inconsistencies
            if (subjectType == "location" && predicate.Contains("born") && objectType != "person")
                return true;

            if (subjectType == "organization" && predicate.Contains("married") && objectType == "organization")
                return true;

            return false;
        }

        private double CalculateIntegrityScore(List<StructuralIssue> issues, OntologyMappingResult ontologyResult)
        {
            if (ontologyResult.MappedTriples.Count == 0) return 1.0;

            var criticalWeight = 1.0;
            var errorWeight = 0.7;
            var warningWeight = 0.3;

            var totalWeight = issues.Sum(i => i.Severity switch
            {
                "Critical" => criticalWeight,
                "Error" => errorWeight,
                "Warning" => warningWeight,
                _ => 0.0
            });

            var maxPossibleIssues = ontologyResult.MappedTriples.Count * criticalWeight;
            return Math.Max(0.0, 1.0 - (totalWeight / maxPossibleIssues));
        }

        private OverallQualityScores CalculateQualityScores(GraphQualityResult result)
        {
            var scores = new OverallQualityScores
            {
                ConsistencyScore = result.ConsistencyReport.ConsistencyScore,
                CompletenessScore = result.CompletenessReport.OverallCompletenessScore,
                StructuralIntegrityScore = result.StructuralIntegrityReport.IntegrityScore
            };

            // Penalize for cycles and orphan nodes
            var cyclePenalty = Math.Min(0.3, result.CyclicReferenceReport.CycleCount * 0.05);
            var orphanPenalty = Math.Min(0.2, result.OrphanNodeReport.OrphanCount * 0.02);

            scores.OverallQualityScore = (
                scores.ConsistencyScore * 0.3 +
                scores.CompletenessScore * 0.25 +
                scores.StructuralIntegrityScore * 0.25 +
                (1.0 - cyclePenalty) * 0.1 +
                (1.0 - orphanPenalty) * 0.1
            );

            scores.QualityGrade = DetermineQualityGrade(scores.OverallQualityScore);

            return scores;
        }

        private string DetermineQualityGrade(double score)
        {
            return score switch
            {
                >= 0.9 => "A",
                >= 0.8 => "B",
                >= 0.7 => "C",
                >= 0.6 => "D",
                _ => "F"
            };
        }

        private List<ImprovementRecommendation> GenerateImprovementRecommendations(GraphQualityResult result, GraphQualityOptions options)
        {
            var recommendations = new List<ImprovementRecommendation>();

            // Consistency improvements
            if (result.ConsistencyReport.ConsistencyScore < 0.8)
            {
                recommendations.Add(new ImprovementRecommendation
                {
                    Priority = "High",
                    Category = "Consistency",
                    Description = "Address type and relationship consistency violations",
                    Actions = result.ConsistencyReport.Violations.Take(5).Select(v => v.SuggestedResolution).ToList(),
                    ExpectedImpact = "Improve consistency score by 0.1-0.3"
                });
            }

            // Completeness improvements
            if (result.CompletenessReport.OverallCompletenessScore < 0.7)
            {
                recommendations.Add(new ImprovementRecommendation
                {
                    Priority = "Medium",
                    Category = "Completeness",
                    Description = "Add missing entity types and properties",
                    Actions = new List<string> { "Define types for untyped entities", "Add required properties", "Expand schema coverage" },
                    ExpectedImpact = "Improve completeness score by 0.2-0.4"
                });
            }

            // Structural improvements
            if (result.CyclicReferenceReport.HasCycles)
            {
                recommendations.Add(new ImprovementRecommendation
                {
                    Priority = "High",
                    Category = "Structure",
                    Description = "Resolve cyclic references in the graph",
                    Actions = result.CyclicReferenceReport.DetectedCycles.Select(c => $"Break cycle: {c.Description}").ToList(),
                    ExpectedImpact = "Eliminate structural inconsistencies"
                });
            }

            // Orphan node handling
            if (result.OrphanNodeReport.OrphanCount > 0)
            {
                recommendations.Add(new ImprovementRecommendation
                {
                    Priority = "Low",
                    Category = "Connectivity",
                    Description = "Connect or remove orphan nodes",
                    Actions = new List<string> { "Add relationships for isolated entities", "Remove irrelevant orphan nodes" },
                    ExpectedImpact = "Improve graph connectivity and reduce noise"
                });
            }

            return recommendations.OrderByDescending(r => r.Priority == "High" ? 3 : r.Priority == "Medium" ? 2 : 1).ToList();
        }

        private AutoFixResults ApplyQualityFixes(OntologyMappingResult ontologyResult, GraphQualityResult qualityResult, GraphQualityOptions options)
        {
            var fixResults = new AutoFixResults();
            var appliedFixes = new List<AppliedFix>();

            // Auto-fix simple consistency violations
            appliedFixes.AddRange(AutoFixTypeConsistency(ontologyResult, qualityResult));

            // Auto-fix orphan nodes (simple cases)
            appliedFixes.AddRange(AutoFixOrphanNodes(ontologyResult, qualityResult));

            // Auto-fix property mappings
            appliedFixes.AddRange(AutoFixPropertyMappings(ontologyResult, qualityResult));

            fixResults.AppliedFixes = appliedFixes;
            fixResults.FixSuccess = appliedFixes.All(f => f.Success);
            fixResults.ImprovementMeasured = CalculateImprovementAfterFixes(ontologyResult, appliedFixes);

            return fixResults;
        }

        private List<AppliedFix> AutoFixTypeConsistency(OntologyMappingResult ontologyResult, GraphQualityResult qualityResult)
        {
            var fixes = new List<AppliedFix>();

            foreach (var violation in qualityResult.ConsistencyReport.Violations.Where(v => v.ViolationType == "TypeInconsistency"))
            {
                // For type conflicts, choose the most frequent type
                var entityValue = violation.EntityValue;
                var conflictingEntities = ontologyResult.TypedEntities.Where(te => 
                    te.Entity.Value.Equals(entityValue, StringComparison.OrdinalIgnoreCase)).ToList();

                if (conflictingEntities.Count > 1)
                {
                    var typeFrequency = conflictingEntities
                        .GroupBy(te => te.TypeDefinition?.TypeName ?? "Unknown")
                        .OrderByDescending(g => g.Count())
                        .First();

                    var resolvedType = typeFrequency.Key;

                    // Update all conflicting entities to use the most frequent type
                    foreach (var entity in conflictingEntities)
                    {
                        if (entity.TypeDefinition?.TypeName != resolvedType)
                        {
                            var resolvedTypeDef = ontologyResult.InferredSchema.EntityTypes
                                .FirstOrDefault(et => et.TypeName == resolvedType);
                            entity.TypeDefinition = resolvedTypeDef ?? new EntityTypeDefinition { TypeName = resolvedType };
                        }
                    }

                    fixes.Add(new AppliedFix
                    {
                        FixType = "TypeConsistency",
                        Description = $"Unified type for '{entityValue}' to '{resolvedType}'",
                        Success = true,
                        EntityAffected = entityValue
                    });
                }
            }

            return fixes;
        }

        private List<AppliedFix> AutoFixOrphanNodes(OntologyMappingResult ontologyResult, GraphQualityResult qualityResult)
        {
            var fixes = new List<AppliedFix>();

            // For now, just mark orphan nodes - actual connection would require domain knowledge
            foreach (var orphan in qualityResult.OrphanNodeReport.OrphanNodes.Where(o => o.ImpactLevel == "Low"))
            {
                fixes.Add(new AppliedFix
                {
                    FixType = "OrphanNode",
                    Description = $"Marked orphan node '{orphan.EntityValue}' for review",
                    Success = true,
                    EntityAffected = orphan.EntityValue
                });
            }

            return fixes;
        }

        private List<AppliedFix> AutoFixPropertyMappings(OntologyMappingResult ontologyResult, GraphQualityResult qualityResult)
        {
            var fixes = new List<AppliedFix>();

            // Fix simple property type inconsistencies by standardizing formats
            foreach (var violation in qualityResult.ConsistencyReport.Violations.Where(v => v.ViolationType == "PropertyTypeInconsistency"))
            {
                var propertyName = violation.EntityValue;
                var relatedMappings = ontologyResult.PropertyMappings.Where(pm => pm.MappedProperty == propertyName).ToList();

                if (relatedMappings.Count > 0)
                {
                    // Standardize to the most common data type
                    var typeFrequency = relatedMappings
                        .GroupBy(pm => InferDataType(pm.TransformationApplied))
                        .OrderByDescending(g => g.Count())
                        .First();

                    var standardType = typeFrequency.Key;

                    foreach (var mapping in relatedMappings)
                    {
                        mapping.TransformationApplied = StandardizeToDataType(mapping.TransformationApplied, standardType);
                    }

                    fixes.Add(new AppliedFix
                    {
                        FixType = "PropertyType",
                        Description = $"Standardized property '{propertyName}' to type '{standardType}'",
                        Success = true,
                        EntityAffected = propertyName
                    });
                }
            }

            return fixes;
        }

        private string InferDataType(string value)
        {
            if (DateTime.TryParse(value, out _)) return "DateTime";
            if (double.TryParse(value, out _)) return "Number";
            if (bool.TryParse(value, out _)) return "Boolean";
            if (Uri.TryCreate(value, UriKind.Absolute, out _)) return "URI";
            return "String";
        }

        private string StandardizeToDataType(string value, string targetType)
        {
            return targetType switch
            {
                "DateTime" => DateTime.TryParse(value, out var date) ? date.ToString("yyyy-MM-dd") : value,
                "Number" => double.TryParse(value, out var num) ? num.ToString("F2") : value,
                "Boolean" => bool.TryParse(value, out var boolean) ? boolean.ToString().ToLower() : value,
                _ => value
            };
        }

        private double CalculateImprovementAfterFixes(OntologyMappingResult ontologyResult, List<AppliedFix> fixes)
        {
            // Simple improvement calculation based on number of successful fixes
            var successfulFixes = fixes.Count(f => f.Success);
            var totalIssues = fixes.Count;

            return totalIssues > 0 ? (double)successfulFixes / totalIssues : 0.0;
        }

        private Dictionary<string, QualityRule> InitializeQualityRules()
        {
            return new Dictionary<string, QualityRule>
            {
                ["TypeConsistency"] = new QualityRule
                {
                    RuleName = "TypeConsistency",
                    Description = "Entities should have consistent types across the graph",
                    Severity = "Error",
                    AutoFixable = true
                },
                ["RelationshipDomain"] = new QualityRule
                {
                    RuleName = "RelationshipDomain",
                    Description = "Relationships should respect domain and range constraints",
                    Severity = "Error",
                    AutoFixable = false
                },
                ["PropertyDataType"] = new QualityRule
                {
                    RuleName = "PropertyDataType",
                    Description = "Properties should have consistent data types",
                    Severity = "Warning",
                    AutoFixable = true
                },
                ["CyclicReference"] = new QualityRule
                {
                    RuleName = "CyclicReference",
                    Description = "Graph should not contain problematic cycles",
                    Severity = "Warning",
                    AutoFixable = false
                },
                ["OrphanNode"] = new QualityRule
                {
                    RuleName = "OrphanNode",
                    Description = "Entities should be connected to the graph",
                    Severity = "Info",
                    AutoFixable = true
                }
            };
        }
    }

    // Supporting classes and data structures
    public class GraphQualityOptions
    {
        public bool AutoFix { get; set; } = false;
        public int MinConnectionThreshold { get; set; } = 2;
        public bool ValidateSemanticConsistency { get; set; } = true;
        public bool DetectCycles { get; set; } = true;
        public bool IdentifyOrphans { get; set; } = true;
    }

    public class GraphQualityResult
    {
        public string ChunkId { get; set; }
        public OntologyMappingResult SourceOntology { get; set; }
        public ConsistencyReport ConsistencyReport { get; set; }
        public CompletenessReport CompletenessReport { get; set; }
        public CyclicReferenceReport CyclicReferenceReport { get; set; }
        public OrphanNodeReport OrphanNodeReport { get; set; }
        public StructuralIntegrityReport StructuralIntegrityReport { get; set; }
        public OverallQualityScores QualityScores { get; set; }
        public List<ImprovementRecommendation> ImprovementRecommendations { get; set; } = new List<ImprovementRecommendation>();
        public AutoFixResults AutoFixResults { get; set; }
    }

    public class ConsistencyReport
    {
        public List<ConsistencyViolation> Violations { get; set; } = new List<ConsistencyViolation>();
        public double ConsistencyScore { get; set; }
        public bool IsConsistent { get; set; }
    }

    public class ConsistencyViolation
    {
        public string ViolationType { get; set; }
        public string EntityValue { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
        public List<string> ConflictingTypes { get; set; } = new List<string>();
        public RdfTriple RelatedTriple { get; set; }
        public string SuggestedResolution { get; set; }
    }

    public class CompletenessReport
    {
        public double EntityCompletenessScore { get; set; }
        public double RelationshipCompletenessScore { get; set; }
        public double PropertyCompletenessScore { get; set; }
        public double SchemaCoverageScore { get; set; }
        public double OverallCompletenessScore { get; set; }
        public List<MissingElement> MissingElements { get; set; } = new List<MissingElement>();
    }

    public class MissingElement
    {
        public string ElementType { get; set; }
        public string ElementValue { get; set; }
        public string Description { get; set; }
        public string ImpactLevel { get; set; }
    }

    public class CyclicReferenceReport
    {
        public List<CyclicReference> DetectedCycles { get; set; } = new List<CyclicReference>();
        public int CycleCount { get; set; }
        public bool HasCycles { get; set; }
        public Dictionary<string, List<CyclicReference>> CyclicReferencesByType { get; set; } = new Dictionary<string, List<CyclicReference>>();
        public Dictionary<string, int> SeverityDistribution { get; set; } = new Dictionary<string, int>();
    }

    public class CyclicReference
    {
        public List<string> CyclePath { get; set; } = new List<string>();
        public int CycleLength { get; set; }
        public string CycleType { get; set; }
        public string Severity { get; set; }
        public string Description { get; set; }
    }

    public class OrphanNodeReport
    {
        public List<OrphanNode> OrphanNodes { get; set; } = new List<OrphanNode>();
        public int OrphanCount { get; set; }
        public int IsolatedNodeCount { get; set; }
        public int WeaklyConnectedCount { get; set; }
    }

    public class OrphanNode
    {
        public string EntityValue { get; set; }
        public string EntityType { get; set; }
        public string OrphanType { get; set; }
        public int ConnectionCount { get; set; }
        public string Description { get; set; }
        public string ImpactLevel { get; set; }
    }

    public class StructuralIntegrityReport
    {
        public List<StructuralIssue> StructuralIssues { get; set; } = new List<StructuralIssue>();
        public double IntegrityScore { get; set; }
        public bool IsStructurallySound { get; set; }
    }

    public class StructuralIssue
    {
        public string IssueType { get; set; }
        public string Description { get; set; }
        public string EntityValue { get; set; }
        public string Severity { get; set; }
        public string SuggestedFix { get; set; }
    }

    public class OverallQualityScores
    {
        public double ConsistencyScore { get; set; }
        public double CompletenessScore { get; set; }
        public double StructuralIntegrityScore { get; set; }
        public double OverallQualityScore { get; set; }
        public string QualityGrade { get; set; }
    }

    public class ImprovementRecommendation
    {
        public string Priority { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public List<string> Actions { get; set; } = new List<string>();
        public string ExpectedImpact { get; set; }
    }

    public class AutoFixResults
    {
        public List<AppliedFix> AppliedFixes { get; set; } = new List<AppliedFix>();
        public bool FixSuccess { get; set; }
        public double ImprovementMeasured { get; set; }
    }

    public class AppliedFix
    {
        public string FixType { get; set; }
        public string Description { get; set; }
        public bool Success { get; set; }
        public string EntityAffected { get; set; }
    }

    public class QualityRule
    {
        public string RuleName { get; set; }
        public string Description { get; set; }
        public string Severity { get; set; }
        public bool AutoFixable { get; set; }
    }
}