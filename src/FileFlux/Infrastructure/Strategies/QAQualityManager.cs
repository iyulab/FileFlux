using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies
{
    /// <summary>
    /// Phase 13: T13-003 - Q&amp;A Quality Manager
    /// Manages quality assessment, filtering, and improvement of Q&amp;A pairs
    /// </summary>
    public class QAQualityManager
    {
        private readonly QuestionDifficultyAnalyzer _difficultyAnalyzer;
        private readonly AnswerCompletenessValidator _completenessValidator;
        private readonly DuplicateRemover _duplicateRemover;
        private readonly RelevanceFilter _relevanceFilter;
        private readonly QualityScorer _qualityScorer;

        public QAQualityManager()
        {
            _difficultyAnalyzer = new QuestionDifficultyAnalyzer();
            _completenessValidator = new AnswerCompletenessValidator();
            _duplicateRemover = new DuplicateRemover();
            _relevanceFilter = new RelevanceFilter();
            _qualityScorer = new QualityScorer();
        }

        /// <summary>
        /// Evaluate and manage quality of Q&amp;A pairs
        /// </summary>
        public QAQualityResult EvaluateQAQuality(
            List<QAPair> qaPairs,
            QAQualityOptions? options = null)
        {
            options ??= new QAQualityOptions();

            var result = new QAQualityResult
            {
                OriginalCount = qaPairs.Count,
                EvaluatedAt = DateTime.UtcNow
            };

            // 1. Analyze question difficulty
            foreach (var pair in qaPairs)
            {
                pair.Difficulty = _difficultyAnalyzer.AnalyzeDifficulty(pair.Question);
            }

            // 2. Validate answer completeness
            foreach (var pair in qaPairs)
            {
                if (pair.Answer != null)
                {
                    pair.Completeness = _completenessValidator.ValidateCompleteness(
                        pair.Question,
                        pair.Answer
                    );
                }
            }

            // 3. Score overall quality
            foreach (var pair in qaPairs)
            {
                pair.Quality = _qualityScorer.ScoreQAPair(pair);
            }

            // 4. Filter by quality threshold
            var filteredPairs = qaPairs.Where(p => p.Quality >= options.MinQualityThreshold).ToList();

            // 5. Remove duplicates
            filteredPairs = _duplicateRemover.RemoveDuplicates(filteredPairs, options);

            // 6. Filter by relevance
            if (options.ApplyRelevanceFilter)
            {
                filteredPairs = _relevanceFilter.FilterByRelevance(filteredPairs, options);
            }

            // 7. Balance difficulty distribution
            if (options.BalanceDifficulty)
            {
                filteredPairs = BalanceDifficultyDistribution(filteredPairs, options);
            }

            result.FilteredPairs = filteredPairs;
            result.FilteredCount = filteredPairs.Count;
            result.QualityMetrics = CalculateQualityMetrics(filteredPairs);
            result.DifficultyDistribution = CalculateDifficultyDistribution(filteredPairs);

            return result;
        }

        /// <summary>
        /// Improve quality of existing Q&amp;A pairs
        /// </summary>
        public QAImprovementResult ImproveQAQuality(
            List<QAPair> qaPairs,
            QAImprovementOptions? options = null)
        {
            options ??= new QAImprovementOptions();

            var result = new QAImprovementResult
            {
                OriginalPairs = new List<QAPair>(qaPairs),
                ImprovedAt = DateTime.UtcNow
            };

            var improvedPairs = new List<QAPair>();

            foreach (var pair in qaPairs)
            {
                var improvedPair = new QAPair
                {
                    Question = pair.Question,
                    Answer = pair.Answer ?? new ExtractedAnswer { Text = string.Empty },
                    ChunkId = pair.ChunkId ?? string.Empty,
                    Quality = pair.Quality
                };

                // Improve question clarity
                if (options.ImproveQuestionClarity)
                {
                    improvedPair.Question = ImproveQuestionClarity(pair.Question);
                }

                // Enhance answer completeness
                if (options.EnhanceAnswerCompleteness && pair.Answer != null)
                {
                    improvedPair.Answer = EnhanceAnswerCompleteness(pair.Answer, pair.Question);
                }

                // Add context if missing
                if (options.AddMissingContext && pair.Answer != null)
                {
                    AddContextToAnswer(improvedPair.Answer, pair.ChunkId);
                }

                // Recalculate quality score
                improvedPair.Quality = _qualityScorer.ScoreQAPair(improvedPair);

                if (improvedPair.Quality > pair.Quality)
                {
                    result.ImprovementCount++;
                }

                improvedPairs.Add(improvedPair);
            }

            result.ImprovedPairs = improvedPairs;
            result.AverageImprovement = CalculateAverageImprovement(qaPairs, improvedPairs);

            return result;
        }

        /// <summary>
        /// Validate Q&amp;A pairs against quality criteria
        /// </summary>
        public QAValidationResult ValidateQAPairs(
            List<QAPair> qaPairs,
            ValidationCriteria? criteria = null)
        {
            criteria ??= new ValidationCriteria();

            var result = new QAValidationResult
            {
                TotalPairs = qaPairs.Count,
                ValidationTime = DateTime.UtcNow
            };

            foreach (var pair in qaPairs)
            {
                var pairValidation = new QAPairValidation
                {
                    QAPair = pair,
                    IsValid = true
                };

                // Check question validity
                var questionIssues = ValidateQuestion(pair.Question, criteria);
                if (questionIssues.Any())
                {
                    pairValidation.IsValid = false;
                    pairValidation.Issues.AddRange(questionIssues);
                }

                // Check answer validity
                if (pair.Answer != null)
                {
                    var answerIssues = ValidateAnswer(pair.Answer, pair.Question, criteria);
                    if (answerIssues.Any())
                    {
                        pairValidation.IsValid = false;
                        pairValidation.Issues.AddRange(answerIssues);
                    }
                }
                else if (criteria.RequireAnswer)
                {
                    pairValidation.IsValid = false;
                    pairValidation.Issues.Add("Missing answer");
                }

                result.Validations.Add(pairValidation);
            }

            result.ValidCount = result.Validations.Count(v => v.IsValid);
            result.InvalidCount = result.Validations.Count(v => !v.IsValid);
            result.ValidationRate = result.ValidCount / (double)result.TotalPairs;

            return result;
        }

        private List<QAPair> BalanceDifficultyDistribution(
            List<QAPair> pairs,
            QAQualityOptions options)
        {
            var balanced = new List<QAPair>();

            // Group by difficulty
            var byDifficulty = pairs.GroupBy(p => GetDifficultyLevel(p.Difficulty)).ToList();

            // Calculate target count per difficulty level
            var targetPerLevel = options.MaxQAPairs / 4; // Assuming 4 difficulty levels

            foreach (var group in byDifficulty)
            {
                var selected = group.OrderByDescending(p => p.Quality)
                                   .Take(targetPerLevel)
                                   .ToList();
                balanced.AddRange(selected);
            }

            return balanced.OrderByDescending(p => p.Quality)
                          .Take(options.MaxQAPairs)
                          .ToList();
        }

        private DifficultyLevel GetDifficultyLevel(double difficulty)
        {
            return difficulty switch
            {
                < 0.25 => DifficultyLevel.Easy,
                < 0.5 => DifficultyLevel.Medium,
                < 0.75 => DifficultyLevel.Hard,
                _ => DifficultyLevel.Expert
            };
        }

        private GeneratedQuestion ImproveQuestionClarity(GeneratedQuestion question)
        {
            var improved = new GeneratedQuestion
            {
                Type = question.Type,
                Keywords = question.Keywords,
                Quality = question.Quality,
                Complexity = question.Complexity,
                RequiredHops = question.RequiredHops,
                ExpectedAnswerLocation = question.ExpectedAnswerLocation
            };

            // Improve question text
            improved.QuestionText = ImproveQuestionText(question.QuestionText);

            return improved;
        }

        private string ImproveQuestionText(string questionText)
        {
            var improved = questionText;

            // Remove redundant words
            improved = Regex.Replace(improved, @"\b(very|really|actually|basically)\b", "",
                                    RegexOptions.IgnoreCase);

            // Fix common grammar issues
            improved = Regex.Replace(improved, @"\s+", " "); // Multiple spaces to single
            improved = improved.Trim();

            // Ensure proper question ending
            if (!improved.EndsWith("?", StringComparison.Ordinal))
            {
                improved += "?";
            }

            // Capitalize first letter
            if (improved.Length > 0)
            {
                improved = char.ToUpper(improved[0]) + improved.Substring(1);
            }

            return improved;
        }

        private ExtractedAnswer EnhanceAnswerCompleteness(
            ExtractedAnswer answer,
            GeneratedQuestion question)
        {
            var enhanced = new ExtractedAnswer
            {
                Text = answer.Text,
                StartPosition = answer.StartPosition,
                EndPosition = answer.EndPosition,
                Type = answer.Type,
                Confidence = answer.Confidence,
                IsVerified = answer.IsVerified,
                VerificationScore = answer.VerificationScore,
                Context = answer.Context,
                SupportingEvidence = new List<string>(answer.SupportingEvidence),
                ExtractionMethod = answer.ExtractionMethod
            };

            // Add missing supporting evidence
            if (!enhanced.SupportingEvidence.Any() && !string.IsNullOrEmpty(enhanced.Context))
            {
                enhanced.SupportingEvidence.Add(enhanced.Context);
            }

            // Enhance answer text if too brief
            if (enhanced.Text.Split(' ').Length < 5 && question.Type == QuestionType.Conceptual)
            {
                if (!string.IsNullOrEmpty(enhanced.Context))
                {
                    enhanced.Text = ExtractMoreCompleteAnswer(enhanced.Text, enhanced.Context);
                }
            }

            return enhanced;
        }

        private string ExtractMoreCompleteAnswer(string originalAnswer, string context)
        {
            // Find the original answer in context and expand it
            var index = context.IndexOf(originalAnswer, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // Find sentence boundaries
                var sentenceStart = context.LastIndexOf('.', Math.Max(0, index - 1));
                sentenceStart = sentenceStart < 0 ? 0 : sentenceStart + 1;

                var sentenceEnd = context.IndexOf('.', index + originalAnswer.Length);
                sentenceEnd = sentenceEnd < 0 ? context.Length : sentenceEnd + 1;

                return context.Substring(sentenceStart, sentenceEnd - sentenceStart).Trim();
            }

            return originalAnswer;
        }

        private void AddContextToAnswer(ExtractedAnswer answer, string chunkId)
        {
            if (string.IsNullOrEmpty(answer.Context))
            {
                // Add chunk ID as minimal context
                answer.Context = $"Source: {chunkId}";
            }
        }

        private List<string> ValidateQuestion(GeneratedQuestion question, ValidationCriteria criteria)
        {
            var issues = new List<string>();

            // Check question length
            var wordCount = question.QuestionText.Split(' ').Length;
            if (wordCount < criteria.MinQuestionWords)
            {
                issues.Add($"Question too short ({wordCount} words)");
            }
            if (wordCount > criteria.MaxQuestionWords)
            {
                issues.Add($"Question too long ({wordCount} words)");
            }

            // Check for question mark
            if (!question.QuestionText.EndsWith("?", StringComparison.Ordinal))
            {
                issues.Add("Question doesn't end with question mark");
            }

            // Check for empty keywords
            if (!question.Keywords.Any())
            {
                issues.Add("No keywords identified");
            }

            // Check quality score
            if (question.Quality < criteria.MinQuestionQuality)
            {
                issues.Add($"Question quality too low ({question.Quality:F2})");
            }

            return issues;
        }

        private List<string> ValidateAnswer(
            ExtractedAnswer answer,
            GeneratedQuestion question,
            ValidationCriteria criteria)
        {
            var issues = new List<string>();

            // Check answer length
            var wordCount = answer.Text.Split(' ').Length;
            if (wordCount < criteria.MinAnswerWords)
            {
                issues.Add($"Answer too short ({wordCount} words)");
            }
            if (wordCount > criteria.MaxAnswerWords)
            {
                issues.Add($"Answer too long ({wordCount} words)");
            }

            // Check confidence
            if (answer.Confidence < criteria.MinAnswerConfidence)
            {
                issues.Add($"Answer confidence too low ({answer.Confidence:F2})");
            }

            // Check verification
            if (criteria.RequireVerification && !answer.IsVerified)
            {
                issues.Add("Answer not verified");
            }

            // Check answer relevance to question
            if (!IsAnswerRelevant(answer, question))
            {
                issues.Add("Answer may not be relevant to question");
            }

            return issues;
        }

        private bool IsAnswerRelevant(ExtractedAnswer answer, GeneratedQuestion question)
        {
            // Simple relevance check based on keyword overlap
            var answerLower = answer.Text.ToLower();
            var relevantKeywords = question.Keywords.Count(k => answerLower.Contains(k.ToLower()));

            return relevantKeywords > 0 || question.Keywords.Count == 0;
        }

        private QAQualityMetrics CalculateQualityMetrics(List<QAPair> pairs)
        {
            if (!pairs.Any())
            {
                return new QAQualityMetrics();
            }

            return new QAQualityMetrics
            {
                AverageQuality = pairs.Average(p => p.Quality),
                MinQuality = pairs.Min(p => p.Quality),
                MaxQuality = pairs.Max(p => p.Quality),
                StandardDeviation = CalculateStandardDeviation(pairs.Select(p => p.Quality).ToList()),
                CompletePairs = pairs.Count(p => p.Answer != null),
                VerifiedPairs = pairs.Count(p => p.Answer?.IsVerified == true)
            };
        }

        private double CalculateStandardDeviation(List<double> values)
        {
            if (values.Count < 2) return 0.0;

            var average = values.Average();
            var sumOfSquares = values.Sum(v => Math.Pow(v - average, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }

        private Dictionary<DifficultyLevel, int> CalculateDifficultyDistribution(List<QAPair> pairs)
        {
            return pairs.GroupBy(p => GetDifficultyLevel(p.Difficulty))
                       .ToDictionary(g => g.Key, g => g.Count());
        }

        private double CalculateAverageImprovement(List<QAPair> original, List<QAPair> improved)
        {
            if (!original.Any()) return 0.0;

            var improvements = new List<double>();
            for (int i = 0; i < Math.Min(original.Count, improved.Count); i++)
            {
                var improvement = improved[i].Quality - original[i].Quality;
                improvements.Add(improvement);
            }

            return improvements.Any() ? improvements.Average() : 0.0;
        }
    }

    // Supporting classes
    public class QuestionDifficultyAnalyzer
    {
        public double AnalyzeDifficulty(GeneratedQuestion question)
        {
            double difficulty = 0.0;

            // Base difficulty from question type
            difficulty += question.Type switch
            {
                QuestionType.Factual => 0.2,
                QuestionType.Conceptual => 0.4,
                QuestionType.Inferential => 0.6,
                QuestionType.MultiHop => 0.8,
                _ => 0.3
            };

            // Complexity factor
            difficulty += question.Complexity * 0.2;

            // Word count factor
            var wordCount = question.QuestionText.Split(' ').Length;
            difficulty += Math.Min(0.2, wordCount / 50.0);

            // Multi-hop factor
            if (question.RequiredHops > 1)
            {
                difficulty += 0.1 * (question.RequiredHops - 1);
            }

            return Math.Min(1.0, difficulty);
        }
    }

    public class AnswerCompletenessValidator
    {
        public double ValidateCompleteness(GeneratedQuestion question, ExtractedAnswer answer)
        {
            double completeness = 0.0;

            // Check if answer addresses question type appropriately
            if (IsAppropriateForQuestionType(answer, question.Type))
            {
                completeness += 0.3;
            }

            // Check keyword coverage
            var keywordCoverage = CalculateKeywordCoverage(answer.Text, question.Keywords);
            completeness += keywordCoverage * 0.3;

            // Check supporting evidence
            if (answer.SupportingEvidence.Any())
            {
                completeness += Math.Min(0.2, answer.SupportingEvidence.Count * 0.1);
            }

            // Check answer length appropriateness
            var lengthScore = CalculateLengthAppropriateness(answer.Text, question.Type);
            completeness += lengthScore * 0.2;

            return Math.Min(1.0, completeness);
        }

        private bool IsAppropriateForQuestionType(ExtractedAnswer answer, QuestionType questionType)
        {
            return questionType switch
            {
                QuestionType.Factual => answer.Type == AnswerType.Entity ||
                                        answer.Type == AnswerType.Numerical ||
                                        answer.Type == AnswerType.Date,
                QuestionType.Conceptual => answer.Type == AnswerType.Explanation,
                QuestionType.Inferential => answer.Type == AnswerType.Explanation,
                QuestionType.MultiHop => answer.Type == AnswerType.Explanation,
                _ => true
            };
        }

        private double CalculateKeywordCoverage(string text, List<string> keywords)
        {
            if (!keywords.Any()) return 1.0;

            var textLower = text.ToLower();
            var coveredKeywords = keywords.Count(k => textLower.Contains(k.ToLower()));

            return (double)coveredKeywords / keywords.Count;
        }

        private double CalculateLengthAppropriateness(string text, QuestionType questionType)
        {
            var wordCount = text.Split(' ').Length;

            return questionType switch
            {
                QuestionType.Factual => wordCount <= 20 ? 1.0 : 0.7,
                QuestionType.Conceptual => wordCount >= 15 && wordCount <= 100 ? 1.0 : 0.6,
                QuestionType.Inferential => wordCount >= 20 && wordCount <= 150 ? 1.0 : 0.5,
                QuestionType.MultiHop => wordCount >= 30 ? 1.0 : wordCount / 30.0,
                _ => 0.5
            };
        }
    }

    public class DuplicateRemover
    {
        public List<QAPair> RemoveDuplicates(List<QAPair> pairs, QAQualityOptions options)
        {
            var unique = new List<QAPair>();
            var seenQuestions = new HashSet<string>();
            var seenAnswers = new HashSet<string>();

            foreach (var pair in pairs.OrderByDescending(p => p.Quality))
            {
                var questionNormalized = NormalizeText(pair.Question.QuestionText);
                var answerNormalized = pair.Answer != null ? NormalizeText(pair.Answer.Text) : "";

                // Check for duplicate questions
                if (seenQuestions.Contains(questionNormalized))
                    continue;

                // Check for duplicate answers (if strict mode)
                if (options.StrictDuplicateRemoval &&
                    !string.IsNullOrEmpty(answerNormalized) &&
                    seenAnswers.Contains(answerNormalized))
                    continue;

                // Check for similar questions
                if (options.RemoveSimilar && IsSimilarToExisting(questionNormalized, seenQuestions))
                    continue;

                unique.Add(pair);
                seenQuestions.Add(questionNormalized);
                if (!string.IsNullOrEmpty(answerNormalized))
                    seenAnswers.Add(answerNormalized);
            }

            return unique;
        }

        private string NormalizeText(string text)
        {
            return Regex.Replace(text.ToLower(), @"[^\w\s]", "").Trim();
        }

        private bool IsSimilarToExisting(string normalized, HashSet<string> existing)
        {
            // Simple similarity check - can be enhanced with better algorithms
            foreach (var text in existing)
            {
                if (CalculateSimilarity(normalized, text) > 0.8)
                    return true;
            }
            return false;
        }

        private double CalculateSimilarity(string text1, string text2)
        {
            var words1 = new HashSet<string>(text1.Split(' '));
            var words2 = new HashSet<string>(text2.Split(' '));

            if (!words1.Any() || !words2.Any()) return 0.0;

            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();

            return (double)intersection / union;
        }
    }

    public class RelevanceFilter
    {
        public List<QAPair> FilterByRelevance(List<QAPair> pairs, QAQualityOptions options)
        {
            return pairs.Where(p => IsRelevant(p, options)).ToList();
        }

        private bool IsRelevant(QAPair pair, QAQualityOptions options)
        {
            // Filter out trivial questions
            if (IsTrivial(pair.Question))
                return false;

            // Filter out incomplete answers
            if (pair.Answer != null && IsIncomplete(pair.Answer))
                return false;

            // Apply domain-specific relevance if specified
            if (!string.IsNullOrEmpty(options.DomainFilter))
            {
                return IsDomainRelevant(pair, options.DomainFilter);
            }

            return true;
        }

        private bool IsTrivial(GeneratedQuestion question)
        {
            var trivialPatterns = new[]
            {
                @"^what is this",
                @"^who is this",
                @"^where is this",
                @"^when is this"
            };

            var questionLower = question.QuestionText.ToLower();
            return trivialPatterns.Any(pattern => Regex.IsMatch(questionLower, pattern));
        }

        private bool IsIncomplete(ExtractedAnswer answer)
        {
            return answer.Text.Length < 10 ||
                   answer.Text.EndsWith("...", StringComparison.Ordinal) ||
                   answer.Text.Contains("[incomplete]");
        }

        private bool IsDomainRelevant(QAPair pair, string domain)
        {
            // Simple domain relevance check
            var domainKeywords = GetDomainKeywords(domain);
            var text = pair.Question.QuestionText + " " + (pair.Answer?.Text ?? "");

            return domainKeywords.Any(keyword =>
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> GetDomainKeywords(string domain)
        {
            return domain.ToLower() switch
            {
                "technical" => new List<string> { "code", "algorithm", "function", "system", "implementation" },
                "business" => new List<string> { "revenue", "customer", "market", "strategy", "growth" },
                "medical" => new List<string> { "patient", "treatment", "diagnosis", "symptom", "medication" },
                "legal" => new List<string> { "law", "regulation", "compliance", "contract", "liability" },
                _ => new List<string>()
            };
        }
    }

    public class QualityScorer
    {
        public double ScoreQAPair(QAPair pair)
        {
            double score = 0.0;

            // Question quality contribution
            score += pair.Question.Quality * 0.3;

            // Answer quality contribution
            if (pair.Answer != null)
            {
                score += pair.Answer.Confidence * 0.3;
                score += pair.Answer.VerificationScore * 0.2;

                // Completeness contribution
                if (pair.Completeness > 0)
                {
                    score += pair.Completeness * 0.2;
                }
            }
            else
            {
                // Penalize missing answer
                score *= 0.5;
            }

            return Math.Min(1.0, score);
        }
    }

    // Data structures
    public class QAQualityOptions
    {
        public double MinQualityThreshold { get; set; } = 0.6;
        public int MaxQAPairs { get; set; } = 100;
        public bool ApplyRelevanceFilter { get; set; } = true;
        public bool RemoveSimilar { get; set; } = true;
        public bool StrictDuplicateRemoval { get; set; } = false;
        public bool BalanceDifficulty { get; set; } = true;
        public string DomainFilter { get; set; }
    }

    public class QAImprovementOptions
    {
        public bool ImproveQuestionClarity { get; set; } = true;
        public bool EnhanceAnswerCompleteness { get; set; } = true;
        public bool AddMissingContext { get; set; } = true;
    }

    public class ValidationCriteria
    {
        public int MinQuestionWords { get; set; } = 3;
        public int MaxQuestionWords { get; set; } = 50;
        public int MinAnswerWords { get; set; } = 1;
        public int MaxAnswerWords { get; set; } = 200;
        public double MinQuestionQuality { get; set; } = 0.5;
        public double MinAnswerConfidence { get; set; } = 0.5;
        public bool RequireAnswer { get; set; } = false;
        public bool RequireVerification { get; set; } = false;
    }

    public class QAQualityResult
    {
        public int OriginalCount { get; set; }
        public int FilteredCount { get; set; }
        public List<QAPair> FilteredPairs { get; set; } = new List<QAPair>();
        public QAQualityMetrics QualityMetrics { get; set; }
        public Dictionary<DifficultyLevel, int> DifficultyDistribution { get; set; }
        public DateTime EvaluatedAt { get; set; }
    }

    public class QAImprovementResult
    {
        public List<QAPair> OriginalPairs { get; set; } = new List<QAPair>();
        public List<QAPair> ImprovedPairs { get; set; } = new List<QAPair>();
        public int ImprovementCount { get; set; }
        public double AverageImprovement { get; set; }
        public DateTime ImprovedAt { get; set; }
    }

    public class QAValidationResult
    {
        public int TotalPairs { get; set; }
        public int ValidCount { get; set; }
        public int InvalidCount { get; set; }
        public double ValidationRate { get; set; }
        public List<QAPairValidation> Validations { get; set; } = new List<QAPairValidation>();
        public DateTime ValidationTime { get; set; }
    }

    public class QAPairValidation
    {
        public QAPair QAPair { get; set; }
        public bool IsValid { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    public enum DifficultyLevel
    {
        Easy,
        Medium,
        Hard,
        Expert
    }

    // Extension to QAPair for quality management
    public partial class QAPair
    {
        public double Difficulty { get; set; }
        public double Completeness { get; set; }
        public double QualityScore { get; set; }
    }
}
