using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFlux.Domain;
using FileFlux.Exceptions;

namespace FileFlux.Infrastructure.Quality;

/// <summary>
/// Internal quality engine that powers both internal benchmarking and external API.
/// Provides consistent quality metrics and validation logic across all use cases.
/// </summary>
public class ChunkQualityEngine
{
    private readonly ITextCompletionService? _textCompletionService;

    public ChunkQualityEngine(ITextCompletionService? textCompletionService = null)
    {
        _textCompletionService = textCompletionService;
    }

    /// <summary>
    /// Calculates comprehensive quality metrics for document chunks.
    /// This method is used by both internal benchmarks and external API.
    /// </summary>
    internal async Task<ChunkingQualityMetrics> CalculateQualityMetricsAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var comprehensive = await CalculateComprehensiveQualityMetricsAsync(chunks, cancellationToken);
        var (chunking, _, _) = comprehensive.ToSeparatedMetrics();
        return chunking;
    }

    /// <summary>
    /// Calculates comprehensive quality metrics for internal use
    /// </summary>
    private async Task<ComprehensiveQualityMetrics> CalculateComprehensiveQualityMetricsAsync(
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        if (!chunkList.Any())
            return new ComprehensiveQualityMetrics();

        var metrics = new ComprehensiveQualityMetrics();

        // Calculate individual chunk quality scores
        var chunkQualities = await Task.WhenAll(
            chunkList.Select(async chunk => await AnalyzeChunkQualityAsync(chunk, cancellationToken))
        );

        // Aggregate chunking quality metrics
        metrics.AverageCompleteness = chunkQualities.Average(q => q.Completeness);
        metrics.ContentConsistency = CalculateContentConsistency(chunkList);
        metrics.BoundaryQuality = CalculateBoundaryQuality(chunkList);
        metrics.SizeDistribution = CalculateSizeDistribution(chunkList);
        metrics.OverlapEffectiveness = CalculateOverlapEffectiveness(chunkList);

        // Calculate information density metrics
        metrics.AverageInformationDensity = CalculateInformationDensity(chunkList);
        metrics.KeywordRichness = CalculateKeywordRichness(chunkList);
        metrics.FactualContentRatio = CalculateFactualContentRatio(chunkList);
        metrics.RedundancyLevel = CalculateRedundancyLevel(chunkList);

        // Calculate structural coherence metrics
        metrics.StructurePreservation = CalculateStructurePreservation(chunkList);
        metrics.ContextContinuity = CalculateContextContinuity(chunkList);
        metrics.ReferenceIntegrity = CalculateReferenceIntegrity(chunkList);
        metrics.MetadataRichness = CalculateMetadataRichness(chunkList);

        return metrics;
    }

    /// <summary>
    /// Generates questions from document content for QA benchmarking.
    /// Same logic used in both internal tests and external API.
    /// </summary>
    internal async Task<List<GeneratedQuestion>> GenerateQuestionsAsync(
        ParsedDocumentContent content,
        int questionCount,
        CancellationToken cancellationToken = default)
    {
        var questions = new List<GeneratedQuestion>();

        try
        {
            // Calculate how many questions of each type to generate
            var typeCounts = new Dictionary<QuestionType, int>();
            var questionTypes = Enum.GetValues<QuestionType>().ToList();
            var baseCount = questionCount / questionTypes.Count;
            var remainder = questionCount % questionTypes.Count;

            // Distribute questions evenly across all types
            for (int i = 0; i < questionTypes.Count; i++)
            {
                var type = questionTypes[i];
                typeCounts[type] = baseCount + (i < remainder ? 1 : 0);
            }

            // Generate questions for each type
            foreach (var (type, count) in typeCounts)
            {
                if (count > 0)
                {
                    var typeQuestions = await GenerateQuestionsByType(content, type, count, cancellationToken);
                    questions.AddRange(typeQuestions);
                }
            }

            // If we still don't have enough questions, add more
            while (questions.Count < questionCount)
            {
                var randomType = questionTypes[Random.Shared.Next(questionTypes.Count)];
                var additionalQuestions = await GenerateQuestionsByType(content, randomType, 1, cancellationToken);
                questions.AddRange(additionalQuestions);
            }
        }
        catch (Exception ex)
        {
            // Fallback to basic question generation with varied types
            var basicQuestions = await GenerateBasicQuestionsWithTypes(content, questionCount, cancellationToken);
            questions.AddRange(basicQuestions);
        }

        return questions.Take(questionCount).ToList();
    }

    /// <summary>
    /// Validates answerability of questions against document chunks.
    /// Critical for measuring RAG system performance.
    /// </summary>
    internal async Task<QAValidationResult> ValidateAnswerabilityAsync(
        List<GeneratedQuestion> questions,
        IEnumerable<DocumentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var result = new QAValidationResult
        {
            TotalQuestions = questions.Count
        };

        var validations = await Task.WhenAll(
            questions.Select(async q => await ValidateQuestionAnswerabilityAsync(q, chunkList, cancellationToken))
        );

        result.AnswerableQuestions = validations.Count(v => v.IsAnswerable);
        result.HighQualityAnswers = validations.Count(v => v.IsHighQuality);
        result.AverageConfidence = validations.Any() ? validations.Average(v => v.Confidence) : 0;

        // Calculate detailed validation metrics
        result.ValidationMetrics = CalculateValidationMetrics(validations);

        return result;
    }

    /// <summary>
    /// Calculates overall answerability score for QA benchmark.
    /// Used by both internal benchmarking and external API.
    /// </summary>
    internal double CalculateAnswerabilityScore(QAValidationResult validation)
    {
        if (validation.TotalQuestions == 0) return 0;

        // Weighted score: answerability (70%) + quality (30%)
        var answerabilityWeight = 0.7;
        var qualityWeight = 0.3;

        var answerabilityScore = validation.AnswerabilityRatio;
        var qualityScore = validation.HighQualityRatio;

        return (answerabilityScore * answerabilityWeight) + (qualityScore * qualityWeight);
    }

    /// <summary>
    /// Calculates overall document quality score combining all metrics.
    /// </summary>
    internal double CalculateOverallQualityScore(
        ChunkingQualityMetrics chunkingQuality,
        InformationDensityMetrics informationDensity,
        StructuralCoherenceMetrics structuralCoherence)
    {
        // Weighted combination of metric categories
        var chunkingWeight = 0.4;
        var densityWeight = 0.3;
        var structureWeight = 0.3;

        var chunkingScore = (chunkingQuality.AverageCompleteness + 
                           chunkingQuality.ContentConsistency + 
                           chunkingQuality.BoundaryQuality + 
                           chunkingQuality.SizeDistribution + 
                           chunkingQuality.OverlapEffectiveness) / 5;

        var densityScore = (informationDensity.AverageInformationDensity +
                           informationDensity.KeywordRichness +
                           informationDensity.FactualContentRatio +
                           (1 - informationDensity.RedundancyLevel)) / 4;

        var structureScore = (structuralCoherence.StructurePreservation +
                             structuralCoherence.ContextContinuity +
                             structuralCoherence.ReferenceIntegrity +
                             structuralCoherence.MetadataRichness) / 4;

        return (chunkingScore * chunkingWeight) + 
               (densityScore * densityWeight) + 
               (structureScore * structureWeight);
    }

    /// <summary>
    /// Generates quality recommendations based on analysis results.
    /// </summary>
    internal List<QualityRecommendation> GenerateRecommendations(
        ChunkingQualityMetrics chunkingQuality,
        InformationDensityMetrics informationDensity,
        StructuralCoherenceMetrics structuralCoherence,
        ChunkingOptions currentOptions)
    {
        var recommendations = new List<QualityRecommendation>();

        // Chunk size recommendations
        if (chunkingQuality.SizeDistribution < 0.7)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.ChunkSize,
                Priority = (int)RecommendationPriority.High,
                Description = "Consider adjusting chunk size for better size distribution uniformity",
                ExpectedImprovement = 0.15,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["MaxChunkSize"] = Math.Max(256, currentOptions.MaxChunkSize * 0.8),
                    ["Reason"] = "Current chunking produces uneven chunk sizes"
                }
            });
        }

        // Boundary quality recommendations
        if (chunkingQuality.BoundaryQuality < 0.6)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.ChunkingStrategy,
                Priority = (int)RecommendationPriority.Critical,
                Description = "Switch to Intelligent chunking strategy for better semantic boundary detection",
                ExpectedImprovement = 0.25,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["Strategy"] = "Intelligent",
                    ["Reason"] = "Current strategy produces poor semantic boundaries"
                }
            });
        }

        // Information density recommendations
        if (informationDensity.RedundancyLevel > 0.7)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.ContentFiltering,
                Priority = (int)RecommendationPriority.Medium,
                Description = "High redundancy detected - consider content filtering or preprocessing",
                ExpectedImprovement = 0.12,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["EnableContentFiltering"] = true,
                    ["RedundancyThreshold"] = 0.5
                }
            });
        }

        // Structure preservation recommendations
        if (structuralCoherence.StructurePreservation < 0.7)
        {
            recommendations.Add(new QualityRecommendation
            {
                Type = RecommendationType.StructurePreservation,
                Priority = (int)RecommendationPriority.High,
                Description = "Enable structure preservation to maintain document hierarchy",
                ExpectedImprovement = 0.18,
                SuggestedParameters = new Dictionary<string, object>
                {
                    ["PreserveStructure"] = true,
                    ["IncludeMetadata"] = true
                }
            });
        }

        return recommendations.OrderByDescending(r => r.ExpectedImprovement).ToList();
    }

    #region Private Helper Methods

    private async Task<(double Completeness, double Coherence)> AnalyzeChunkQualityAsync(
        DocumentChunk chunk, CancellationToken cancellationToken)
    {
        // Analyze chunk completeness and coherence
        var completeness = AnalyzeChunkCompleteness(chunk);
        var coherence = AnalyzeChunkCoherence(chunk);

        return (completeness, coherence);
    }

    private double AnalyzeChunkCompleteness(DocumentChunk chunk)
    {
        var content = chunk.Content.Trim();
        if (string.IsNullOrEmpty(content)) return 0;

        var score = 0.5; // Base score

        // Check for complete sentences
        if (content.EndsWith('.') || content.EndsWith('!') || content.EndsWith('?'))
            score += 0.2;

        // Check for proper capitalization
        if (char.IsUpper(content[0]))
            score += 0.1;

        // Check for reasonable length
        if (content.Length > 50 && content.Length < 2000)
            score += 0.2;

        return Math.Min(1.0, score);
    }

    private double AnalyzeChunkCoherence(DocumentChunk chunk)
    {
        var content = chunk.Content;
        if (string.IsNullOrEmpty(content)) return 0;

        var sentences = content.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length < 2) return 0.8; // Single sentence is coherent

        // Simple coherence analysis based on sentence connectivity
        var coherenceScore = 0.5;

        // Check for transition words
        var transitionWords = new[] { "however", "therefore", "additionally", "furthermore", "moreover", "consequently" };
        var transitionCount = transitionWords.Count(word => 
            content.Contains(word, StringComparison.OrdinalIgnoreCase));

        coherenceScore += Math.Min(0.3, transitionCount * 0.1);

        // Check for consistent terminology
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var repetitionRatio = 1.0 - ((double)uniqueWords / words.Length);
        
        if (repetitionRatio > 0.1 && repetitionRatio < 0.5)
            coherenceScore += 0.2;

        return Math.Min(1.0, coherenceScore);
    }

    private double CalculateContentConsistency(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        // Analyze consistency in writing style, terminology, and format
        var avgLengths = chunks.Select(c => (double)c.Content.Length);
        var lengthVariance = CalculateVariance(avgLengths);
        var lengthConsistency = Math.Max(0, 1.0 - (lengthVariance / 100000)); // Normalize variance

        return Math.Min(1.0, lengthConsistency);
    }

    private double CalculateBoundaryQuality(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        var boundaryScores = new List<double>();

        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var currentChunk = chunks[i];
            var nextChunk = chunks[i + 1];

            var boundaryScore = AnalyzeBoundaryQuality(currentChunk, nextChunk);
            boundaryScores.Add(boundaryScore);
        }

        return boundaryScores.Any() ? boundaryScores.Average() : 0.5;
    }

    private double AnalyzeBoundaryQuality(DocumentChunk current, DocumentChunk next)
    {
        var score = 0.5; // Base score

        // Check if current chunk ends with sentence boundary
        var currentContent = current.Content.Trim();
        if (currentContent.EndsWith('.') || currentContent.EndsWith('!') || currentContent.EndsWith('?'))
            score += 0.25;

        // Check if next chunk starts with capital letter
        var nextContent = next.Content.Trim();
        if (nextContent.Length > 0 && char.IsUpper(nextContent[0]))
            score += 0.25;

        return Math.Min(1.0, score);
    }

    private double CalculateSizeDistribution(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0;

        var sizes = chunks.Select(c => (double)c.Content.Length);
        var avgSize = sizes.Average();
        var variance = CalculateVariance(sizes);
        var coefficient = variance > 0 ? Math.Sqrt(variance) / avgSize : 0;

        // Lower coefficient of variation indicates better size distribution
        return Math.Max(0, 1.0 - coefficient);
    }

    private double CalculateOverlapEffectiveness(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;

        var overlapScores = new List<double>();

        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var current = chunks[i];
            var next = chunks[i + 1];

            // Analyze overlap quality between adjacent chunks
            var overlapScore = AnalyzeOverlapQuality(current, next);
            overlapScores.Add(overlapScore);
        }

        return overlapScores.Any() ? overlapScores.Average() : 0.5;
    }

    private double AnalyzeOverlapQuality(DocumentChunk current, DocumentChunk next)
    {
        // Simple word-based overlap analysis
        var currentWords = current.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nextWords = next.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var overlap = currentWords.Intersect(nextWords, StringComparer.OrdinalIgnoreCase).Count();
        var union = currentWords.Union(nextWords, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (double)overlap / union : 0;
    }

    private double CalculateVariance(IEnumerable<double> values)
    {
        var valueList = values.ToList();
        if (!valueList.Any()) return 0;

        var mean = valueList.Average();
        var variance = valueList.Sum(v => Math.Pow(v - mean, 2)) / valueList.Count;
        return variance;
    }

    private async Task<List<GeneratedQuestion>> GenerateQuestionsByType(
        ParsedDocumentContent content,
        QuestionType questionType,
        int count,
        CancellationToken cancellationToken)
    {
        if (count <= 0) return new List<GeneratedQuestion>();

        var questions = new List<GeneratedQuestion>();
        
        // If no text completion service is available, use fallback generation
        if (_textCompletionService == null)
        {
            // Generate questions with proper type
            for (int i = 0; i < count; i++)
            {
                var question = GenerateQuestionForType(content, questionType, i);
                if (question != null)
                {
                    questions.Add(question);
                }
            }
            return questions;
        }

        var prompt = CreateQuestionGenerationPrompt(content.StructuredText, questionType, count);

        try
        {
            var response = await _textCompletionService.GenerateAsync(prompt, cancellationToken);
            questions = ParseGeneratedQuestions(response, questionType);
        }
        catch
        {
            // Fallback to type-specific generation
            for (int i = 0; i < count; i++)
            {
                var question = GenerateQuestionForType(content, questionType, i);
                if (question != null)
                {
                    questions.Add(question);
                }
            }
        }

        return questions.Take(count).ToList();
    }

    private GeneratedQuestion? GenerateQuestionForType(ParsedDocumentContent content, QuestionType questionType, int index)
    {
        var contentSnippet = content.StructuredText.Length > 100 
            ? content.StructuredText.Substring(index * 20 % content.StructuredText.Length, Math.Min(100, content.StructuredText.Length - (index * 20 % content.StructuredText.Length)))
            : content.StructuredText;

        var keyword = GetKeywordFromContent(contentSnippet);
        
        var question = questionType switch
        {
            QuestionType.Factual => $"What specific information is provided about {keyword}?",
            QuestionType.Conceptual => $"How is {keyword} defined or explained in the document?",
            QuestionType.Analytical => $"What is the significance of {keyword} in this context?",
            QuestionType.Procedural => $"What process or steps are associated with {keyword}?",
            QuestionType.Comparative => $"How does {keyword} relate to other concepts in the document?",
            _ => $"What does the document say about {keyword}?"
        };

        return new GeneratedQuestion
        {
            Question = question,
            ExpectedAnswer = contentSnippet.Trim(),
            Type = questionType,
            ConfidenceScore = 0.7,
            DifficultyScore = questionType switch
            {
                QuestionType.Factual => 0.3,
                QuestionType.Conceptual => 0.5,
                QuestionType.Analytical => 0.7,
                QuestionType.Procedural => 0.6,
                QuestionType.Comparative => 0.8,
                _ => 0.5
            }
        };
    }

    private string GetKeywordFromContent(string content)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 4 && !IsStopWord(w))
            .Select(w => w.Trim('.', ',', '!', '?', ';', ':'))
            .ToList();
        
        return words.Any() ? words[Random.Shared.Next(words.Count)] : "this topic";
    }

    private string CreateQuestionGenerationPrompt(string content, QuestionType questionType, int count)
    {
        var typeDescription = questionType switch
        {
            QuestionType.Factual => "factual questions about specific information, data, or details",
            QuestionType.Conceptual => "conceptual questions about explanations, definitions, or understanding",
            QuestionType.Analytical => "analytical questions requiring reasoning or comparison",
            QuestionType.Procedural => "procedural questions about processes or steps",
            _ => "general questions about the content"
        };

        return $@"Based on the following document content, generate {count} {typeDescription}.

Content:
{content.Substring(0, Math.Min(content.Length, 2000))}...

Generate exactly {count} questions in the following format:
Q: [question]
A: [expected answer]

---

";
    }

    private List<GeneratedQuestion> ParseGeneratedQuestions(string response, QuestionType questionType)
    {
        var questions = new List<GeneratedQuestion>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        GeneratedQuestion? currentQuestion = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("Q:", StringComparison.OrdinalIgnoreCase))
            {
                if (currentQuestion != null)
                {
                    questions.Add(currentQuestion);
                }

                currentQuestion = new GeneratedQuestion
                {
                    Question = line.Substring(2).Trim(),
                    Type = questionType,
                    ConfidenceScore = 0.8,
                    DifficultyScore = questionType switch
                    {
                        QuestionType.Factual => 0.3,
                        QuestionType.Conceptual => 0.5,
                        QuestionType.Analytical => 0.7,
                        QuestionType.Procedural => 0.6,
                        _ => 0.5
                    }
                };
            }
            else if (line.StartsWith("A:", StringComparison.OrdinalIgnoreCase) && currentQuestion != null)
            {
                currentQuestion.ExpectedAnswer = line.Substring(2).Trim();
            }
        }

        if (currentQuestion != null)
        {
            questions.Add(currentQuestion);
        }

        return questions;
    }

    private async Task<List<GeneratedQuestion>> GenerateBasicQuestions(
        ParsedDocumentContent content,
        int count,
        CancellationToken cancellationToken)
    {
        // Fallback: generate simple questions based on content structure
        var questions = new List<GeneratedQuestion>();
        var sentences = content.StructuredText.Split('.', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < Math.Min(count, sentences.Length) && i < 10; i++)
        {
            var sentence = sentences[i].Trim();
            if (sentence.Length > 20)
            {
                questions.Add(new GeneratedQuestion
                {
                    Question = $"What does the document say about {sentence.Substring(0, Math.Min(30, sentence.Length))}...?",
                    ExpectedAnswer = sentence,
                    Type = QuestionType.Factual,
                    ConfidenceScore = 0.6,
                    DifficultyScore = 0.4
                });
            }
        }

        return questions;
    }

    private async Task<List<GeneratedQuestion>> GenerateBasicQuestionsWithTypes(
        ParsedDocumentContent content,
        int count,
        CancellationToken cancellationToken)
    {
        // Fallback: generate simple questions with varied types
        var questions = new List<GeneratedQuestion>();
        var sentences = content.StructuredText.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var questionTypes = Enum.GetValues<QuestionType>().ToArray();

        for (int i = 0; i < Math.Min(count, sentences.Length * 2) && questions.Count < count; i++)
        {
            var sentenceIndex = i % sentences.Length;
            var sentence = sentences[sentenceIndex].Trim();
            if (sentence.Length > 20)
            {
                var questionType = questionTypes[i % questionTypes.Length];
                var question = questionType switch
                {
                    QuestionType.Factual => $"What does the document say about {sentence.Substring(0, Math.Min(30, sentence.Length))}...?",
                    QuestionType.Conceptual => $"How is the concept of {GetKeyword(sentence)} explained?",
                    QuestionType.Analytical => $"Why is {GetKeyword(sentence)} important in this context?",
                    QuestionType.Procedural => $"What are the steps related to {GetKeyword(sentence)}?",
                    QuestionType.Comparative => $"How does {GetKeyword(sentence)} compare to alternatives?",
                    _ => $"What information is provided about {GetKeyword(sentence)}?"
                };

                questions.Add(new GeneratedQuestion
                {
                    Question = question,
                    ExpectedAnswer = sentence,
                    Type = questionType,
                    ConfidenceScore = 0.6,
                    DifficultyScore = questionType switch
                    {
                        QuestionType.Factual => 0.3,
                        QuestionType.Conceptual => 0.5,
                        QuestionType.Analytical => 0.7,
                        QuestionType.Procedural => 0.6,
                        QuestionType.Comparative => 0.8,
                        _ => 0.5
                    }
                });
            }
        }

        return questions.Take(count).ToList();
    }

    private string GetKeyword(string sentence)
    {
        // Extract a meaningful keyword from the sentence
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 4 && !IsStopWord(w))
            .ToList();
        
        return words.Any() ? words.First() : "this topic";
    }

    private async Task<(bool IsAnswerable, bool IsHighQuality, double Confidence)> ValidateQuestionAnswerabilityAsync(
        GeneratedQuestion question,
        List<DocumentChunk> chunks,
        CancellationToken cancellationToken)
    {
        // Find chunks that might contain the answer
        var relevantChunks = FindRelevantChunks(question, chunks);
        
        if (!relevantChunks.Any())
            return (false, false, 0.0);

        // Simple validation based on content overlap
        var confidence = CalculateAnswerConfidence(question, relevantChunks);
        var isAnswerable = confidence > 0.3;
        var isHighQuality = confidence > 0.6;

        return (isAnswerable, isHighQuality, confidence);
    }

    private List<DocumentChunk> FindRelevantChunks(GeneratedQuestion question, List<DocumentChunk> chunks)
    {
        var questionWords = question.Question.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        return chunks
            .Select(chunk => new { Chunk = chunk, Score = CalculateRelevanceScore(chunk, questionWords) })
            .Where(x => x.Score > 0.1)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Chunk)
            .ToList();
    }

    private double CalculateRelevanceScore(DocumentChunk chunk, HashSet<string> questionWords)
    {
        var chunkWords = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .ToHashSet();

        var intersection = questionWords.Intersect(chunkWords).Count();
        var union = questionWords.Union(chunkWords).Count();

        return union > 0 ? (double)intersection / questionWords.Count : 0;
    }

    private double CalculateAnswerConfidence(GeneratedQuestion question, List<DocumentChunk> relevantChunks)
    {
        if (!relevantChunks.Any()) return 0;

        var questionWords = question.Question.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var totalScore = 0.0;
        foreach (var chunk in relevantChunks)
        {
            var chunkWords = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var overlap = questionWords.Intersect(chunkWords).Count();
            var score = questionWords.Count > 0 ? (double)overlap / questionWords.Count : 0;
            totalScore += score;
        }

        return Math.Min(1.0, totalScore / relevantChunks.Count);
    }

    private Dictionary<string, object> CalculateValidationMetrics(
        (bool IsAnswerable, bool IsHighQuality, double Confidence)[] validations)
    {
        return new Dictionary<string, object>
        {
            ["AnswerableCount"] = validations.Count(v => v.IsAnswerable),
            ["HighQualityCount"] = validations.Count(v => v.IsHighQuality),
            ["AverageConfidence"] = validations.Any() ? validations.Average(v => v.Confidence) : 0,
            ["ConfidenceDistribution"] = validations.GroupBy(v => Math.Round(v.Confidence, 1))
                .ToDictionary(g => g.Key.ToString(), g => g.Count())
        };
    }

    #endregion

    #region Information Density Metrics

    private double CalculateInformationDensity(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0.0;
        
        // Simple heuristic: ratio of meaningful words to total words
        var densities = chunks.Select(chunk =>
        {
            var words = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var meaningfulWords = words.Where(w => w.Length > 3 && !IsStopWord(w)).Count();
            return words.Length > 0 ? (double)meaningfulWords / words.Length : 0.0;
        });

        return densities.Average();
    }

    private double CalculateKeywordRichness(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0.0;
        
        // Measure density of technical/important keywords
        var keywordPatterns = new[] { "api", "data", "system", "method", "process", "result", "analysis" };
        var totalWords = chunks.Sum(c => c.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var keywordCount = chunks.Sum(c => keywordPatterns.Count(kw => 
            c.Content.ToLowerInvariant().Contains(kw)));
            
        return totalWords > 0 ? Math.Min(1.0, (double)keywordCount / totalWords * 10) : 0.0;
    }

    private double CalculateFactualContentRatio(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0.0;
        
        // Simple heuristic: chunks with numbers, specific terms, etc.
        var factualChunks = chunks.Count(chunk =>
            System.Text.RegularExpressions.Regex.IsMatch(chunk.Content, @"\d+") ||
            chunk.Content.Contains("percent") ||
            chunk.Content.Contains("result") ||
            chunk.Content.Contains("data"));
            
        return (double)factualChunks / chunks.Count;
    }

    private double CalculateRedundancyLevel(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 0.0;
        
        // Measure similarity between consecutive chunks
        var similarities = new List<double>();
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var similarity = CalculateSimilarity(chunks[i].Content, chunks[i + 1].Content);
            similarities.Add(similarity);
        }
        
        return similarities.Average();
    }

    #endregion

    #region Structural Coherence Metrics

    private double CalculateStructurePreservation(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0.0;
        
        // Check for structure markers (headers, lists, etc.)
        var structuredChunks = chunks.Count(chunk =>
            chunk.Content.Contains("#") ||
            chunk.Content.Contains("- ") ||
            chunk.Content.Contains("1.") ||
            chunk.Content.Contains("•"));
            
        return (double)structuredChunks / chunks.Count;
    }

    private double CalculateContextContinuity(List<DocumentChunk> chunks)
    {
        if (chunks.Count < 2) return 1.0;
        
        // Measure logical flow between consecutive chunks
        var continuityScores = new List<double>();
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            var score = MeasureContextualContinuity(chunks[i], chunks[i + 1]);
            continuityScores.Add(score);
        }
        
        return continuityScores.Average();
    }

    private double CalculateReferenceIntegrity(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0.0;
        
        // Check for proper handling of references, citations, etc.
        var chunksWithReferences = chunks.Count(chunk =>
            chunk.Content.Contains("see") ||
            chunk.Content.Contains("refer") ||
            chunk.Content.Contains("above") ||
            chunk.Content.Contains("below"));
            
        return (double)chunksWithReferences / chunks.Count;
    }

    private double CalculateMetadataRichness(List<DocumentChunk> chunks)
    {
        if (!chunks.Any()) return 0.0;
        
        // Measure richness of chunk metadata
        var richChunks = chunks.Count(chunk =>
            chunk.Metadata != null &&
            !string.IsNullOrEmpty(chunk.Metadata.FileName) &&
            chunk.Metadata.FileSize > 0);
            
        return (double)richChunks / chunks.Count;
    }

    #endregion

    #region Helper Methods

    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string> { "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did" };
        return stopWords.Contains(word.ToLowerInvariant());
    }

    private double CalculateSimilarity(string text1, string text2)
    {
        var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToHashSet();
        var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.ToLowerInvariant()).ToHashSet();
        
        if (!words1.Any() && !words2.Any()) return 1.0;
        if (!words1.Any() || !words2.Any()) return 0.0;
        
        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();
        
        return union > 0 ? (double)intersection / union : 0.0;
    }

    private double MeasureContextualContinuity(DocumentChunk chunk1, DocumentChunk chunk2)
    {
        // Simple heuristic based on content similarity and flow
        var similarity = CalculateSimilarity(chunk1.Content, chunk2.Content);
        
        // Bonus for logical connectors
        var hasConnectors = chunk2.Content.ToLowerInvariant().Any(c =>
            chunk2.Content.Contains("however") ||
            chunk2.Content.Contains("therefore") ||
            chunk2.Content.Contains("furthermore") ||
            chunk2.Content.Contains("moreover"));
            
        return Math.Min(1.0, similarity + (hasConnectors ? 0.2 : 0.0));
    }

    #endregion
}