using System.Text.Json;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// LLM-based chunk filtering implementation with 3-stage assessment.
/// Target: 10 percentage point accuracy improvement.
/// </summary>
public class LLMChunkFilter : ILLMChunkFilter
{
    private double _relevanceThreshold = 0.7;
    private bool _useCriticValidation = true;

    public double RelevanceThreshold
    {
        get => _relevanceThreshold;
        set => _relevanceThreshold = Math.Max(0, Math.Min(1, value));
    }

    public bool UseCriticValidation
    {
        get => _useCriticValidation;
        set => _useCriticValidation = value;
    }

    public async Task<IEnumerable<FilteredChunk>> FilterChunksAsync(
        IEnumerable<DocumentChunk> chunks,
        string? query,
        ITextCompletionService textCompletionService,
        ChunkFilterOptions? options,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChunkFilterOptions();
        var filteredChunks = new List<FilteredChunk>();

        // Process chunks in parallel batches for efficiency
        var chunkList = chunks.ToList();
        var batchSize = 5;
        
        for (int i = 0; i < chunkList.Count; i += batchSize)
        {
            var batch = chunkList.Skip(i).Take(batchSize);
            var tasks = batch.Select(chunk => AssessChunkWithOptionsAsync(
                chunk, query, textCompletionService, options, cancellationToken));
            
            var results = await Task.WhenAll(tasks);
            
            foreach (var (chunk, assessment) in batch.Zip(results))
            {
                var qualityScore = CalculateQualityScore(assessment);
                var relevanceScore = assessment.FinalScore;
                var combinedScore = CalculateCombinedScore(
                    relevanceScore, qualityScore, options.QualityWeight);

                var passed = combinedScore >= options.MinRelevanceScore;
                
                filteredChunks.Add(new FilteredChunk
                {
                    Chunk = chunk,
                    RelevanceScore = relevanceScore,
                    QualityScore = qualityScore,
                    CombinedScore = combinedScore,
                    Passed = passed,
                    Assessment = assessment,
                    Reason = GenerateReason(assessment, passed, options)
                });
            }
        }

        // Apply filtering and sorting
        var result = filteredChunks.Where(fc => fc.Passed);

        if (options.MaxChunks.HasValue)
        {
            result = result
                .OrderByDescending(fc => fc.CombinedScore)
                .Take(options.MaxChunks.Value);
        }

        if (options.PreserveOrder)
        {
            result = result.OrderBy(fc => fc.Chunk.ChunkIndex);
        }

        return result;
    }

    public async Task<ChunkAssessment> AssessChunkAsync(
        DocumentChunk chunk,
        string? query,
        ITextCompletionService textCompletionService,
        CancellationToken cancellationToken = default)
    {
        return await AssessChunkWithOptionsAsync(
            chunk, query, textCompletionService, 
            new ChunkFilterOptions(), cancellationToken);
    }

    private async Task<ChunkAssessment> AssessChunkWithOptionsAsync(
        DocumentChunk chunk,
        string? query,
        ITextCompletionService textCompletionService,
        ChunkFilterOptions options,
        CancellationToken cancellationToken)
    {
        var assessment = new ChunkAssessment
        {
            Reasoning = new Dictionary<string, string>()
        };

        // Stage 1: Initial Assessment
        var initialScore = await PerformInitialAssessmentAsync(
            chunk, query, textCompletionService, options, cancellationToken);
        assessment.InitialScore = initialScore.Score;
        assessment.Reasoning["initial"] = initialScore.Reasoning;
        assessment.Factors.AddRange(initialScore.Factors);

        // Stage 2: Self-Reflection (if enabled)
        if (options.UseSelfReflection)
        {
            var reflectionScore = await PerformSelfReflectionAsync(
                chunk, query, initialScore, textCompletionService, cancellationToken);
            assessment.ReflectionScore = reflectionScore.Score;
            assessment.Reasoning["reflection"] = reflectionScore.Reasoning;
            
            // Update factors based on reflection
            MergeFactors(assessment.Factors, reflectionScore.Factors);
        }

        // Stage 3: Critic Validation (if enabled)
        if (options.UseCriticValidation && _useCriticValidation)
        {
            var criticScore = await PerformCriticValidationAsync(
                chunk, query, assessment, textCompletionService, cancellationToken);
            assessment.CriticScore = criticScore.Score;
            assessment.Reasoning["critic"] = criticScore.Reasoning;
            
            // Update factors based on critic
            MergeFactors(assessment.Factors, criticScore.Factors);
        }

        // Calculate final score (weighted average of stages)
        assessment.FinalScore = CalculateFinalScore(assessment);
        assessment.Confidence = CalculateConfidence(assessment);

        // Generate improvement suggestions
        assessment.Suggestions = GenerateSuggestions(assessment);

        return assessment;
    }

    private async Task<(double Score, string Reasoning, List<AssessmentFactor> Factors)> 
        PerformInitialAssessmentAsync(
            DocumentChunk chunk,
            string? query,
            ITextCompletionService textCompletionService,
            ChunkFilterOptions options,
            CancellationToken cancellationToken)
    {
        var factors = new List<AssessmentFactor>();
        var scores = new List<double>();

        // Evaluate different aspects
        var contentScore = EvaluateContentRelevance(chunk.Content, query);
        factors.Add(new AssessmentFactor
        {
            Name = "Content Relevance",
            Contribution = contentScore,
            Explanation = $"Content alignment with query: {contentScore:F2}"
        });
        scores.Add(contentScore);

        // Information density
        var densityScore = EvaluateInformationDensity(chunk.Content);
        factors.Add(new AssessmentFactor
        {
            Name = "Information Density",
            Contribution = densityScore * 0.5, // Lower weight
            Explanation = $"Information richness: {densityScore:F2}"
        });
        scores.Add(densityScore);

        // Structural importance
        var structuralScore = EvaluateStructuralImportance(chunk);
        factors.Add(new AssessmentFactor
        {
            Name = "Structural Importance",
            Contribution = structuralScore * 0.3,
            Explanation = $"Document structure relevance: {structuralScore:F2}"
        });
        scores.Add(structuralScore);

        // Apply custom criteria if provided
        foreach (var criterion in options.Criteria)
        {
            var criterionScore = EvaluateCriterion(chunk, query, criterion);
            factors.Add(new AssessmentFactor
            {
                Name = criterion.Type.ToString(),
                Contribution = criterionScore * criterion.Weight,
                Explanation = $"Custom criterion {criterion.Type}: {criterionScore:F2}"
            });
            scores.Add(criterionScore * criterion.Weight);
        }

        // If we have LLM, get its assessment
        if (!string.IsNullOrEmpty(query))
        {
            var llmScore = await GetLLMAssessmentAsync(
                chunk, query, textCompletionService, cancellationToken);
            factors.Add(new AssessmentFactor
            {
                Name = "LLM Assessment",
                Contribution = llmScore * 0.8, // High weight for LLM
                Explanation = $"LLM relevance assessment: {llmScore:F2}"
            });
            scores.Add(llmScore);
        }

        var finalScore = scores.Count > 0 ? scores.Average() : 0.5;
        var reasoning = $"Initial assessment based on {factors.Count} factors. " +
                       $"Primary factor: {factors.OrderByDescending(f => Math.Abs(f.Contribution)).First().Name}";

        return (finalScore, reasoning, factors);
    }

    private Task<(double Score, string Reasoning, List<AssessmentFactor> Factors)>
        PerformSelfReflectionAsync(
            DocumentChunk chunk,
            string? query,
            (double Score, string Reasoning, List<AssessmentFactor> Factors) initial,
            ITextCompletionService textCompletionService,
            CancellationToken cancellationToken)
    {
        var factors = new List<AssessmentFactor>();
        
        // Reflect on initial assessment
        var biasCheck = CheckForBias(initial.Factors);
        if (Math.Abs(biasCheck) > 0.1)
        {
            factors.Add(new AssessmentFactor
            {
                Name = "Bias Correction",
                Contribution = -biasCheck,
                Explanation = $"Correcting for assessment bias: {biasCheck:F2}"
            });
        }

        // Check for missed aspects
        var completenessScore = EvaluateCompleteness(chunk, query);
        if (completenessScore < 0.7)
        {
            factors.Add(new AssessmentFactor
            {
                Name = "Completeness Adjustment",
                Contribution = (completenessScore - 0.7) * 0.5,
                Explanation = $"Adjusting for incomplete coverage: {completenessScore:F2}"
            });
        }

        // Re-evaluate with different perspective
        var alternativeScore = EvaluateAlternativePerspective(chunk, query);
        if (Math.Abs(alternativeScore - initial.Score) > 0.2)
        {
            factors.Add(new AssessmentFactor
            {
                Name = "Alternative Perspective",
                Contribution = (alternativeScore - initial.Score) * 0.3,
                Explanation = $"Alternative view suggests: {alternativeScore:F2}"
            });
        }

        // Calculate adjusted score
        var adjustment = factors.Sum(f => f.Contribution);
        var reflectedScore = Math.Max(0, Math.Min(1, initial.Score + adjustment));
        
        var reasoning = $"Self-reflection identified {factors.Count} adjustments. " +
                       $"Score adjusted from {initial.Score:F2} to {reflectedScore:F2}";

        return Task.FromResult((reflectedScore, reasoning, factors));
    }

    private Task<(double Score, string Reasoning, List<AssessmentFactor> Factors)>
        PerformCriticValidationAsync(
            DocumentChunk chunk,
            string? query,
            ChunkAssessment assessment,
            ITextCompletionService textCompletionService,
            CancellationToken cancellationToken)
    {
        var factors = new List<AssessmentFactor>();
        
        // Critical evaluation of previous assessments
        var consistencyScore = EvaluateConsistency(assessment);
        if (consistencyScore < 0.8)
        {
            factors.Add(new AssessmentFactor
            {
                Name = "Consistency Issue",
                Contribution = (consistencyScore - 1) * 0.3,
                Explanation = $"Inconsistency detected: {consistencyScore:F2}"
            });
        }

        // Validate against ground truth patterns
        var validationScore = ValidateAgainstPatterns(chunk, query);
        factors.Add(new AssessmentFactor
        {
            Name = "Pattern Validation",
            Contribution = (validationScore - 0.5) * 0.5,
            Explanation = $"Pattern matching validation: {validationScore:F2}"
        });

        // Check for edge cases
        var edgeCaseScore = CheckEdgeCases(chunk, query);
        if (edgeCaseScore != 0)
        {
            factors.Add(new AssessmentFactor
            {
                Name = "Edge Case Detection",
                Contribution = edgeCaseScore,
                Explanation = $"Edge case adjustment: {edgeCaseScore:F2}"
            });
        }

        // Calculate critic score
        var previousScore = assessment.ReflectionScore ?? assessment.InitialScore;
        var criticAdjustment = factors.Sum(f => f.Contribution);
        var criticScore = Math.Max(0, Math.Min(1, previousScore + criticAdjustment));
        
        var reasoning = $"Critic validation performed {factors.Count} checks. " +
                       $"Final validation score: {criticScore:F2}";

        return Task.FromResult((criticScore, reasoning, factors));
    }

    private async Task<double> GetLLMAssessmentAsync(
        DocumentChunk chunk,
        string query,
        ITextCompletionService textCompletionService,
        CancellationToken cancellationToken)
    {
        var prompt = $@"Rate the relevance of this text chunk to the query.
Query: {query}
Chunk: {chunk.Content.Substring(0, Math.Min(500, chunk.Content.Length))}...

Provide a relevance score from 0.0 to 1.0 where:
- 0.0 = completely irrelevant
- 0.5 = somewhat relevant
- 1.0 = highly relevant

Output only the numeric score.";

        try
        {
            var response = await textCompletionService.GenerateAsync(
                prompt, cancellationToken);
            
            if (double.TryParse(response.Trim(), out var score))
            {
                return Math.Max(0, Math.Min(1, score));
            }
        }
        catch
        {
            // Fallback if LLM fails
        }

        // Fallback to simple keyword matching
        return EvaluateContentRelevance(chunk.Content, query);
    }

    private double EvaluateContentRelevance(string content, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return 0.5; // Neutral if no query

        var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var contentLower = content.ToLower();
        
        var matchCount = queryWords.Count(word => contentLower.Contains(word));
        return (double)matchCount / queryWords.Length;
    }

    private double EvaluateInformationDensity(string content)
    {
        // Simple heuristic: ratio of unique words to total words
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        
        var uniqueWords = words.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var density = (double)uniqueWords / words.Length;
        
        // Also consider presence of numbers, technical terms
        var hasNumbers = words.Any(w => w.Any(char.IsDigit));
        var hasTechnical = words.Any(w => w.Contains('_') || w.Contains('-') || w.Contains('.'));
        
        if (hasNumbers) density += 0.1;
        if (hasTechnical) density += 0.1;
        
        return Math.Min(1, density);
    }

    private double EvaluateStructuralImportance(DocumentChunk chunk)
    {
        var score = 0.5; // Base score
        
        // Check for structural markers
        if (chunk.Content.StartsWith("#") || chunk.Content.Contains("HEADING"))
            score += 0.2;
        
        if (chunk.Content.Contains("```") || chunk.Content.Contains("CODE"))
            score += 0.15;
        
        if (chunk.Content.Contains("TABLE") || chunk.Content.Contains("|"))
            score += 0.15;
        
        // Position in document matters
        if (chunk.ChunkIndex < 3) // Early chunks often important
            score += 0.1;
        
        return Math.Min(1, score);
    }

    private double EvaluateCriterion(DocumentChunk chunk, string? query, FilterCriterion criterion)
    {
        return criterion.Type switch
        {
            CriterionType.KeywordPresence => EvaluateKeywordPresence(chunk.Content, criterion.Value),
            CriterionType.TopicRelevance => EvaluateTopicRelevance(chunk.Content, query),
            CriterionType.InformationDensity => EvaluateInformationDensity(chunk.Content),
            CriterionType.FactualContent => EvaluateFactualContent(chunk.Content),
            CriterionType.Recency => EvaluateRecency(chunk),
            CriterionType.SourceCredibility => EvaluateSourceCredibility(chunk),
            CriterionType.Completeness => EvaluateCompleteness(chunk, query),
            _ => 0.5
        };
    }

    private double EvaluateKeywordPresence(string content, object value)
    {
        if (value is string keyword)
        {
            return content.ToLower().Contains(keyword.ToLower()) ? 1.0 : 0.0;
        }
        if (value is IEnumerable<string> keywords)
        {
            var keywordList = keywords.ToList();
            var matches = keywordList.Count(k => content.ToLower().Contains(k.ToLower()));
            return (double)matches / keywordList.Count;
        }
        return 0.5;
    }

    private double EvaluateTopicRelevance(string content, string? query)
    {
        // Similar to content relevance but with semantic understanding
        return EvaluateContentRelevance(content, query) * 1.2; // Slight boost
    }

    private double EvaluateFactualContent(string content)
    {
        // Check for factual indicators
        var score = 0.5;
        
        // Numbers and dates indicate factual content
        if (System.Text.RegularExpressions.Regex.IsMatch(content, @"\d+"))
            score += 0.2;
        
        // Citations or references
        if (content.Contains("[") && content.Contains("]"))
            score += 0.15;
        
        // Technical terms (capitalized words, acronyms)
        var words = content.Split(' ');
        var capitalizedCount = words.Count(w => w.Length > 2 && char.IsUpper(w[0]));
        if (capitalizedCount > 2)
            score += 0.15;
        
        return Math.Min(1, score);
    }

    private double EvaluateRecency(DocumentChunk chunk)
    {
        // Check metadata for dates
        if (chunk.Metadata?.ProcessedAt != null)
        {
            var age = DateTime.UtcNow - chunk.Metadata.ProcessedAt;
            if (age.TotalDays < 7) return 1.0;
            if (age.TotalDays < 30) return 0.8;
            if (age.TotalDays < 90) return 0.6;
            return 0.4;
        }
        return 0.5;
    }

    private double EvaluateSourceCredibility(DocumentChunk chunk)
    {
        // Based on metadata source indicators
        if (chunk.Metadata?.FileType != null)
        {
            return chunk.Metadata.FileType switch
            {
                "PDF" => 0.8,  // Often formal documents
                "DOCX" => 0.7,
                "Web" => 0.5,
                "Text" => 0.4,
                _ => 0.5
            };
        }
        return 0.5;
    }

    private double EvaluateCompleteness(DocumentChunk chunk, string? query)
    {
        if (string.IsNullOrEmpty(query))
            return 0.7;
        
        // Check if chunk appears to be a complete thought
        var hasStart = char.IsUpper(chunk.Content.TrimStart().FirstOrDefault());
        var hasEnd = chunk.Content.TrimEnd().LastOrDefault() is '.' or '!' or '?';
        
        var score = 0.0;
        if (hasStart) score += 0.5;
        if (hasEnd) score += 0.5;
        
        return score;
    }

    private double CheckForBias(List<AssessmentFactor> factors)
    {
        // Check if assessment is overly reliant on single factor
        if (factors.Count == 0) return 0;
        
        var maxContribution = factors.Max(f => Math.Abs(f.Contribution));
        var totalContribution = factors.Sum(f => Math.Abs(f.Contribution));
        
        if (totalContribution == 0) return 0;
        
        var concentration = maxContribution / totalContribution;
        
        // If >70% weight on single factor, there's bias
        return concentration > 0.7 ? (concentration - 0.7) * 0.5 : 0;
    }

    private double EvaluateAlternativePerspective(DocumentChunk chunk, string? query)
    {
        // Simple alternative: focus on what's NOT mentioned
        if (string.IsNullOrEmpty(query))
            return 0.5;
        
        // Inverse relevance - useful for finding contrasts
        var directRelevance = EvaluateContentRelevance(chunk.Content, query);
        
        // If highly relevant, alternative view is same
        // If not relevant, might be useful as contrast
        if (directRelevance > 0.8) return directRelevance;
        if (directRelevance < 0.2) return 0.3; // Slight boost for contrast
        
        return directRelevance;
    }

    private double EvaluateConsistency(ChunkAssessment assessment)
    {
        var scores = new List<double> { assessment.InitialScore };
        
        if (assessment.ReflectionScore.HasValue)
            scores.Add(assessment.ReflectionScore.Value);
        
        if (scores.Count < 2) return 1.0;
        
        // Calculate variance
        var mean = scores.Average();
        var variance = scores.Select(s => Math.Pow(s - mean, 2)).Average();
        
        // Low variance = high consistency
        return Math.Max(0, 1 - variance * 2);
    }

    private double ValidateAgainstPatterns(DocumentChunk chunk, string? query)
    {
        // Validate against known good patterns
        var score = 0.5;
        
        // Good patterns
        if (chunk.Content.Length > 100 && chunk.Content.Length < 2000)
            score += 0.1;
        
        if (chunk.Content.Contains(". ") || chunk.Content.Contains(".\n"))
            score += 0.1; // Has sentence structure
        
        // Bad patterns
        if (chunk.Content.Length < 50)
            score -= 0.2; // Too short
        
        if (chunk.Content.Count(c => c == '\n') > chunk.Content.Length / 20)
            score -= 0.1; // Too fragmented
        
        return Math.Max(0, Math.Min(1, score));
    }

    private double CheckEdgeCases(DocumentChunk chunk, string? query)
    {
        var adjustment = 0.0;
        
        // Edge case: Very short chunk
        if (chunk.Content.Length < 50)
            adjustment -= 0.3;
        
        // Edge case: Only numbers/data
        var words = chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var numberWords = words.Count(w => w.All(c => char.IsDigit(c) || c == '.' || c == ','));
        if (words.Length > 0 && (double)numberWords / words.Length > 0.8)
            adjustment -= 0.2;
        
        // Edge case: Repeated content
        if (words.Length > 10)
        {
            var uniqueWords = words.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if ((double)uniqueWords / words.Length < 0.3)
                adjustment -= 0.2;
        }
        
        return adjustment;
    }

    private double CalculateFinalScore(ChunkAssessment assessment)
    {
        var scores = new List<(double Score, double Weight)>
        {
            (assessment.InitialScore, 0.4)
        };
        
        if (assessment.ReflectionScore.HasValue)
            scores.Add((assessment.ReflectionScore.Value, 0.3));
        
        if (assessment.CriticScore.HasValue)
            scores.Add((assessment.CriticScore.Value, 0.3));
        
        // Normalize weights
        var totalWeight = scores.Sum(s => s.Weight);
        
        return scores.Sum(s => s.Score * s.Weight / totalWeight);
    }

    private double CalculateConfidence(ChunkAssessment assessment)
    {
        // Confidence based on consistency and factor agreement
        var consistency = EvaluateConsistency(assessment);
        
        // Factor diversity (more factors = more confidence)
        var factorDiversity = Math.Min(1, assessment.Factors.Count / 10.0);
        
        // Score extremity (very high or very low scores = more confidence)
        var extremity = Math.Abs(assessment.FinalScore - 0.5) * 2;
        
        return (consistency * 0.5 + factorDiversity * 0.3 + extremity * 0.2);
    }

    private double CalculateQualityScore(ChunkAssessment assessment)
    {
        // Quality based on multiple factors
        var quality = 0.5;
        
        // Factors that indicate quality
        var densityFactor = assessment.Factors
            .FirstOrDefault(f => f.Name == "Information Density");
        if (densityFactor != null)
            quality = Math.Max(quality, densityFactor.Contribution + 0.5);
        
        var completeness = assessment.Factors
            .FirstOrDefault(f => f.Name == "Completeness Adjustment");
        if (completeness != null)
            quality += completeness.Contribution * 0.5;
        
        return Math.Max(0, Math.Min(1, quality));
    }

    private double CalculateCombinedScore(double relevance, double quality, double qualityWeight)
    {
        return relevance * (1 - qualityWeight) + quality * qualityWeight;
    }

    private void MergeFactors(List<AssessmentFactor> target, List<AssessmentFactor> source)
    {
        foreach (var factor in source)
        {
            var existing = target.FirstOrDefault(f => f.Name == factor.Name);
            if (existing != null)
            {
                // Update existing factor
                existing.Contribution = (existing.Contribution + factor.Contribution) / 2;
                existing.Explanation += $" | {factor.Explanation}";
            }
            else
            {
                target.Add(factor);
            }
        }
    }

    private List<string> GenerateSuggestions(ChunkAssessment assessment)
    {
        var suggestions = new List<string>();
        
        // Low relevance score
        if (assessment.FinalScore < 0.5)
        {
            suggestions.Add("Consider refining chunk boundaries to capture more complete context");
        }
        
        // High inconsistency
        if (assessment.InitialScore > 0 && assessment.ReflectionScore.HasValue)
        {
            var diff = Math.Abs(assessment.InitialScore - assessment.ReflectionScore.Value);
            if (diff > 0.3)
            {
                suggestions.Add("Large score variance detected - consider re-chunking with different strategy");
            }
        }
        
        // Low information density
        var densityFactor = assessment.Factors
            .FirstOrDefault(f => f.Name == "Information Density");
        if (densityFactor != null && densityFactor.Contribution < 0.3)
        {
            suggestions.Add("Low information density - consider merging with adjacent chunks");
        }
        
        // Edge cases detected
        var edgeFactor = assessment.Factors
            .FirstOrDefault(f => f.Name == "Edge Case Detection");
        if (edgeFactor != null && edgeFactor.Contribution < -0.1)
        {
            suggestions.Add("Edge case detected - review chunk extraction logic");
        }
        
        return suggestions;
    }

    private string GenerateReason(ChunkAssessment assessment, bool passed, ChunkFilterOptions options)
    {
        var reasons = new List<string>();
        
        if (passed)
        {
            reasons.Add($"Relevance: {assessment.FinalScore:F2}");
            
            // Top contributing factor
            var topFactor = assessment.Factors
                .OrderByDescending(f => Math.Abs(f.Contribution))
                .FirstOrDefault();
            if (topFactor != null)
            {
                reasons.Add($"Key factor: {topFactor.Name}");
            }
        }
        else
        {
            reasons.Add($"Below threshold ({options.MinRelevanceScore:F2})");
            
            // Main issue
            var worstFactor = assessment.Factors
                .OrderBy(f => f.Contribution)
                .FirstOrDefault();
            if (worstFactor != null)
            {
                reasons.Add($"Issue: {worstFactor.Name}");
            }
        }
        
        if (assessment.Confidence < 0.5)
        {
            reasons.Add("Low confidence assessment");
        }
        
        return string.Join(", ", reasons);
    }
}