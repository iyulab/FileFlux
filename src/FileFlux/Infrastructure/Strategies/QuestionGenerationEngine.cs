using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies
{
    /// <summary>
    /// Phase 13: T13-001 - Question Generation Engine
    /// Generates various types of questions from document chunks
    /// </summary>
    public class QuestionGenerationEngine
    {
        private readonly Dictionary<QuestionType, IQuestionGenerator> _generators;
        private readonly QuestionComplexityAnalyzer _complexityAnalyzer;
        private readonly QuestionDiversityOptimizer _diversityOptimizer;

        public QuestionGenerationEngine()
        {
            _generators = new Dictionary<QuestionType, IQuestionGenerator>
            {
                { QuestionType.Factual, new FactualQuestionGenerator() },
                { QuestionType.Conceptual, new ConceptualQuestionGenerator() },
                { QuestionType.Inferential, new InferentialQuestionGenerator() },
                { QuestionType.MultiHop, new MultiHopQuestionGenerator() }
            };

            _complexityAnalyzer = new QuestionComplexityAnalyzer();
            _diversityOptimizer = new QuestionDiversityOptimizer();
        }

        public QuestionGenerationResult GenerateQuestions(DocumentChunk chunk, QuestionGenerationOptions? options = null)
        {
            options ??= new QuestionGenerationOptions();
            var result = new QuestionGenerationResult
            {
                ChunkId = chunk.Id.ToString(),
                ChunkContent = chunk.Content,
                GeneratedAt = DateTime.UtcNow
            };

            try
            {
                // Extract key information from chunk
                var keyInfo = ExtractKeyInformation(chunk);

                // Generate questions by type
                foreach (var kvp in _generators)
                {
                    if (options.QuestionTypes.Contains(kvp.Key))
                    {
                        var questions = kvp.Value.Generate(chunk, keyInfo, options);
                        result.Questions.AddRange(questions);
                    }
                }

                // Analyze complexity
                foreach (var question in result.Questions)
                {
                    question.Complexity = _complexityAnalyzer.AnalyzeComplexity(question);
                }

                // Optimize diversity
                result.Questions = _diversityOptimizer.OptimizeDiversity(
                    result.Questions,
                    options.MaxQuestions
                );

                // Calculate quality metrics
                result.QualityMetrics = CalculateQualityMetrics(result.Questions);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public BatchQuestionGenerationResult GenerateQuestionsForDocument(
            List<DocumentChunk> chunks,
            QuestionGenerationOptions? options = null)
        {
            var batchResult = new BatchQuestionGenerationResult
            {
                DocumentId = chunks.FirstOrDefault()?.Metadata?.FileName ?? string.Empty,
                GeneratedAt = DateTime.UtcNow
            };

            foreach (var chunk in chunks)
            {
                var result = GenerateQuestions(chunk, options);
                batchResult.ChunkResults.Add(result);

                if (result.Success)
                {
                    batchResult.TotalQuestions += result.Questions.Count;
                }
            }

            // Cross-chunk question generation (multi-hop)
            if (options?.EnableCrossChunkQuestions == true)
            {
                var crossChunkQuestions = GenerateCrossChunkQuestions(chunks, options);
                batchResult.CrossChunkQuestions = crossChunkQuestions;
            }

            // Remove duplicates across chunks
            RemoveDuplicateQuestions(batchResult);

            return batchResult;
        }

        private KeyInformation ExtractKeyInformation(DocumentChunk chunk)
        {
            var keyInfo = new KeyInformation
            {
                Entities = ExtractEntities(chunk.Content),
                Facts = ExtractFacts(chunk.Content),
                Concepts = ExtractConcepts(chunk.Content),
                Relations = ExtractRelations(chunk.Content),
                Numbers = ExtractNumbers(chunk.Content),
                Dates = ExtractDates(chunk.Content)
            };

            return keyInfo;
        }

        private List<string> ExtractEntities(string content)
        {
            var entities = new List<string>();

            // Extract proper nouns (simplified NER)
            var properNounPattern = @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b";
            var matches = Regex.Matches(content, properNounPattern);

            foreach (Match match in matches)
            {
                if (!CommonWords.IsCommon(match.Value))
                {
                    entities.Add(match.Value);
                }
            }

            return entities.Distinct().ToList();
        }

        private List<Fact> ExtractFacts(string content)
        {
            var facts = new List<Fact>();
            var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var sentence in sentences)
            {
                if (IsFactualStatement(sentence))
                {
                    facts.Add(new Fact
                    {
                        Statement = sentence.Trim(),
                        Confidence = CalculateFactConfidence(sentence)
                    });
                }
            }

            return facts;
        }

        private List<string> ExtractConcepts(string content)
        {
            var concepts = new List<string>();

            // Extract abstract nouns and technical terms
            var technicalTerms = ExtractTechnicalTerms(content);
            var abstractNouns = ExtractAbstractNouns(content);

            concepts.AddRange(technicalTerms);
            concepts.AddRange(abstractNouns);

            return concepts.Distinct().ToList();
        }

        private List<Relation> ExtractRelations(string content)
        {
            var relations = new List<Relation>();

            // Extract relationships between entities
            var relationPatterns = new[]
            {
                @"(\w+)\s+(?:is|are|was|were)\s+(\w+)",
                @"(\w+)\s+(?:has|have|had)\s+(\w+)",
                @"(\w+)\s+(?:causes|caused|leads to|results in)\s+(\w+)",
                @"(\w+)\s+(?:depends on|relies on|requires)\s+(\w+)"
            };

            foreach (var pattern in relationPatterns)
            {
                var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count >= 3)
                    {
                        relations.Add(new Relation
                        {
                            Subject = match.Groups[1].Value,
                            Predicate = match.Groups[0].Value.Replace(match.Groups[1].Value, "").Replace(match.Groups[2].Value, "").Trim(),
                            Object = match.Groups[2].Value
                        });
                    }
                }
            }

            return relations;
        }

        private List<string> ExtractNumbers(string content)
        {
            var numbers = new List<string>();
            var numberPattern = @"\b\d+(?:\.\d+)?(?:\s*(?:million|billion|thousand|hundred))?\b";
            var matches = Regex.Matches(content, numberPattern);

            foreach (Match match in matches)
            {
                numbers.Add(match.Value);
            }

            return numbers.Distinct().ToList();
        }

        private List<string> ExtractDates(string content)
        {
            var dates = new List<string>();
            var datePatterns = new[]
            {
                @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b",
                @"\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},?\s+\d{4}\b",
                @"\b\d{4}\b"
            };

            foreach (var pattern in datePatterns)
            {
                var matches = Regex.Matches(content, pattern);
                foreach (Match match in matches)
                {
                    dates.Add(match.Value);
                }
            }

            return dates.Distinct().ToList();
        }

        private bool IsFactualStatement(string sentence)
        {
            // Simple heuristic for identifying factual statements
            var factualIndicators = new[] { "is", "are", "was", "were", "has", "have", "contains", "includes" };
            var lowerSentence = sentence.ToLower();

            return factualIndicators.Any(indicator => lowerSentence.Contains(indicator)) &&
                   !sentence.Contains('?') &&
                   sentence.Split(' ').Length > 3;
        }

        private double CalculateFactConfidence(string sentence)
        {
            var confidence = 0.5;

            // Increase confidence for definitive language
            if (Regex.IsMatch(sentence, @"\b(?:always|never|definitely|certainly|must)\b", RegexOptions.IgnoreCase))
                confidence += 0.2;

            // Decrease confidence for hedging language
            if (Regex.IsMatch(sentence, @"\b(?:might|maybe|possibly|could|perhaps)\b", RegexOptions.IgnoreCase))
                confidence -= 0.2;

            // Increase confidence for numerical data
            if (Regex.IsMatch(sentence, @"\d+"))
                confidence += 0.1;

            return Math.Max(0, Math.Min(1, confidence));
        }

        private List<string> ExtractTechnicalTerms(string content)
        {
            var terms = new List<string>();

            // Extract capitalized multi-word terms
            var technicalPattern = @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b";
            var matches = Regex.Matches(content, technicalPattern);

            foreach (Match match in matches)
            {
                terms.Add(match.Value);
            }

            // Extract acronyms
            var acronymPattern = @"\b[A-Z]{2,}\b";
            matches = Regex.Matches(content, acronymPattern);

            foreach (Match match in matches)
            {
                terms.Add(match.Value);
            }

            return terms;
        }

        private List<string> ExtractAbstractNouns(string content)
        {
            var abstractNouns = new List<string>();

            // Common abstract noun suffixes
            var suffixes = new[] { "tion", "ment", "ness", "ity", "ance", "ence", "ship", "hood" };
            var words = content.Split(new[] { ' ', '.', ',', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var lowerWord = word.ToLower();
                if (suffixes.Any(suffix => lowerWord.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    abstractNouns.Add(word);
                }
            }

            return abstractNouns.Distinct().ToList();
        }

        private List<GeneratedQuestion> GenerateCrossChunkQuestions(
            List<DocumentChunk> chunks,
            QuestionGenerationOptions options)
        {
            var crossChunkQuestions = new List<GeneratedQuestion>();
            var multiHopGenerator = _generators[QuestionType.MultiHop];

            // Find relationships across chunks
            for (int i = 0; i < chunks.Count - 1; i++)
            {
                for (int j = i + 1; j < Math.Min(i + 3, chunks.Count); j++)
                {
                    var chunk1Info = ExtractKeyInformation(chunks[i]);
                    var chunk2Info = ExtractKeyInformation(chunks[j]);

                    // Find common entities or concepts
                    var commonEntities = chunk1Info.Entities.Intersect(chunk2Info.Entities).ToList();
                    var commonConcepts = chunk1Info.Concepts.Intersect(chunk2Info.Concepts).ToList();

                    if (commonEntities.Any() || commonConcepts.Any())
                    {
                        var crossChunkContext = new CrossChunkContext
                        {
                            Chunk1 = chunks[i],
                            Chunk2 = chunks[j],
                            CommonEntities = commonEntities,
                            CommonConcepts = commonConcepts
                        };

                        var questions = multiHopGenerator.GenerateCrossChunkQuestions(crossChunkContext);
                        crossChunkQuestions.AddRange(questions);
                    }
                }
            }

            return crossChunkQuestions;
        }

        private void RemoveDuplicateQuestions(BatchQuestionGenerationResult batchResult)
        {
            var allQuestions = new List<GeneratedQuestion>();

            foreach (var chunkResult in batchResult.ChunkResults)
            {
                allQuestions.AddRange(chunkResult.Questions);
            }

            allQuestions.AddRange(batchResult.CrossChunkQuestions);

            // Group similar questions
            var uniqueQuestions = new List<GeneratedQuestion>();
            var addedQuestionTexts = new HashSet<string>();

            foreach (var question in allQuestions.OrderByDescending(q => q.Quality))
            {
                var normalizedText = NormalizeQuestionText(question.QuestionText);

                if (!addedQuestionTexts.Contains(normalizedText))
                {
                    uniqueQuestions.Add(question);
                    addedQuestionTexts.Add(normalizedText);
                }
            }

            // Update results with unique questions
            var questionIndex = 0;
            foreach (var chunkResult in batchResult.ChunkResults)
            {
                chunkResult.Questions.Clear();
                var chunkQuestionCount = Math.Min(5, uniqueQuestions.Count - questionIndex);

                for (int i = 0; i < chunkQuestionCount && questionIndex < uniqueQuestions.Count; i++)
                {
                    chunkResult.Questions.Add(uniqueQuestions[questionIndex++]);
                }
            }

            batchResult.TotalQuestions = uniqueQuestions.Count;
        }

        private string NormalizeQuestionText(string text)
        {
            // Remove extra spaces, lowercase, remove punctuation for comparison
            return Regex.Replace(text.ToLower(), @"[^\w\s]", "").Trim();
        }

        private QualityMetrics CalculateQualityMetrics(List<GeneratedQuestion> questions)
        {
            if (!questions.Any())
            {
                return new QualityMetrics();
            }

            return new QualityMetrics
            {
                AverageComplexity = questions.Average(q => q.Complexity),
                DiversityScore = CalculateDiversityScore(questions),
                CoverageScore = CalculateCoverageScore(questions),
                QualityScore = questions.Average(q => q.Quality)
            };
        }

        private double CalculateDiversityScore(List<GeneratedQuestion> questions)
        {
            if (questions.Count < 2) return 0;

            var uniqueTypes = questions.Select(q => q.Type).Distinct().Count();
            var uniqueComplexities = questions.Select(q => Math.Round(q.Complexity, 1)).Distinct().Count();

            return (uniqueTypes / 4.0 + uniqueComplexities / questions.Count) / 2.0;
        }

        private double CalculateCoverageScore(List<GeneratedQuestion> questions)
        {
            var coveredTopics = new HashSet<string>();

            foreach (var question in questions)
            {
                foreach (var keyword in question.Keywords)
                {
                    coveredTopics.Add(keyword.ToLower());
                }
            }

            return Math.Min(1.0, coveredTopics.Count / 10.0);
        }
    }

    // Supporting classes and interfaces
    public interface IQuestionGenerator
    {
        List<GeneratedQuestion> Generate(DocumentChunk chunk, KeyInformation keyInfo, QuestionGenerationOptions options);
        List<GeneratedQuestion> GenerateCrossChunkQuestions(CrossChunkContext context);
    }

    public class FactualQuestionGenerator : IQuestionGenerator
    {
        public List<GeneratedQuestion> Generate(DocumentChunk chunk, KeyInformation keyInfo, QuestionGenerationOptions options)
        {
            var questions = new List<GeneratedQuestion>();

            // Generate "What" questions for entities
            foreach (var entity in keyInfo.Entities.Take(3))
            {
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"What is {entity}?",
                    Type = QuestionType.Factual,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { entity },
                    Quality = 0.7
                });
            }

            // Generate "When" questions for dates
            foreach (var date in keyInfo.Dates.Take(2))
            {
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"What happened in {date}?",
                    Type = QuestionType.Factual,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { date },
                    Quality = 0.6
                });
            }

            // Generate "How many" questions for numbers
            foreach (var number in keyInfo.Numbers.Take(2))
            {
                var context = ExtractNumberContext(chunk.Content, number);
                if (!string.IsNullOrEmpty(context))
                {
                    questions.Add(new GeneratedQuestion
                    {
                        QuestionText = $"How many {context}?",
                        Type = QuestionType.Factual,
                        ExpectedAnswerLocation = chunk.Id.ToString(),
                        Keywords = new List<string> { number, context },
                        Quality = 0.8
                    });
                }
            }

            return questions;
        }

        public List<GeneratedQuestion> GenerateCrossChunkQuestions(CrossChunkContext context)
        {
            return new List<GeneratedQuestion>();
        }

        private string ExtractNumberContext(string content, string number)
        {
            var index = content.IndexOf(number, StringComparison.Ordinal);
            if (index > 0)
            {
                var words = content.Substring(Math.Max(0, index - 50), Math.Min(100, content.Length - index))
                    .Split(' ');

                // Find noun after the number
                for (int i = 0; i < words.Length - 1; i++)
                {
                    if (words[i].Contains(number) && i < words.Length - 1)
                    {
                        return words[i + 1].Trim('.', ',', '!', '?');
                    }
                }
            }
            return string.Empty;
        }
    }

    public class ConceptualQuestionGenerator : IQuestionGenerator
    {
        public List<GeneratedQuestion> Generate(DocumentChunk chunk, KeyInformation keyInfo, QuestionGenerationOptions options)
        {
            var questions = new List<GeneratedQuestion>();

            // Generate "Why" questions for concepts
            foreach (var concept in keyInfo.Concepts.Take(3))
            {
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"Why is {concept} important?",
                    Type = QuestionType.Conceptual,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { concept, "important" },
                    Quality = 0.75
                });

                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"How does {concept} work?",
                    Type = QuestionType.Conceptual,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { concept, "work" },
                    Quality = 0.8
                });
            }

            return questions;
        }

        public List<GeneratedQuestion> GenerateCrossChunkQuestions(CrossChunkContext context)
        {
            return new List<GeneratedQuestion>();
        }
    }

    public class InferentialQuestionGenerator : IQuestionGenerator
    {
        public List<GeneratedQuestion> Generate(DocumentChunk chunk, KeyInformation keyInfo, QuestionGenerationOptions options)
        {
            var questions = new List<GeneratedQuestion>();

            // Generate inference questions based on relations
            foreach (var relation in keyInfo.Relations.Take(2))
            {
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"What can be inferred from the relationship between {relation.Subject} and {relation.Object}?",
                    Type = QuestionType.Inferential,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { relation.Subject, relation.Object, "relationship" },
                    Quality = 0.85
                });
            }

            // Generate cause-effect questions
            if (keyInfo.Relations.Any(r => r.Predicate.Contains("causes") || r.Predicate.Contains("leads to")))
            {
                var causalRelation = keyInfo.Relations.First(r => r.Predicate.Contains("causes") || r.Predicate.Contains("leads to"));
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"What are the implications of {causalRelation.Subject} on {causalRelation.Object}?",
                    Type = QuestionType.Inferential,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { causalRelation.Subject, causalRelation.Object, "implications" },
                    Quality = 0.9
                });
            }

            return questions;
        }

        public List<GeneratedQuestion> GenerateCrossChunkQuestions(CrossChunkContext context)
        {
            return new List<GeneratedQuestion>();
        }
    }

    public class MultiHopQuestionGenerator : IQuestionGenerator
    {
        public List<GeneratedQuestion> Generate(DocumentChunk chunk, KeyInformation keyInfo, QuestionGenerationOptions options)
        {
            var questions = new List<GeneratedQuestion>();

            // Generate multi-hop questions requiring multiple facts
            if (keyInfo.Facts.Count >= 2 && keyInfo.Entities.Count >= 2)
            {
                var entity1 = keyInfo.Entities[0];
                var entity2 = keyInfo.Entities[1];

                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"How does {entity1} relate to {entity2} and what is the impact?",
                    Type = QuestionType.MultiHop,
                    ExpectedAnswerLocation = chunk.Id.ToString(),
                    Keywords = new List<string> { entity1, entity2, "relate", "impact" },
                    Quality = 0.9,
                    RequiredHops = 2
                });
            }

            return questions;
        }

        public List<GeneratedQuestion> GenerateCrossChunkQuestions(CrossChunkContext context)
        {
            var questions = new List<GeneratedQuestion>();

            foreach (var entity in context.CommonEntities.Take(2))
            {
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = $"How does the information about {entity} in the first section relate to its description in the later section?",
                    Type = QuestionType.MultiHop,
                    ExpectedAnswerLocation = $"{context.Chunk1.Id},{context.Chunk2.Id}",
                    Keywords = new List<string> { entity, "relate", "section" },
                    Quality = 0.95,
                    RequiredHops = 2
                });
            }

            return questions;
        }
    }

    // Supporting data structures
    public class QuestionGenerationOptions
    {
        public List<QuestionType> QuestionTypes { get; set; } = new List<QuestionType>
        {
            QuestionType.Factual,
            QuestionType.Conceptual,
            QuestionType.Inferential,
            QuestionType.MultiHop
        };
        public int MaxQuestions { get; set; } = 10;
        public bool EnableCrossChunkQuestions { get; set; } = true;
        public double MinimumQuality { get; set; } = 0.6;
        public ComplexityLevel TargetComplexity { get; set; } = ComplexityLevel.Medium;
    }

    public enum QuestionType
    {
        Factual,
        Conceptual,
        Inferential,
        MultiHop
    }

    public enum ComplexityLevel
    {
        Low,
        Medium,
        High
    }

    public class GeneratedQuestion
    {
        public string QuestionText { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public string ExpectedAnswerLocation { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new List<string>();
        public double Quality { get; set; }
        public double Complexity { get; set; }
        public int RequiredHops { get; set; } = 1;
    }

    public class QuestionGenerationResult
    {
        public string ChunkId { get; set; } = string.Empty;
        public string ChunkContent { get; set; } = string.Empty;
        public List<GeneratedQuestion> Questions { get; set; } = new List<GeneratedQuestion>();
        public QualityMetrics QualityMetrics { get; set; } = null!;
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class BatchQuestionGenerationResult
    {
        public string DocumentId { get; set; } = string.Empty;
        public List<QuestionGenerationResult> ChunkResults { get; set; } = new List<QuestionGenerationResult>();
        public List<GeneratedQuestion> CrossChunkQuestions { get; set; } = new List<GeneratedQuestion>();
        public int TotalQuestions { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class KeyInformation
    {
        public List<string> Entities { get; set; } = new List<string>();
        public List<Fact> Facts { get; set; } = new List<Fact>();
        public List<string> Concepts { get; set; } = new List<string>();
        public List<Relation> Relations { get; set; } = new List<Relation>();
        public List<string> Numbers { get; set; } = new List<string>();
        public List<string> Dates { get; set; } = new List<string>();
    }

    public class Fact
    {
        public string Statement { get; set; } = string.Empty;
        public double Confidence { get; set; }
    }

    public class Relation
    {
        public string Subject { get; set; } = string.Empty;
        public string Predicate { get; set; } = string.Empty;
        public string Object { get; set; } = string.Empty;
    }

    public class CrossChunkContext
    {
        public DocumentChunk Chunk1 { get; set; } = null!;
        public DocumentChunk Chunk2 { get; set; } = null!;
        public List<string> CommonEntities { get; set; } = new List<string>();
        public List<string> CommonConcepts { get; set; } = new List<string>();
    }

    public class QualityMetrics
    {
        public double AverageComplexity { get; set; }
        public double DiversityScore { get; set; }
        public double CoverageScore { get; set; }
        public double QualityScore { get; set; }
    }

    public class QuestionComplexityAnalyzer
    {
        public double AnalyzeComplexity(GeneratedQuestion question)
        {
            var complexity = 0.5;

            // Word count factor
            var wordCount = question.QuestionText.Split(' ').Length;
            complexity += Math.Min(0.2, wordCount / 50.0);

            // Question type factor
            complexity += question.Type switch
            {
                QuestionType.Factual => 0.0,
                QuestionType.Conceptual => 0.1,
                QuestionType.Inferential => 0.2,
                QuestionType.MultiHop => 0.3,
                _ => 0.0
            };

            // Multi-hop factor
            if (question.RequiredHops > 1)
            {
                complexity += 0.1 * (question.RequiredHops - 1);
            }

            return Math.Min(1.0, complexity);
        }
    }

    public class QuestionDiversityOptimizer
    {
        public List<GeneratedQuestion> OptimizeDiversity(List<GeneratedQuestion> questions, int maxQuestions)
        {
            if (questions.Count <= maxQuestions)
                return questions;

            var optimized = new List<GeneratedQuestion>();
            var typeGroups = questions.GroupBy(q => q.Type).ToList();

            // Ensure at least one question of each type
            foreach (var group in typeGroups)
            {
                optimized.Add(group.OrderByDescending(q => q.Quality).First());
            }

            // Fill remaining slots with highest quality questions
            var remaining = questions.Except(optimized)
                .OrderByDescending(q => q.Quality)
                .Take(maxQuestions - optimized.Count);

            optimized.AddRange(remaining);

            return optimized.Take(maxQuestions).ToList();
        }
    }

    public static class CommonWords
    {
        private static readonly HashSet<string> _commonWords = new HashSet<string>
        {
            "The", "This", "That", "These", "Those", "There", "Here",
            "Some", "Any", "All", "Many", "Few", "Several", "Both"
        };

        public static bool IsCommon(string word)
        {
            return _commonWords.Contains(word);
        }
    }
}
