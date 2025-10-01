using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 12 T12-001: Entity Extraction System
/// Extracts entities and relationships for knowledge graph construction
/// </summary>
public class EntityExtractionSystem
{
    private static readonly Regex PersonNameRegex = new(@"\b(?:Dr|Mr|Mrs|Ms|Prof|Sir|Lady)\.?\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b", RegexOptions.Compiled);
    private static readonly Regex OrganizationRegex = new(@"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\s+(?:Inc|Corp|Ltd|LLC|Company|Corporation|Organization|Institute|University|College)\b", RegexOptions.Compiled);
    private static readonly Regex LocationRegex = new(@"\b(?:in|at|from|to)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)\b", RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}\b|\b\d{1,2}/\d{1,2}/\d{4}\b|\b\d{4}-\d{2}-\d{2}\b", RegexOptions.Compiled);
    private static readonly Regex TechnicalTermRegex = new(@"\b[A-Z]{2,}\b|\b[A-Z][a-z]+(?:[A-Z][a-z]*)+\b", RegexOptions.Compiled);
    
    /// <summary>
    /// Extract entities and relationships from document chunk
    /// </summary>
    public EntityExtractionResult ExtractEntitiesAndRelationships(DocumentChunk chunk, EntityExtractionOptions options)
    {
        var result = new EntityExtractionResult
        {
            ChunkId = chunk.Id.ToString(),
            ExtractionTimestamp = DateTime.UtcNow
        };

        // 1. Named Entity Recognition (NER)
        result.NamedEntities = PerformNamedEntityRecognition(chunk.Content, options);

        // 2. Relationship Extraction
        result.ExtractedRelationships = ExtractRelationships(chunk.Content, result.NamedEntities, options);

        // 3. Coreference Resolution
        result.CoreferenceChains = ResolveCoreferences(chunk.Content, result.NamedEntities);

        // 4. Entity Normalization and Linking
        result.NormalizedEntities = NormalizeAndLinkEntities(result.NamedEntities, options);

        // 5. Confidence scoring
        result.ExtractionConfidence = CalculateExtractionConfidence(result);

        return result;
    }

    /// <summary>
    /// Named Entity Recognition using pattern-based approach
    /// </summary>
    private List<NamedEntity> PerformNamedEntityRecognition(string content, EntityExtractionOptions options)
    {
        var entities = new List<NamedEntity>();

        // Extract person names
        var personMatches = PersonNameRegex.Matches(content);
        foreach (Match match in personMatches)
        {
            entities.Add(new NamedEntity
            {
                Text = match.Value.Trim(),
                Type = EntityType.Person,
                StartPosition = match.Index,
                EndPosition = match.Index + match.Length,
                Confidence = CalculatePatternConfidence(match.Value, EntityType.Person)
            });
        }

        // Extract organizations
        var orgMatches = OrganizationRegex.Matches(content);
        foreach (Match match in orgMatches)
        {
            entities.Add(new NamedEntity
            {
                Text = match.Value.Trim(),
                Type = EntityType.Organization,
                StartPosition = match.Index,
                EndPosition = match.Index + match.Length,
                Confidence = CalculatePatternConfidence(match.Value, EntityType.Organization)
            });
        }

        // Extract locations
        var locationMatches = LocationRegex.Matches(content);
        foreach (Match match in locationMatches)
        {
            var locationText = match.Groups[1].Value.Trim();
            if (IsValidLocation(locationText))
            {
                entities.Add(new NamedEntity
                {
                    Text = locationText,
                    Type = EntityType.Location,
                    StartPosition = match.Groups[1].Index,
                    EndPosition = match.Groups[1].Index + match.Groups[1].Length,
                    Confidence = CalculatePatternConfidence(locationText, EntityType.Location)
                });
            }
        }

        // Extract dates
        var dateMatches = DateRegex.Matches(content);
        foreach (Match match in dateMatches)
        {
            entities.Add(new NamedEntity
            {
                Text = match.Value.Trim(),
                Type = EntityType.Date,
                StartPosition = match.Index,
                EndPosition = match.Index + match.Length,
                Confidence = CalculatePatternConfidence(match.Value, EntityType.Date)
            });
        }

        // Extract technical terms and concepts
        var techMatches = TechnicalTermRegex.Matches(content);
        foreach (Match match in techMatches)
        {
            if (IsTechnicalConcept(match.Value))
            {
                entities.Add(new NamedEntity
                {
                    Text = match.Value.Trim(),
                    Type = EntityType.Concept,
                    StartPosition = match.Index,
                    EndPosition = match.Index + match.Length,
                    Confidence = CalculatePatternConfidence(match.Value, EntityType.Concept)
                });
            }
        }

        // Extract custom domain entities
        if (options.EnableDomainSpecificExtraction)
        {
            entities.AddRange(ExtractDomainSpecificEntities(content, options));
        }

        // Remove duplicates and low-confidence entities
        return DeduplicateAndFilterEntities(entities, options);
    }

    /// <summary>
    /// Extract relationships between entities
    /// </summary>
    private List<ExtractedRelationship> ExtractRelationships(
        string content, 
        List<NamedEntity> entities, 
        EntityExtractionOptions options)
    {
        var relationships = new List<ExtractedRelationship>();

        // Pattern-based relationship extraction
        relationships.AddRange(ExtractPatternBasedRelationships(content, entities));

        // Distance-based relationships
        relationships.AddRange(ExtractProximityRelationships(entities, content));

        // Syntactic relationships (simplified)
        relationships.AddRange(ExtractSyntacticRelationships(content, entities));

        // Domain-specific relationships
        if (options.EnableDomainSpecificExtraction)
        {
            relationships.AddRange(ExtractDomainSpecificRelationships(content, entities, options));
        }

        return FilterAndScoreRelationships(relationships, options);
    }

    /// <summary>
    /// Resolve coreferences (pronouns and references)
    /// </summary>
    private List<CoreferenceChain> ResolveCoreferences(string content, List<NamedEntity> entities)
    {
        var chains = new List<CoreferenceChain>();
        var sentences = SplitIntoSentences(content);

        foreach (var entity in entities.Where(e => e.Type == EntityType.Person || e.Type == EntityType.Organization))
        {
            var chain = new CoreferenceChain
            {
                MainEntity = entity,
                References = new List<CoreferenceReference>()
            };

            // Find pronouns and references in subsequent sentences
            for (int i = 0; i < sentences.Count; i++)
            {
                var sentence = sentences[i];
                
                // Check if entity appears in this sentence
                if (sentence.Contains(entity.Text, StringComparison.OrdinalIgnoreCase))
                {
                    // Look for pronouns in the next few sentences
                    for (int j = i + 1; j < Math.Min(i + 3, sentences.Count); j++)
                    {
                        var nextSentence = sentences[j];
                        var pronouns = ExtractPronouns(nextSentence, entity.Type);
                        
                        foreach (var pronoun in pronouns)
                        {
                            chain.References.Add(new CoreferenceReference
                            {
                                Text = pronoun.Text,
                                Position = pronoun.Position,
                                SentenceIndex = j,
                                Confidence = CalculatePronounConfidence(entity, pronoun, sentences[i], nextSentence)
                            });
                        }
                    }
                }
            }

            if (chain.References.Any())
            {
                chains.Add(chain);
            }
        }

        return chains;
    }

    /// <summary>
    /// Normalize and link entities to knowledge bases
    /// </summary>
    private List<NormalizedEntity> NormalizeAndLinkEntities(
        List<NamedEntity> entities, 
        EntityExtractionOptions options)
    {
        var normalized = new List<NormalizedEntity>();

        foreach (var entity in entities)
        {
            var normalizedEntity = new NormalizedEntity
            {
                OriginalEntity = entity,
                NormalizedForm = NormalizeEntityText(entity.Text, entity.Type),
                EntityId = GenerateEntityId(entity),
                Aliases = GenerateAliases(entity),
                LinkedKnowledgeBase = options.EnableKnowledgeBaseLinking ? 
                    LinkToKnowledgeBase(entity) : null
            };

            // Add semantic information
            normalizedEntity.SemanticType = DetermineSemanticType(entity);
            normalizedEntity.Properties = ExtractEntityProperties(entity);

            normalized.Add(normalizedEntity);
        }

        return normalized;
    }

    /// <summary>
    /// Calculate overall extraction confidence
    /// </summary>
    private double CalculateExtractionConfidence(EntityExtractionResult result)
    {
        if (!result.NamedEntities.Any())
            return 0.0;

        var entityConfidence = result.NamedEntities.Average(e => e.Confidence);
        var relationshipConfidence = result.ExtractedRelationships.Any() ? 
            result.ExtractedRelationships.Average(r => r.Confidence) : 0.5;
        var coreferenceConfidence = result.CoreferenceChains.Any() ?
            result.CoreferenceChains.SelectMany(c => c.References).Average(r => r.Confidence) : 0.5;

        return (entityConfidence * 0.5) + (relationshipConfidence * 0.3) + (coreferenceConfidence * 0.2);
    }

    // Helper methods for entity extraction

    private double CalculatePatternConfidence(string text, EntityType type)
    {
        var baseConfidence = 0.7;

        // Adjust based on entity type and characteristics
        switch (type)
        {
            case EntityType.Person:
                if (text.Contains("Dr") || text.Contains("Prof"))
                    baseConfidence += 0.2;
                break;
            case EntityType.Organization:
                if (text.Contains("Inc") || text.Contains("Corp"))
                    baseConfidence += 0.2;
                break;
            case EntityType.Date:
                if (Regex.IsMatch(text, @"\d{4}-\d{2}-\d{2}"))
                    baseConfidence += 0.2;
                break;
        }

        // Adjust based on length and capitalization
        if (text.Length > 10)
            baseConfidence -= 0.1;
        if (char.IsUpper(text[0]))
            baseConfidence += 0.1;

        return Math.Min(1.0, Math.Max(0.1, baseConfidence));
    }

    private bool IsValidLocation(string text)
    {
        var invalidWords = new[] { "the", "and", "or", "but", "in", "on", "at", "to", "for" };
        return !invalidWords.Contains(text.ToLowerInvariant()) && text.Length > 2;
    }

    private bool IsTechnicalConcept(string text)
    {
        // Check if it's a technical term
        var technicalIndicators = new[]
        {
            text.Length >= 3 && text.All(char.IsUpper), // Acronyms
            text.Contains("API") || text.Contains("SDK") || text.Contains("HTTP"),
            Regex.IsMatch(text, @"^[A-Z][a-z]+(?:[A-Z][a-z]*)+$") // CamelCase
        };

        return technicalIndicators.Any(indicator => indicator);
    }

    private List<NamedEntity> ExtractDomainSpecificEntities(string content, EntityExtractionOptions options)
    {
        var entities = new List<NamedEntity>();

        // Technical domain entities
        if (options.DomainType == "Technical")
        {
            var techPatterns = new[]
            {
                (@"\b\w+\(\)", EntityType.Method),
                (@"\bclass\s+(\w+)", EntityType.Class),
                (@"\binterface\s+(\w+)", EntityType.Interface),
                (@"\b(\w+)\s*:\s*\w+", EntityType.Property)
            };

            foreach (var (pattern, entityType) in techPatterns)
            {
                var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    var text = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                    entities.Add(new NamedEntity
                    {
                        Text = text.Trim(),
                        Type = entityType,
                        StartPosition = match.Index,
                        EndPosition = match.Index + match.Length,
                        Confidence = 0.8
                    });
                }
            }
        }

        // Business domain entities
        if (options.DomainType == "Business")
        {
            var businessPatterns = new[]
            {
                (@"\$[\d,]+\.?\d*", EntityType.Money),
                (@"\b\d+%", EntityType.Percentage),
                (@"\bQ[1-4]\s+\d{4}", EntityType.Quarter)
            };

            foreach (var (pattern, entityType) in businessPatterns)
            {
                var matches = Regex.Matches(content, pattern);
                foreach (Match match in matches)
                {
                    entities.Add(new NamedEntity
                    {
                        Text = match.Value.Trim(),
                        Type = entityType,
                        StartPosition = match.Index,
                        EndPosition = match.Index + match.Length,
                        Confidence = 0.9
                    });
                }
            }
        }

        return entities;
    }

    private List<NamedEntity> DeduplicateAndFilterEntities(List<NamedEntity> entities, EntityExtractionOptions options)
    {
        // Remove duplicates based on text and type
        var deduplicated = entities
            .GroupBy(e => new { Text = e.Text.ToLowerInvariant(), e.Type })
            .Select(g => g.OrderByDescending(e => e.Confidence).First())
            .ToList();

        // Filter by confidence threshold
        return deduplicated
            .Where(e => e.Confidence >= options.MinEntityConfidence)
            .OrderByDescending(e => e.Confidence)
            .Take(options.MaxEntitiesPerChunk)
            .ToList();
    }

    private List<ExtractedRelationship> ExtractPatternBasedRelationships(string content, List<NamedEntity> entities)
    {
        var relationships = new List<ExtractedRelationship>();

        var relationshipPatterns = new[]
        {
            (@"(\w+)\s+(?:works for|employed by|member of)\s+(\w+)", RelationType.EmployedBy),
            (@"(\w+)\s+(?:founded|established|created)\s+(\w+)", RelationType.Founded),
            (@"(\w+)\s+(?:located in|based in|situated in)\s+(\w+)", RelationType.LocatedIn),
            (@"(\w+)\s+(?:owns|possesses|has)\s+(\w+)", RelationType.Owns),
            (@"(\w+)\s+(?:married to|spouse of)\s+(\w+)", RelationType.MarriedTo)
        };

        foreach (var (pattern, relationType) in relationshipPatterns)
        {
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var subject = match.Groups[1].Value.Trim();
                var objectEntity = match.Groups[2].Value.Trim();

                var subjectEntity = entities.FirstOrDefault(e => 
                    e.Text.Contains(subject, StringComparison.OrdinalIgnoreCase));
                var targetEntity = entities.FirstOrDefault(e => 
                    e.Text.Contains(objectEntity, StringComparison.OrdinalIgnoreCase));

                if (subjectEntity != null && targetEntity != null)
                {
                    relationships.Add(new ExtractedRelationship
                    {
                        Subject = subjectEntity,
                        Predicate = relationType.ToString(),
                        Object = targetEntity,
                        RelationType = relationType,
                        Confidence = 0.8,
                        TextualEvidence = match.Value,
                        Position = match.Index
                    });
                }
            }
        }

        return relationships;
    }

    private List<ExtractedRelationship> ExtractProximityRelationships(List<NamedEntity> entities, string content)
    {
        var relationships = new List<ExtractedRelationship>();
        const int proximityThreshold = 50; // characters

        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                var entity1 = entities[i];
                var entity2 = entities[j];

                var distance = Math.Abs(entity1.StartPosition - entity2.StartPosition);
                if (distance <= proximityThreshold)
                {
                    var relationType = InferRelationshipType(entity1, entity2);
                    var confidence = CalculateProximityConfidence(distance, proximityThreshold);

                    relationships.Add(new ExtractedRelationship
                    {
                        Subject = entity1,
                        Predicate = "near",
                        Object = entity2,
                        RelationType = relationType,
                        Confidence = confidence,
                        TextualEvidence = ExtractContextText(content, entity1, entity2),
                        Position = Math.Min(entity1.StartPosition, entity2.StartPosition)
                    });
                }
            }
        }

        return relationships;
    }

    private List<ExtractedRelationship> ExtractSyntacticRelationships(string content, List<NamedEntity> entities)
    {
        var relationships = new List<ExtractedRelationship>();
        var sentences = SplitIntoSentences(content);

        foreach (var sentence in sentences)
        {
            var sentenceEntities = entities.Where(e => 
                sentence.Contains(e.Text, StringComparison.OrdinalIgnoreCase)).ToList();

            if (sentenceEntities.Count >= 2)
            {
                // Simple subject-verb-object extraction
                var subjectVerbObject = ExtractSubjectVerbObject(sentence, sentenceEntities);
                if (subjectVerbObject != null)
                {
                    relationships.Add(subjectVerbObject);
                }
            }
        }

        return relationships;
    }

    private List<ExtractedRelationship> ExtractDomainSpecificRelationships(
        string content, 
        List<NamedEntity> entities, 
        EntityExtractionOptions options)
    {
        var relationships = new List<ExtractedRelationship>();

        if (options.DomainType == "Technical")
        {
            // Extract inheritance, implementation, composition relationships
            var techPatterns = new[]
            {
                (@"(\w+)\s+extends\s+(\w+)", RelationType.Inherits),
                (@"(\w+)\s+implements\s+(\w+)", RelationType.Implements),
                (@"(\w+)\s+contains\s+(\w+)", RelationType.Contains)
            };

            foreach (var (pattern, relationType) in techPatterns)
            {
                var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    // Create relationships based on technical patterns
                    var subject = entities.FirstOrDefault(e => 
                        e.Text.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
                    var objectEntity = entities.FirstOrDefault(e => 
                        e.Text.Equals(match.Groups[2].Value, StringComparison.OrdinalIgnoreCase));

                    if (subject != null && objectEntity != null)
                    {
                        relationships.Add(new ExtractedRelationship
                        {
                            Subject = subject,
                            Predicate = relationType.ToString(),
                            Object = objectEntity,
                            RelationType = relationType,
                            Confidence = 0.9,
                            TextualEvidence = match.Value,
                            Position = match.Index
                        });
                    }
                }
            }
        }

        return relationships;
    }

    private List<ExtractedRelationship> FilterAndScoreRelationships(
        List<ExtractedRelationship> relationships, 
        EntityExtractionOptions options)
    {
        return relationships
            .Where(r => r.Confidence >= options.MinRelationshipConfidence)
            .OrderByDescending(r => r.Confidence)
            .Take(options.MaxRelationshipsPerChunk)
            .ToList();
    }

    // Additional helper methods

    private List<string> SplitIntoSentences(string content)
    {
        return Regex.Split(content, @"[.!?]+\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();
    }

    private List<PronounReference> ExtractPronouns(string sentence, EntityType entityType)
    {
        var pronouns = new List<PronounReference>();
        
        var pronounPatterns = entityType switch
        {
            EntityType.Person => new[] { "he", "she", "him", "her", "his", "hers", "they", "them", "their" },
            EntityType.Organization => new[] { "it", "its", "they", "them", "their" },
            _ => new[] { "it", "its" }
        };

        foreach (var pronoun in pronounPatterns)
        {
            var pattern = $@"\b{pronoun}\b";
            var matches = Regex.Matches(sentence, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                pronouns.Add(new PronounReference
                {
                    Text = match.Value,
                    Position = match.Index
                });
            }
        }

        return pronouns;
    }

    private double CalculatePronounConfidence(
        NamedEntity entity, 
        PronounReference pronoun, 
        string entitySentence, 
        string pronounSentence)
    {
        var baseConfidence = 0.6;

        // Gender agreement for persons
        if (entity.Type == EntityType.Person)
        {
            var isMasculine = new[] { "he", "him", "his" }.Contains(pronoun.Text.ToLowerInvariant());
            var isFeminine = new[] { "she", "her", "hers" }.Contains(pronoun.Text.ToLowerInvariant());
            
            // Simple heuristic: if entity name suggests gender
            if (entity.Text.Contains("Mr") && isMasculine) baseConfidence += 0.2;
            if (entity.Text.Contains("Mrs") && isFeminine) baseConfidence += 0.2;
            if (entity.Text.Contains("Ms") && isFeminine) baseConfidence += 0.2;
        }

        // Distance penalty
        var distance = Math.Abs(entitySentence.Length - pronounSentence.Length);
        if (distance > 100) baseConfidence -= 0.1;

        return Math.Min(1.0, Math.Max(0.1, baseConfidence));
    }

    private string NormalizeEntityText(string text, EntityType type)
    {
        // Remove titles and honorifics
        var normalized = text.Trim();
        
        if (type == EntityType.Person)
        {
            normalized = Regex.Replace(normalized, @"^(Dr|Mr|Mrs|Ms|Prof|Sir|Lady)\.?\s+", "", RegexOptions.IgnoreCase);
        }

        if (type == EntityType.Organization)
        {
            normalized = Regex.Replace(normalized, @"\s+(Inc|Corp|Ltd|LLC|Company|Corporation)\.?$", "", RegexOptions.IgnoreCase);
        }

        return normalized.Trim();
    }

    private string GenerateEntityId(NamedEntity entity)
    {
        var normalizedText = entity.Text.ToLowerInvariant()
            .Replace(" ", "_")
            .Replace(".", "")
            .Replace(",", "");
        
        return $"{entity.Type}_{normalizedText}_{entity.GetHashCode():X8}";
    }

    private List<string> GenerateAliases(NamedEntity entity)
    {
        var aliases = new List<string>();
        
        if (entity.Type == EntityType.Person)
        {
            // Generate initials
            var parts = entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                var initials = string.Join(".", parts.Select(p => p[0])) + ".";
                aliases.Add(initials);
                
                // Last name only
                aliases.Add(parts.Last());
            }
        }

        if (entity.Type == EntityType.Organization)
        {
            // Generate acronym
            var words = entity.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1)
            {
                var acronym = string.Join("", words.Select(w => w[0]));
                aliases.Add(acronym);
            }
        }

        return aliases.Distinct().ToList();
    }

    private string? LinkToKnowledgeBase(NamedEntity entity)
    {
        // Simplified knowledge base linking
        // In real implementation, this would query external knowledge bases
        return entity.Type switch
        {
            EntityType.Person => $"person:{entity.Text.Replace(" ", "_")}",
            EntityType.Organization => $"org:{entity.Text.Replace(" ", "_")}",
            EntityType.Location => $"place:{entity.Text.Replace(" ", "_")}",
            _ => null
        };
    }

    private string DetermineSemanticType(NamedEntity entity)
    {
        return entity.Type switch
        {
            EntityType.Person => "Agent",
            EntityType.Organization => "Organization",
            EntityType.Location => "Place",
            EntityType.Date => "TemporalEntity",
            EntityType.Concept => "AbstractConcept",
            _ => "Entity"
        };
    }

    private Dictionary<string, object> ExtractEntityProperties(NamedEntity entity)
    {
        var properties = new Dictionary<string, object>
        {
            ["confidence"] = entity.Confidence,
            ["length"] = entity.Text.Length,
            ["position"] = entity.StartPosition
        };

        if (entity.Type == EntityType.Date)
        {
            if (DateTime.TryParse(entity.Text, out var date))
            {
                properties["parsed_date"] = date;
                properties["year"] = date.Year;
            }
        }

        return properties;
    }

    private RelationType InferRelationshipType(NamedEntity entity1, NamedEntity entity2)
    {
        return (entity1.Type, entity2.Type) switch
        {
            (EntityType.Person, EntityType.Organization) => RelationType.AffiliatedWith,
            (EntityType.Organization, EntityType.Location) => RelationType.LocatedIn,
            (EntityType.Person, EntityType.Location) => RelationType.LocatedIn,
            (EntityType.Concept, EntityType.Concept) => RelationType.RelatedTo,
            _ => RelationType.AssociatedWith
        };
    }

    private double CalculateProximityConfidence(int distance, int threshold)
    {
        return Math.Max(0.1, 1.0 - ((double)distance / threshold));
    }

    private string ExtractContextText(string content, NamedEntity entity1, NamedEntity entity2)
    {
        var start = Math.Min(entity1.StartPosition, entity2.StartPosition);
        var end = Math.Max(entity1.EndPosition, entity2.EndPosition);
        var contextStart = Math.Max(0, start - 20);
        var contextEnd = Math.Min(content.Length, end + 20);
        
        return content.Substring(contextStart, contextEnd - contextStart);
    }

    private ExtractedRelationship? ExtractSubjectVerbObject(string sentence, List<NamedEntity> entities)
    {
        // Simplified SVO extraction
        var verbPatterns = new[] { "is", "was", "are", "were", "has", "have", "had", "does", "did" };
        
        foreach (var verb in verbPatterns)
        {
            var pattern = $@"(\w+)\s+{verb}\s+(\w+)";
            var match = Regex.Match(sentence, pattern, RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var subject = entities.FirstOrDefault(e => 
                    sentence.Contains(e.Text, StringComparison.OrdinalIgnoreCase) && 
                    sentence.IndexOf(e.Text, StringComparison.OrdinalIgnoreCase) < match.Index);
                var objectEntity = entities.FirstOrDefault(e => 
                    sentence.Contains(e.Text, StringComparison.OrdinalIgnoreCase) && 
                    sentence.IndexOf(e.Text, StringComparison.OrdinalIgnoreCase) > match.Index);

                if (subject != null && objectEntity != null)
                {
                    return new ExtractedRelationship
                    {
                        Subject = subject,
                        Predicate = verb,
                        Object = objectEntity,
                        RelationType = RelationType.RelatedTo,
                        Confidence = 0.6,
                        TextualEvidence = match.Value,
                        Position = match.Index
                    };
                }
            }
        }

        return null;
    }

    // Helper classes for internal use
    private class PronounReference
    {
        public string Text { get; set; } = string.Empty;
        public int Position { get; set; }
    }
}

// Data models and enums

public class EntityExtractionOptions
{
    public double MinEntityConfidence { get; set; } = 0.5;
    public double MinRelationshipConfidence { get; set; } = 0.4;
    public int MaxEntitiesPerChunk { get; set; } = 50;
    public int MaxRelationshipsPerChunk { get; set; } = 30;
    public bool EnableDomainSpecificExtraction { get; set; } = true;
    public bool EnableKnowledgeBaseLinking { get; set; } = false;
    public string DomainType { get; set; } = "General"; // General, Technical, Business, Academic
    
    // Additional properties for compatibility
    public bool EnableNER { get; set; } = true;
    public bool EnableRelationshipExtraction { get; set; } = true;
    public bool EnableCoreferenceResolution { get; set; } = true;
    public bool ExtractPersons { get; set; } = true;
    public bool ExtractOrganizations { get; set; } = true;
    public bool ExtractLocations { get; set; } = true;
    public bool ExtractDates { get; set; } = true;
    public bool ExtractConcepts { get; set; } = true;
}

public class EntityExtractionResult
{
    public string ChunkId { get; set; } = string.Empty;
    public List<NamedEntity> NamedEntities { get; set; } = new();
    public List<ExtractedRelationship> ExtractedRelationships { get; set; } = new();
    public List<CoreferenceChain> CoreferenceChains { get; set; } = new();
    public List<NormalizedEntity> NormalizedEntities { get; set; } = new();
    public double ExtractionConfidence { get; set; }
    public DateTime ExtractionTimestamp { get; set; }
}

public class NamedEntity
{
    public string Text { get; set; } = string.Empty;
    public string Value => Text; // Alias for compatibility
    public EntityType Type { get; set; }
    public string TypeString => Type.ToString(); // String representation of type
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();
}

public class ExtractedRelationship
{
    public NamedEntity Subject { get; set; } = null!;
    public string Predicate { get; set; } = string.Empty;
    public NamedEntity Object { get; set; } = null!;
    public RelationType RelationType { get; set; }
    public double Confidence { get; set; }
    public string TextualEvidence { get; set; } = string.Empty;
    public string Evidence => TextualEvidence; // Alias for compatibility
    public int Position { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
}

public class CoreferenceChain
{
    public NamedEntity MainEntity { get; set; } = null!;
    public List<CoreferenceReference> References { get; set; } = new();
    
    // Compatibility properties
    public List<string> EntityMentions => References.Select(r => r.Text).ToList();
    public string CanonicalForm => MainEntity.Value;
    public double Confidence => References.Count > 0 ? References.Average(r => r.Confidence) : 0.0;
    public string EntityType => MainEntity.TypeString;
}

public class CoreferenceReference
{
    public string Text { get; set; } = string.Empty;
    public int Position { get; set; }
    public int SentenceIndex { get; set; }
    public double Confidence { get; set; }
}

public class NormalizedEntity
{
    public NamedEntity OriginalEntity { get; set; } = null!;
    public string NormalizedForm { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string? LinkedKnowledgeBase { get; set; }
    public string SemanticType { get; set; } = string.Empty;
    public Dictionary<string, object> Properties { get; set; } = new();
}

public enum EntityType
{
    Person,
    Organization,
    Location,
    Date,
    Concept,
    Method,
    Class,
    Interface,
    Property,
    Money,
    Percentage,
    Quarter
}

public enum RelationType
{
    EmployedBy,
    Founded,
    LocatedIn,
    Owns,
    MarriedTo,
    AffiliatedWith,
    RelatedTo,
    AssociatedWith,
    Inherits,
    Implements,
    Contains
}