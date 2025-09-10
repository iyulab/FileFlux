using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies
{
    public class OntologyMapper
    {
        private readonly Dictionary<string, OntologyTemplate> _domainTemplates;
        private readonly Dictionary<string, TypeDefinition> _typeSystem;
        private readonly List<PropertyMappingRule> _mappingRules;

        public OntologyMapper()
        {
            _domainTemplates = InitializeDomainTemplates();
            _typeSystem = InitializeTypeSystem();
            _mappingRules = InitializePropertyMappingRules();
        }

        public OntologyMappingResult MapToOntology(GraphStructureResult graphResult, OntologyMappingOptions options)
        {
            var result = new OntologyMappingResult
            {
                ChunkId = graphResult.ChunkId,
                SourceGraph = graphResult
            };

            // Select appropriate ontology template based on domain detection
            var detectedDomain = DetectDomain(graphResult, options);
            var template = GetOntologyTemplate(detectedDomain);
            
            result.DomainOntology = template;
            result.InferredSchema = InferSchema(graphResult, template, options);
            result.MappedTriples = MapTriplesToOntology(graphResult.Triples, result.InferredSchema, options);
            result.TypedEntities = ApplyTypeSystem(graphResult, result.InferredSchema);
            result.PropertyMappings = ApplyPropertyMappings(graphResult, result.InferredSchema);

            // Calculate ontology quality metrics
            result.QualityMetrics = CalculateOntologyQuality(result);

            return result;
        }

        private string DetectDomain(GraphStructureResult graphResult, OntologyMappingOptions options)
        {
            var domainScores = new Dictionary<string, double>();

            foreach (var domain in _domainTemplates.Keys)
            {
                var template = _domainTemplates[domain];
                var score = CalculateDomainRelevance(graphResult, template);
                domainScores[domain] = score;
            }

            // Return domain with highest relevance score
            var bestDomain = domainScores.OrderByDescending(kvp => kvp.Value).First();
            return bestDomain.Value > options.MinDomainConfidence ? bestDomain.Key : "general";
        }

        private double CalculateDomainRelevance(GraphStructureResult graphResult, OntologyTemplate template)
        {
            double relevanceScore = 0.0;
            int totalChecks = 0;

            // Check entity type overlap
            foreach (var entityType in template.ExpectedEntityTypes)
            {
                var matchCount = graphResult.Triples.Count(t => 
                    t.Subject.Type.Equals(entityType, StringComparison.OrdinalIgnoreCase) ||
                    t.Object.Type.Equals(entityType, StringComparison.OrdinalIgnoreCase));
                
                relevanceScore += matchCount > 0 ? 1.0 : 0.0;
                totalChecks++;
            }

            // Check relationship pattern overlap
            foreach (var relationPattern in template.CommonRelationships)
            {
                var matchCount = graphResult.Triples.Count(t => 
                    t.Predicate.Equals(relationPattern, StringComparison.OrdinalIgnoreCase));
                
                relevanceScore += matchCount > 0 ? 1.0 : 0.0;
                totalChecks++;
            }

            return totalChecks > 0 ? relevanceScore / totalChecks : 0.0;
        }

        private OntologyTemplate GetOntologyTemplate(string domain)
        {
            return _domainTemplates.TryGetValue(domain, out var template) 
                ? template 
                : _domainTemplates["general"];
        }

        private SchemaDefinition InferSchema(GraphStructureResult graphResult, OntologyTemplate template, OntologyMappingOptions options)
        {
            var schema = new SchemaDefinition
            {
                Domain = template.Domain,
                EntityTypes = new List<EntityTypeDefinition>(),
                RelationshipTypes = new List<RelationshipTypeDefinition>(),
                PropertyDefinitions = new List<PropertyDefinition>()
            };

            // Infer entity types from graph structure
            var observedEntityTypes = graphResult.Triples
                .SelectMany(t => new[] { t.Subject.Type, t.Object.Type })
                .Where(type => !string.IsNullOrEmpty(type))
                .GroupBy(type => type)
                .Select(g => new EntityTypeDefinition
                {
                    TypeName = g.Key,
                    Frequency = g.Count(),
                    Properties = InferEntityProperties(graphResult, g.Key),
                    BaseType = template.GetBaseTypeFor(g.Key)
                })
                .ToList();

            schema.EntityTypes.AddRange(observedEntityTypes);

            // Infer relationship types
            var observedRelationships = graphResult.Triples
                .GroupBy(t => t.Predicate)
                .Select(g => new RelationshipTypeDefinition
                {
                    RelationshipName = g.Key,
                    Frequency = g.Count(),
                    DomainTypes = g.Select(t => t.Subject.Type).Distinct().ToList(),
                    RangeTypes = g.Select(t => t.Object.Type).Distinct().ToList(),
                    IsSymmetric = CheckSymmetry(g.ToList()),
                    IsTransitive = CheckTransitivity(g.ToList())
                })
                .ToList();

            schema.RelationshipTypes.AddRange(observedRelationships);

            return schema;
        }

        private List<PropertyDefinition> InferEntityProperties(GraphStructureResult graphResult, string entityType)
        {
            var properties = new List<PropertyDefinition>();

            // Find all predicates where this entity type appears as subject
            var entityPredicates = graphResult.Triples
                .Where(t => t.Subject.Type.Equals(entityType, StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t.Predicate)
                .ToList();

            foreach (var predicateGroup in entityPredicates)
            {
                var property = new PropertyDefinition
                {
                    PropertyName = predicateGroup.Key,
                    DataType = InferDataType(predicateGroup.Select(t => t.Object.Value)),
                    IsRequired = predicateGroup.Count() > 1, // Heuristic: frequent properties are likely required
                    Cardinality = DetermineCardinality(predicateGroup.ToList())
                };

                properties.Add(property);
            }

            return properties;
        }

        private string InferDataType(IEnumerable<string> values)
        {
            var sampleValues = values.Take(10).ToList();
            
            if (sampleValues.All(v => DateTime.TryParse(v, out _)))
                return "DateTime";
            
            if (sampleValues.All(v => double.TryParse(v, out _)))
                return "Number";
            
            if (sampleValues.All(v => bool.TryParse(v, out _)))
                return "Boolean";
            
            if (sampleValues.All(v => Uri.TryCreate(v, UriKind.Absolute, out _)))
                return "URI";
            
            return "String";
        }

        private string DetermineCardinality(List<RdfTriple> triples)
        {
            var subjectGroups = triples.GroupBy(t => t.Subject.Value);
            var maxObjectsPerSubject = subjectGroups.Max(g => g.Count());
            
            return maxObjectsPerSubject == 1 ? "1:1" : "1:N";
        }

        private bool CheckSymmetry(List<RdfTriple> relationshipTriples)
        {
            // Check if for every (A, relation, B) there exists (B, relation, A)
            var forwardTriples = relationshipTriples.ToHashSet(new TripleComparer());
            var reverseTriples = relationshipTriples.Select(t => new RdfTriple
            {
                Subject = t.Object,
                Predicate = t.Predicate,
                Object = t.Subject
            }).ToHashSet(new TripleComparer());

            return forwardTriples.SetEquals(reverseTriples);
        }

        private bool CheckTransitivity(List<RdfTriple> relationshipTriples)
        {
            // Simple transitivity check: if (A,R,B) and (B,R,C) then (A,R,C) should exist
            var tripleDict = relationshipTriples.ToDictionary(t => $"{t.Subject.Value}_{t.Object.Value}", t => t);
            
            foreach (var triple1 in relationshipTriples)
            {
                foreach (var triple2 in relationshipTriples)
                {
                    if (triple1.Object.Value == triple2.Subject.Value)
                    {
                        var transitiveKey = $"{triple1.Subject.Value}_{triple2.Object.Value}";
                        if (!tripleDict.ContainsKey(transitiveKey))
                            return false;
                    }
                }
            }
            
            return true;
        }

        private List<MappedTriple> MapTriplesToOntology(List<RdfTriple> triples, SchemaDefinition schema, OntologyMappingOptions options)
        {
            var mappedTriples = new List<MappedTriple>();

            foreach (var triple in triples)
            {
                var mappedTriple = new MappedTriple
                {
                    OriginalTriple = triple,
                    MappedSubject = MapEntityToOntology(triple.Subject, schema),
                    MappedPredicate = MapPredicateToOntology(triple.Predicate, schema),
                    MappedObject = MapEntityToOntology(triple.Object, schema),
                    ConfidenceScore = CalculateMappingConfidence(triple, schema)
                };

                mappedTriples.Add(mappedTriple);
            }

            return mappedTriples;
        }

        private OntologyEntity MapEntityToOntology(RdfEntity entity, SchemaDefinition schema)
        {
            var entityType = schema.EntityTypes.FirstOrDefault(et => 
                et.TypeName.Equals(entity.Type, StringComparison.OrdinalIgnoreCase));

            return new OntologyEntity
            {
                OriginalEntity = entity,
                OntologyType = entityType?.TypeName ?? "Unknown",
                NormalizedValue = NormalizeEntityValue(entity.Value, entityType),
                TypeHierarchy = GetTypeHierarchy(entityType)
            };
        }

        private string MapPredicateToOntology(string predicate, SchemaDefinition schema)
        {
            var relationshipType = schema.RelationshipTypes.FirstOrDefault(rt =>
                rt.RelationshipName.Equals(predicate, StringComparison.OrdinalIgnoreCase));

            return relationshipType?.RelationshipName ?? predicate;
        }

        private string NormalizeEntityValue(string value, EntityTypeDefinition entityType)
        {
            if (entityType == null) return value;

            // Apply normalization rules based on entity type
            switch (entityType.TypeName.ToLower())
            {
                case "person":
                    return NormalizePersonName(value);
                case "organization":
                    return NormalizeOrganizationName(value);
                case "location":
                    return NormalizeLocationName(value);
                case "date":
                    return NormalizeDateValue(value);
                default:
                    return value;
            }
        }

        private string NormalizePersonName(string name)
        {
            // Remove titles, standardize format
            var cleanName = name.Trim();
            var titles = new[] { "Mr.", "Mrs.", "Ms.", "Dr.", "Prof." };
            
            foreach (var title in titles)
            {
                if (cleanName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(title.Length).Trim();
                }
            }
            
            return cleanName;
        }

        private string NormalizeOrganizationName(string orgName)
        {
            // Remove common suffixes, standardize format
            var suffixes = new[] { "Inc.", "Corp.", "Ltd.", "LLC", "Co." };
            var cleanName = orgName.Trim();
            
            foreach (var suffix in suffixes)
            {
                if (cleanName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    cleanName = cleanName.Substring(0, cleanName.Length - suffix.Length).Trim();
                }
            }
            
            return cleanName;
        }

        private string NormalizeLocationName(string location)
        {
            // Standardize location format
            return location.Trim();
        }

        private string NormalizeDateValue(string dateValue)
        {
            if (DateTime.TryParse(dateValue, out var date))
            {
                return date.ToString("yyyy-MM-dd");
            }
            return dateValue;
        }

        private List<string> GetTypeHierarchy(EntityTypeDefinition entityType)
        {
            var hierarchy = new List<string>();
            
            if (entityType != null)
            {
                hierarchy.Add(entityType.TypeName);
                
                var currentType = new TypeDefinition { TypeName = entityType.TypeName, BaseType = entityType.BaseType };
                while (!string.IsNullOrEmpty(currentType.BaseType))
                {
                    hierarchy.Add(currentType.BaseType);
                    currentType = _typeSystem.Values.FirstOrDefault(t => t.TypeName == currentType.BaseType);
                    if (currentType == null) break;
                }
            }
            
            return hierarchy;
        }

        private double CalculateMappingConfidence(RdfTriple triple, SchemaDefinition schema)
        {
            double confidence = 0.0;
            int factors = 0;

            // Subject type confidence
            if (schema.EntityTypes.Any(et => et.TypeName.Equals(triple.Subject.Type, StringComparison.OrdinalIgnoreCase)))
            {
                confidence += 1.0;
            }
            factors++;

            // Object type confidence
            if (schema.EntityTypes.Any(et => et.TypeName.Equals(triple.Object.Type, StringComparison.OrdinalIgnoreCase)))
            {
                confidence += 1.0;
            }
            factors++;

            // Predicate confidence
            if (schema.RelationshipTypes.Any(rt => rt.RelationshipName.Equals(triple.Predicate, StringComparison.OrdinalIgnoreCase)))
            {
                confidence += 1.0;
            }
            factors++;

            return factors > 0 ? confidence / factors : 0.0;
        }

        private List<TypedEntity> ApplyTypeSystem(GraphStructureResult graphResult, SchemaDefinition schema)
        {
            var typedEntities = new List<TypedEntity>();

            var allEntities = graphResult.Triples
                .SelectMany(t => new[] { t.Subject, t.Object })
                .GroupBy(e => e.Value)
                .Select(g => g.First())
                .ToList();

            foreach (var entity in allEntities)
            {
                var entityType = schema.EntityTypes.FirstOrDefault(et =>
                    et.TypeName.Equals(entity.Type, StringComparison.OrdinalIgnoreCase));

                var typedEntity = new TypedEntity
                {
                    Entity = entity,
                    TypeDefinition = entityType ?? new EntityTypeDefinition { TypeName = entity.Type },
                    TypeConstraints = GetTypeConstraints(entityType),
                    ValidationResults = ValidateEntityAgainstType(entity, entityType)
                };

                typedEntities.Add(typedEntity);
            }

            return typedEntities;
        }

        private List<TypeConstraint> GetTypeConstraints(EntityTypeDefinition entityType)
        {
            var constraints = new List<TypeConstraint>();

            if (entityType != null)
            {
                foreach (var property in entityType.Properties)
                {
                    constraints.Add(new TypeConstraint
                    {
                        PropertyName = property.PropertyName,
                        DataType = property.DataType,
                        IsRequired = property.IsRequired,
                        ConstraintType = "DataType"
                    });
                }
            }

            return constraints;
        }

        private List<ValidationResult> ValidateEntityAgainstType(RdfEntity entity, EntityTypeDefinition entityType)
        {
            var results = new List<ValidationResult>();

            if (entityType == null)
            {
                results.Add(new ValidationResult
                {
                    IsValid = false,
                    Message = "No type definition found",
                    Severity = "Warning"
                });
                return results;
            }

            // Validate entity value format based on type
            var isValidFormat = ValidateEntityFormat(entity, entityType);
            results.Add(new ValidationResult
            {
                IsValid = isValidFormat,
                Message = isValidFormat ? "Valid format" : "Invalid format for type",
                Severity = isValidFormat ? "Info" : "Error"
            });

            return results;
        }

        private bool ValidateEntityFormat(RdfEntity entity, EntityTypeDefinition entityType)
        {
            switch (entityType.TypeName.ToLower())
            {
                case "person":
                    return !string.IsNullOrWhiteSpace(entity.Value) && entity.Value.Length > 1;
                case "organization":
                    return !string.IsNullOrWhiteSpace(entity.Value) && entity.Value.Length > 1;
                case "location":
                    return !string.IsNullOrWhiteSpace(entity.Value);
                case "date":
                    return DateTime.TryParse(entity.Value, out _);
                case "number":
                    return double.TryParse(entity.Value, out _);
                default:
                    return !string.IsNullOrEmpty(entity.Value);
            }
        }

        private List<PropertyMapping> ApplyPropertyMappings(GraphStructureResult graphResult, SchemaDefinition schema)
        {
            var propertyMappings = new List<PropertyMapping>();

            foreach (var rule in _mappingRules)
            {
                var applicableTriples = graphResult.Triples.Where(t =>
                    rule.AppliesTo(t.Subject.Type, t.Predicate)).ToList();

                foreach (var triple in applicableTriples)
                {
                    var mapping = new PropertyMapping
                    {
                        OriginalPredicate = triple.Predicate,
                        MappedProperty = rule.TargetProperty,
                        MappingRule = rule,
                        SourceTriple = triple,
                        TransformationApplied = rule.TransformationFunction?.Invoke(triple.Object.Value) ?? triple.Object.Value
                    };

                    propertyMappings.Add(mapping);
                }
            }

            return propertyMappings;
        }

        private OntologyQualityMetrics CalculateOntologyQuality(OntologyMappingResult result)
        {
            var metrics = new OntologyQualityMetrics();

            // Calculate completeness
            var mappedTriples = result.MappedTriples.Count(mt => mt.ConfidenceScore > 0.5);
            metrics.Completeness = result.MappedTriples.Count > 0 
                ? (double)mappedTriples / result.MappedTriples.Count 
                : 0.0;

            // Calculate consistency
            var consistentMappings = result.MappedTriples.Count(mt => 
                ValidateMappingConsistency(mt, result.InferredSchema));
            metrics.Consistency = result.MappedTriples.Count > 0
                ? (double)consistentMappings / result.MappedTriples.Count
                : 0.0;

            // Calculate coverage
            var uniqueEntityTypes = result.TypedEntities.Select(te => te.TypeDefinition?.TypeName).Distinct().Count();
            var schemaEntityTypes = result.InferredSchema.EntityTypes.Count;
            metrics.Coverage = schemaEntityTypes > 0 
                ? (double)uniqueEntityTypes / schemaEntityTypes 
                : 0.0;

            // Calculate average confidence
            metrics.AverageConfidence = result.MappedTriples.Count > 0
                ? result.MappedTriples.Average(mt => mt.ConfidenceScore)
                : 0.0;

            return metrics;
        }

        private bool ValidateMappingConsistency(MappedTriple mappedTriple, SchemaDefinition schema)
        {
            // Check if the mapped relationship is consistent with schema constraints
            var relationshipType = schema.RelationshipTypes.FirstOrDefault(rt =>
                rt.RelationshipName.Equals(mappedTriple.MappedPredicate, StringComparison.OrdinalIgnoreCase));

            if (relationshipType == null) return false;

            // Check domain constraints
            var subjectTypeValid = relationshipType.DomainTypes.Contains(mappedTriple.MappedSubject.OntologyType);
            var objectTypeValid = relationshipType.RangeTypes.Contains(mappedTriple.MappedObject.OntologyType);

            return subjectTypeValid && objectTypeValid;
        }

        private Dictionary<string, OntologyTemplate> InitializeDomainTemplates()
        {
            return new Dictionary<string, OntologyTemplate>
            {
                ["technical"] = new OntologyTemplate
                {
                    Domain = "technical",
                    ExpectedEntityTypes = new[] { "Component", "Function", "Class", "Method", "Variable", "Parameter" },
                    CommonRelationships = new[] { "implements", "extends", "calls", "uses", "contains", "depends_on" }
                },
                ["legal"] = new OntologyTemplate
                {
                    Domain = "legal",
                    ExpectedEntityTypes = new[] { "Person", "Organization", "Contract", "Law", "Court", "Case" },
                    CommonRelationships = new[] { "party_to", "governed_by", "violates", "enforces", "references" }
                },
                ["medical"] = new OntologyTemplate
                {
                    Domain = "medical",
                    ExpectedEntityTypes = new[] { "Patient", "Doctor", "Diagnosis", "Treatment", "Medication", "Symptom" },
                    CommonRelationships = new[] { "diagnoses", "treats", "prescribes", "exhibits", "causes", "prevents" }
                },
                ["business"] = new OntologyTemplate
                {
                    Domain = "business",
                    ExpectedEntityTypes = new[] { "Company", "Product", "Market", "Customer", "Revenue", "Strategy" },
                    CommonRelationships = new[] { "competes_with", "serves", "produces", "targets", "partners_with" }
                },
                ["general"] = new OntologyTemplate
                {
                    Domain = "general",
                    ExpectedEntityTypes = new[] { "Person", "Organization", "Location", "Date", "Concept", "Event" },
                    CommonRelationships = new[] { "related_to", "part_of", "located_in", "occurs_at", "involves" }
                }
            };
        }

        private Dictionary<string, TypeDefinition> InitializeTypeSystem()
        {
            return new Dictionary<string, TypeDefinition>
            {
                ["Entity"] = new TypeDefinition { TypeName = "Entity", BaseType = null },
                ["Person"] = new TypeDefinition { TypeName = "Person", BaseType = "Entity" },
                ["Organization"] = new TypeDefinition { TypeName = "Organization", BaseType = "Entity" },
                ["Location"] = new TypeDefinition { TypeName = "Location", BaseType = "Entity" },
                ["Concept"] = new TypeDefinition { TypeName = "Concept", BaseType = "Entity" },
                ["Event"] = new TypeDefinition { TypeName = "Event", BaseType = "Entity" },
                ["Date"] = new TypeDefinition { TypeName = "Date", BaseType = "Entity" }
            };
        }

        private List<PropertyMappingRule> InitializePropertyMappingRules()
        {
            return new List<PropertyMappingRule>
            {
                new PropertyMappingRule
                {
                    SourceEntityType = "Person",
                    SourcePredicate = "has_name",
                    TargetProperty = "fullName",
                    TransformationFunction = value => NormalizePersonName(value)
                },
                new PropertyMappingRule
                {
                    SourceEntityType = "Organization",
                    SourcePredicate = "has_name",
                    TargetProperty = "organizationName",
                    TransformationFunction = value => NormalizeOrganizationName(value)
                },
                new PropertyMappingRule
                {
                    SourceEntityType = "Location",
                    SourcePredicate = "has_name",
                    TargetProperty = "locationName",
                    TransformationFunction = value => NormalizeLocationName(value)
                }
            };
        }
    }

    // Supporting classes and data structures
    public class OntologyMappingOptions
    {
        public double MinDomainConfidence { get; set; } = 0.7;
        public bool EnableSchemaInference { get; set; } = true;
        public bool ApplyPropertyMappings { get; set; } = true;
        public bool ValidateConsistency { get; set; } = true;
    }

    public class OntologyMappingResult
    {
        public string ChunkId { get; set; }
        public GraphStructureResult SourceGraph { get; set; }
        public OntologyTemplate DomainOntology { get; set; }
        public SchemaDefinition InferredSchema { get; set; }
        public List<MappedTriple> MappedTriples { get; set; } = new List<MappedTriple>();
        public List<TypedEntity> TypedEntities { get; set; } = new List<TypedEntity>();
        public List<PropertyMapping> PropertyMappings { get; set; } = new List<PropertyMapping>();
        public OntologyQualityMetrics QualityMetrics { get; set; }
    }

    public class OntologyTemplate
    {
        public string Domain { get; set; }
        public string[] ExpectedEntityTypes { get; set; }
        public string[] CommonRelationships { get; set; }

        public string GetBaseTypeFor(string entityType)
        {
            // Simple mapping logic - can be enhanced
            return entityType.ToLower() switch
            {
                var t when t.Contains("person") || t.Contains("people") => "Person",
                var t when t.Contains("organization") || t.Contains("company") => "Organization",
                var t when t.Contains("location") || t.Contains("place") => "Location",
                var t when t.Contains("date") || t.Contains("time") => "Date",
                _ => "Entity"
            };
        }
    }

    public class SchemaDefinition
    {
        public string Domain { get; set; }
        public List<EntityTypeDefinition> EntityTypes { get; set; } = new List<EntityTypeDefinition>();
        public List<RelationshipTypeDefinition> RelationshipTypes { get; set; } = new List<RelationshipTypeDefinition>();
        public List<PropertyDefinition> PropertyDefinitions { get; set; } = new List<PropertyDefinition>();
    }

    public class EntityTypeDefinition
    {
        public string TypeName { get; set; }
        public string BaseType { get; set; }
        public int Frequency { get; set; }
        public List<PropertyDefinition> Properties { get; set; } = new List<PropertyDefinition>();
    }

    public class RelationshipTypeDefinition
    {
        public string RelationshipName { get; set; }
        public int Frequency { get; set; }
        public List<string> DomainTypes { get; set; } = new List<string>();
        public List<string> RangeTypes { get; set; } = new List<string>();
        public bool IsSymmetric { get; set; }
        public bool IsTransitive { get; set; }
    }

    public class PropertyDefinition
    {
        public string PropertyName { get; set; }
        public string DataType { get; set; }
        public bool IsRequired { get; set; }
        public string Cardinality { get; set; }
    }

    public class TypeDefinition
    {
        public string TypeName { get; set; }
        public string BaseType { get; set; }
    }

    public class MappedTriple
    {
        public RdfTriple OriginalTriple { get; set; }
        public OntologyEntity MappedSubject { get; set; }
        public string MappedPredicate { get; set; }
        public OntologyEntity MappedObject { get; set; }
        public double ConfidenceScore { get; set; }
    }

    public class OntologyEntity
    {
        public RdfEntity OriginalEntity { get; set; }
        public string OntologyType { get; set; }
        public string NormalizedValue { get; set; }
        public List<string> TypeHierarchy { get; set; } = new List<string>();
    }

    public class TypedEntity
    {
        public RdfEntity Entity { get; set; }
        public EntityTypeDefinition TypeDefinition { get; set; }
        public List<TypeConstraint> TypeConstraints { get; set; } = new List<TypeConstraint>();
        public List<ValidationResult> ValidationResults { get; set; } = new List<ValidationResult>();
    }

    public class TypeConstraint
    {
        public string PropertyName { get; set; }
        public string DataType { get; set; }
        public bool IsRequired { get; set; }
        public string ConstraintType { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public string Severity { get; set; }
    }

    public class PropertyMapping
    {
        public string OriginalPredicate { get; set; }
        public string MappedProperty { get; set; }
        public PropertyMappingRule MappingRule { get; set; }
        public RdfTriple SourceTriple { get; set; }
        public string TransformationApplied { get; set; }
    }

    public class PropertyMappingRule
    {
        public string SourceEntityType { get; set; }
        public string SourcePredicate { get; set; }
        public string TargetProperty { get; set; }
        public Func<string, string> TransformationFunction { get; set; }

        public bool AppliesTo(string entityType, string predicate)
        {
            return SourceEntityType.Equals(entityType, StringComparison.OrdinalIgnoreCase) &&
                   SourcePredicate.Equals(predicate, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class OntologyQualityMetrics
    {
        public double Completeness { get; set; }
        public double Consistency { get; set; }
        public double Coverage { get; set; }
        public double AverageConfidence { get; set; }
    }

    public class TripleComparer : IEqualityComparer<RdfTriple>
    {
        public bool Equals(RdfTriple x, RdfTriple y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            
            return x.Subject.Value == y.Subject.Value &&
                   x.Predicate == y.Predicate &&
                   x.Object.Value == y.Object.Value;
        }

        public int GetHashCode(RdfTriple obj)
        {
            return HashCode.Combine(obj.Subject.Value, obj.Predicate, obj.Object.Value);
        }
    }
}