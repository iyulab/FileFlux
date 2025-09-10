using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies
{
    /// <summary>
    /// Phase 13: T13-002 - Answer Extraction System
    /// Extracts accurate answers from document chunks for generated questions
    /// </summary>
    public class AnswerExtractionSystem
    {
        private readonly AnswerSpanIdentifier _spanIdentifier;
        private readonly AnswerConfidenceEvaluator _confidenceEvaluator;
        private readonly MultiSourceAnswerIntegrator _multiSourceIntegrator;
        private readonly AnswerVerificationEngine _verificationEngine;

        public AnswerExtractionSystem()
        {
            _spanIdentifier = new AnswerSpanIdentifier();
            _confidenceEvaluator = new AnswerConfidenceEvaluator();
            _multiSourceIntegrator = new MultiSourceAnswerIntegrator();
            _verificationEngine = new AnswerVerificationEngine();
        }

        /// <summary>
        /// Extract answer for a specific question from a chunk
        /// </summary>
        public AnswerExtractionResult ExtractAnswer(
            GeneratedQuestion question, 
            DocumentChunk chunk,
            AnswerExtractionOptions options = null)
        {
            options ??= new AnswerExtractionOptions();
            
            var result = new AnswerExtractionResult
            {
                QuestionId = question.QuestionText.GetHashCode().ToString(),
                Question = question,
                ChunkId = chunk.Id,
                ExtractedAt = DateTime.UtcNow
            };

            try
            {
                // 1. Identify potential answer spans
                var answerSpans = _spanIdentifier.IdentifyAnswerSpans(
                    question, 
                    chunk.Content, 
                    options
                );

                if (!answerSpans.Any())
                {
                    result.Success = false;
                    result.NoAnswerReason = "No relevant answer spans found in chunk";
                    return result;
                }

                // 2. Rank and select best answer span
                var bestSpan = SelectBestAnswerSpan(answerSpans, question, chunk);

                // 3. Extract and refine answer
                var extractedAnswer = ExtractAnswerFromSpan(bestSpan, chunk.Content, question);

                // 4. Evaluate confidence
                extractedAnswer.Confidence = _confidenceEvaluator.EvaluateConfidence(
                    extractedAnswer, 
                    question, 
                    chunk
                );

                // 5. Verify answer
                var verificationResult = _verificationEngine.VerifyAnswer(
                    extractedAnswer, 
                    question, 
                    chunk
                );

                extractedAnswer.IsVerified = verificationResult.IsValid;
                extractedAnswer.VerificationScore = verificationResult.Score;

                result.Answer = extractedAnswer;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Extract answers from multiple chunks
        /// </summary>
        public BatchAnswerExtractionResult ExtractAnswersFromMultipleSources(
            GeneratedQuestion question,
            List<DocumentChunk> chunks,
            AnswerExtractionOptions options = null)
        {
            var batchResult = new BatchAnswerExtractionResult
            {
                Question = question,
                ExtractedAt = DateTime.UtcNow
            };

            // Extract answers from each chunk
            foreach (var chunk in chunks)
            {
                var result = ExtractAnswer(question, chunk, options);
                if (result.Success && result.Answer != null)
                {
                    batchResult.AnswersFromChunks.Add(result.Answer);
                }
            }

            // Integrate multiple answers if found
            if (batchResult.AnswersFromChunks.Count > 1)
            {
                batchResult.IntegratedAnswer = _multiSourceIntegrator.IntegrateAnswers(
                    batchResult.AnswersFromChunks,
                    question
                );
            }
            else if (batchResult.AnswersFromChunks.Count == 1)
            {
                batchResult.IntegratedAnswer = batchResult.AnswersFromChunks.First();
            }

            batchResult.Success = batchResult.IntegratedAnswer != null;
            return batchResult;
        }

        /// <summary>
        /// Extract answers for all questions in a Q&A generation result
        /// </summary>
        public QAExtractionResult ExtractAnswersForQuestions(
            QuestionGenerationResult questionResult,
            DocumentChunk chunk,
            AnswerExtractionOptions options = null)
        {
            var qaResult = new QAExtractionResult
            {
                ChunkId = chunk.Id,
                GeneratedAt = DateTime.UtcNow
            };

            foreach (var question in questionResult.Questions)
            {
                var answerResult = ExtractAnswer(question, chunk, options);
                
                if (answerResult.Success && answerResult.Answer != null)
                {
                    qaResult.QAPairs.Add(new QAPair
                    {
                        Question = question,
                        Answer = answerResult.Answer,
                        ChunkId = chunk.Id,
                        Quality = CalculateQAPairQuality(question, answerResult.Answer)
                    });
                }
            }

            // Calculate overall quality metrics
            qaResult.QualityMetrics = CalculateQAQualityMetrics(qaResult.QAPairs);
            qaResult.Success = qaResult.QAPairs.Any();

            return qaResult;
        }

        private AnswerSpan SelectBestAnswerSpan(
            List<AnswerSpan> spans, 
            GeneratedQuestion question,
            DocumentChunk chunk)
        {
            // Score each span
            foreach (var span in spans)
            {
                span.Score = CalculateSpanScore(span, question, chunk);
            }

            // Select highest scoring span
            return spans.OrderByDescending(s => s.Score).First();
        }

        private double CalculateSpanScore(
            AnswerSpan span, 
            GeneratedQuestion question,
            DocumentChunk chunk)
        {
            double score = 0.0;

            // Keyword overlap score
            var keywordOverlap = CalculateKeywordOverlap(span.Text, question.Keywords);
            score += keywordOverlap * 0.3;

            // Position score (prefer earlier mentions)
            var positionScore = 1.0 - (span.StartPosition / (double)chunk.Content.Length);
            score += positionScore * 0.2;

            // Length appropriateness score
            var lengthScore = CalculateLengthScore(span.Text, question.Type);
            score += lengthScore * 0.2;

            // Semantic relevance score
            var semanticScore = CalculateSemanticRelevance(span.Text, question.QuestionText);
            score += semanticScore * 0.3;

            return score;
        }

        private double CalculateKeywordOverlap(string text, List<string> keywords)
        {
            if (!keywords.Any()) return 0.0;

            var textLower = text.ToLower();
            var matchCount = keywords.Count(k => textLower.Contains(k.ToLower()));
            
            return (double)matchCount / keywords.Count;
        }

        private double CalculateLengthScore(string text, QuestionType questionType)
        {
            var wordCount = text.Split(' ').Length;

            return questionType switch
            {
                QuestionType.Factual => wordCount <= 20 ? 1.0 : Math.Max(0, 1.0 - (wordCount - 20) / 100.0),
                QuestionType.Conceptual => wordCount >= 10 && wordCount <= 50 ? 1.0 : 0.7,
                QuestionType.Inferential => wordCount >= 20 && wordCount <= 100 ? 1.0 : 0.6,
                QuestionType.MultiHop => wordCount >= 30 ? 1.0 : wordCount / 30.0,
                _ => 0.5
            };
        }

        private double CalculateSemanticRelevance(string answerText, string questionText)
        {
            // Simplified semantic similarity calculation
            var questionWords = new HashSet<string>(
                questionText.ToLower().Split(' ').Where(w => w.Length > 3)
            );
            var answerWords = new HashSet<string>(
                answerText.ToLower().Split(' ').Where(w => w.Length > 3)
            );

            if (!questionWords.Any() || !answerWords.Any()) return 0.0;

            var intersection = questionWords.Intersect(answerWords).Count();
            var union = questionWords.Union(answerWords).Count();

            return union > 0 ? (double)intersection / union : 0.0;
        }

        private ExtractedAnswer ExtractAnswerFromSpan(
            AnswerSpan span,
            string chunkContent,
            GeneratedQuestion question)
        {
            var answer = new ExtractedAnswer
            {
                Text = span.Text,
                StartPosition = span.StartPosition,
                EndPosition = span.EndPosition,
                Type = DetermineAnswerType(span.Text, question),
                ExtractionMethod = span.ExtractionMethod
            };

            // Extract context around answer
            answer.Context = ExtractContext(chunkContent, span.StartPosition, span.EndPosition);

            // Extract supporting evidence
            answer.SupportingEvidence = ExtractSupportingEvidence(chunkContent, span);

            return answer;
        }

        private AnswerType DetermineAnswerType(string answerText, GeneratedQuestion question)
        {
            // Determine answer type based on content and question type
            if (Regex.IsMatch(answerText, @"^\d+$"))
                return AnswerType.Numerical;
            
            if (Regex.IsMatch(answerText, @"\d{4}|\d{1,2}[/-]\d{1,2}"))
                return AnswerType.Date;
            
            if (answerText.Split(' ').Length <= 3)
                return AnswerType.Entity;
            
            if (answerText.Split(' ').Length > 20)
                return AnswerType.Explanation;
            
            return AnswerType.Phrase;
        }

        private string ExtractContext(string content, int startPos, int endPos)
        {
            // Extract 50 characters before and after the answer
            var contextStart = Math.Max(0, startPos - 50);
            var contextEnd = Math.Min(content.Length, endPos + 50);
            
            return content.Substring(contextStart, contextEnd - contextStart);
        }

        private List<string> ExtractSupportingEvidence(string content, AnswerSpan span)
        {
            var evidence = new List<string>();
            
            // Find sentences containing the answer
            var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var sentence in sentences)
            {
                if (sentence.Contains(span.Text))
                {
                    evidence.Add(sentence.Trim());
                }
            }

            return evidence;
        }

        private double CalculateQAPairQuality(GeneratedQuestion question, ExtractedAnswer answer)
        {
            double quality = 0.0;

            // Answer confidence contributes to quality
            quality += answer.Confidence * 0.4;

            // Verification score contributes to quality
            quality += answer.VerificationScore * 0.3;

            // Answer completeness
            var completeness = CalculateAnswerCompleteness(question, answer);
            quality += completeness * 0.3;

            return Math.Min(1.0, quality);
        }

        private double CalculateAnswerCompleteness(GeneratedQuestion question, ExtractedAnswer answer)
        {
            // Check if answer addresses all aspects of the question
            double completeness = 0.5; // Base score

            // Check keyword coverage
            var keywordCoverage = CalculateKeywordOverlap(answer.Text, question.Keywords);
            completeness += keywordCoverage * 0.3;

            // Check answer length appropriateness
            var lengthScore = CalculateLengthScore(answer.Text, question.Type);
            completeness += lengthScore * 0.2;

            return Math.Min(1.0, completeness);
        }

        private QAQualityMetrics CalculateQAQualityMetrics(List<QAPair> qaPairs)
        {
            if (!qaPairs.Any())
            {
                return new QAQualityMetrics();
            }

            return new QAQualityMetrics
            {
                AverageQuality = qaPairs.Average(p => p.Quality),
                AnswerCoverage = qaPairs.Count(p => p.Answer != null) / (double)qaPairs.Count,
                AverageConfidence = qaPairs.Where(p => p.Answer != null).Average(p => p.Answer.Confidence),
                VerificationRate = qaPairs.Count(p => p.Answer?.IsVerified == true) / (double)qaPairs.Count
            };
        }
    }

    // Supporting classes
    public class AnswerSpanIdentifier
    {
        public List<AnswerSpan> IdentifyAnswerSpans(
            GeneratedQuestion question,
            string content,
            AnswerExtractionOptions options)
        {
            var spans = new List<AnswerSpan>();

            // Method 1: Keyword-based extraction
            spans.AddRange(ExtractKeywordBasedSpans(question, content));

            // Method 2: Pattern-based extraction
            spans.AddRange(ExtractPatternBasedSpans(question, content));

            // Method 3: Sentence-based extraction
            spans.AddRange(ExtractSentenceBasedSpans(question, content));

            // Remove duplicates and overlapping spans
            return DeduplicateSpans(spans);
        }

        private List<AnswerSpan> ExtractKeywordBasedSpans(GeneratedQuestion question, string content)
        {
            var spans = new List<AnswerSpan>();

            foreach (var keyword in question.Keywords)
            {
                var index = content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    // Extract surrounding context as potential answer
                    var start = Math.Max(0, index - 20);
                    var end = Math.Min(content.Length, index + keyword.Length + 50);
                    
                    spans.Add(new AnswerSpan
                    {
                        Text = content.Substring(start, end - start),
                        StartPosition = start,
                        EndPosition = end,
                        ExtractionMethod = "keyword-based"
                    });

                    index = content.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
                }
            }

            return spans;
        }

        private List<AnswerSpan> ExtractPatternBasedSpans(GeneratedQuestion question, string content)
        {
            var spans = new List<AnswerSpan>();
            
            // Extract based on question patterns
            if (question.QuestionText.StartsWith("What is", StringComparison.OrdinalIgnoreCase))
            {
                // Look for definitions
                var definitionPattern = @"(?:is|are|means|refers to)\s+([^.!?]+)";
                var matches = Regex.Matches(content, definitionPattern);
                
                foreach (Match match in matches)
                {
                    spans.Add(new AnswerSpan
                    {
                        Text = match.Groups[1].Value,
                        StartPosition = match.Groups[1].Index,
                        EndPosition = match.Groups[1].Index + match.Groups[1].Length,
                        ExtractionMethod = "pattern-based"
                    });
                }
            }

            return spans;
        }

        private List<AnswerSpan> ExtractSentenceBasedSpans(GeneratedQuestion question, string content)
        {
            var spans = new List<AnswerSpan>();
            var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            
            var currentPos = 0;
            foreach (var sentence in sentences)
            {
                // Check if sentence might contain answer
                if (question.Keywords.Any(k => sentence.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    spans.Add(new AnswerSpan
                    {
                        Text = sentence.Trim(),
                        StartPosition = currentPos,
                        EndPosition = currentPos + sentence.Length,
                        ExtractionMethod = "sentence-based"
                    });
                }
                currentPos += sentence.Length + 1; // +1 for delimiter
            }

            return spans;
        }

        private List<AnswerSpan> DeduplicateSpans(List<AnswerSpan> spans)
        {
            // Remove exact duplicates and highly overlapping spans
            var uniqueSpans = new List<AnswerSpan>();
            
            foreach (var span in spans.OrderBy(s => s.StartPosition))
            {
                if (!uniqueSpans.Any(s => IsOverlapping(s, span)))
                {
                    uniqueSpans.Add(span);
                }
            }

            return uniqueSpans;
        }

        private bool IsOverlapping(AnswerSpan span1, AnswerSpan span2)
        {
            // Check if spans overlap significantly (>50%)
            var overlapStart = Math.Max(span1.StartPosition, span2.StartPosition);
            var overlapEnd = Math.Min(span1.EndPosition, span2.EndPosition);
            
            if (overlapStart >= overlapEnd) return false;
            
            var overlapLength = overlapEnd - overlapStart;
            var minLength = Math.Min(
                span1.EndPosition - span1.StartPosition,
                span2.EndPosition - span2.StartPosition
            );

            return overlapLength > minLength * 0.5;
        }
    }

    public class AnswerConfidenceEvaluator
    {
        public double EvaluateConfidence(
            ExtractedAnswer answer,
            GeneratedQuestion question,
            DocumentChunk chunk)
        {
            double confidence = 0.5; // Base confidence

            // Factor 1: Answer type match
            if (IsAnswerTypeAppropriate(answer.Type, question.Type))
                confidence += 0.1;

            // Factor 2: Supporting evidence strength
            if (answer.SupportingEvidence.Any())
                confidence += 0.1 * Math.Min(1.0, answer.SupportingEvidence.Count / 3.0);

            // Factor 3: Context relevance
            var contextRelevance = CalculateContextRelevance(answer.Context, question.QuestionText);
            confidence += contextRelevance * 0.2;

            // Factor 4: Answer specificity
            var specificity = CalculateAnswerSpecificity(answer.Text);
            confidence += specificity * 0.1;

            return Math.Min(1.0, confidence);
        }

        private bool IsAnswerTypeAppropriate(AnswerType answerType, QuestionType questionType)
        {
            return (questionType, answerType) switch
            {
                (QuestionType.Factual, AnswerType.Entity) => true,
                (QuestionType.Factual, AnswerType.Numerical) => true,
                (QuestionType.Factual, AnswerType.Date) => true,
                (QuestionType.Conceptual, AnswerType.Explanation) => true,
                (QuestionType.Inferential, AnswerType.Explanation) => true,
                (QuestionType.MultiHop, AnswerType.Explanation) => true,
                _ => false
            };
        }

        private double CalculateContextRelevance(string context, string question)
        {
            var questionWords = question.ToLower().Split(' ').Where(w => w.Length > 3).ToHashSet();
            var contextWords = context.ToLower().Split(' ').Where(w => w.Length > 3).ToHashSet();

            if (!questionWords.Any() || !contextWords.Any()) return 0.0;

            var overlap = questionWords.Intersect(contextWords).Count();
            return (double)overlap / questionWords.Count;
        }

        private double CalculateAnswerSpecificity(string answerText)
        {
            // Specific answers contain proper nouns, numbers, or technical terms
            var specificityScore = 0.0;

            // Check for proper nouns
            if (Regex.IsMatch(answerText, @"\b[A-Z][a-z]+\b"))
                specificityScore += 0.3;

            // Check for numbers
            if (Regex.IsMatch(answerText, @"\d+"))
                specificityScore += 0.3;

            // Check for technical terms (multi-word capitalized phrases)
            if (Regex.IsMatch(answerText, @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b"))
                specificityScore += 0.4;

            return Math.Min(1.0, specificityScore);
        }
    }

    public class MultiSourceAnswerIntegrator
    {
        public ExtractedAnswer IntegrateAnswers(
            List<ExtractedAnswer> answers,
            GeneratedQuestion question)
        {
            // Sort answers by confidence
            var sortedAnswers = answers.OrderByDescending(a => a.Confidence).ToList();

            // If there's a clear best answer, use it
            if (sortedAnswers[0].Confidence > sortedAnswers[1].Confidence + 0.2)
            {
                return sortedAnswers[0];
            }

            // Otherwise, integrate multiple answers
            var integratedAnswer = new ExtractedAnswer
            {
                Text = CombineAnswerTexts(sortedAnswers.Take(3).ToList()),
                Confidence = sortedAnswers.Take(3).Average(a => a.Confidence),
                Type = sortedAnswers[0].Type,
                ExtractionMethod = "multi-source-integration",
                SupportingEvidence = sortedAnswers.SelectMany(a => a.SupportingEvidence).Distinct().ToList()
            };

            return integratedAnswer;
        }

        private string CombineAnswerTexts(List<ExtractedAnswer> answers)
        {
            // Find common elements and unique additions
            var allSentences = answers.SelectMany(a => 
                a.Text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            ).Select(s => s.Trim()).Distinct().ToList();

            // Combine into coherent text
            return string.Join(". ", allSentences) + ".";
        }
    }

    public class AnswerVerificationEngine
    {
        public VerificationResult VerifyAnswer(
            ExtractedAnswer answer,
            GeneratedQuestion question,
            DocumentChunk chunk)
        {
            var result = new VerificationResult();

            // Verify answer is actually in the chunk
            if (!chunk.Content.Contains(answer.Text))
            {
                result.IsValid = false;
                result.Score = 0.0;
                result.Issues.Add("Answer text not found in chunk");
                return result;
            }

            // Verify answer addresses the question
            if (!AddressesQuestion(answer, question))
            {
                result.Score = 0.3;
                result.Issues.Add("Answer may not fully address the question");
            }
            else
            {
                result.Score = 0.8;
            }

            // Verify answer consistency
            if (HasConsistentInformation(answer, chunk))
            {
                result.Score += 0.2;
            }
            else
            {
                result.Issues.Add("Potential inconsistency detected");
            }

            result.IsValid = result.Score >= 0.5;
            return result;
        }

        private bool AddressesQuestion(ExtractedAnswer answer, GeneratedQuestion question)
        {
            // Check if answer contains relevant information for the question type
            return question.Type switch
            {
                QuestionType.Factual => answer.Type == AnswerType.Entity || 
                                        answer.Type == AnswerType.Numerical ||
                                        answer.Type == AnswerType.Date,
                QuestionType.Conceptual => answer.Text.Split(' ').Length >= 10,
                QuestionType.Inferential => answer.Text.Split(' ').Length >= 15,
                QuestionType.MultiHop => answer.SupportingEvidence.Count > 1,
                _ => true
            };
        }

        private bool HasConsistentInformation(ExtractedAnswer answer, DocumentChunk chunk)
        {
            // Simple consistency check - no contradictions in the chunk
            // This is a simplified implementation
            return !chunk.Content.Contains("however") || 
                   !chunk.Content.Contains("contradiction") ||
                   !chunk.Content.Contains("incorrect");
        }
    }

    // Data structures
    public class AnswerExtractionOptions
    {
        public int MaxAnswerLength { get; set; } = 200;
        public double MinConfidenceThreshold { get; set; } = 0.5;
        public bool RequireVerification { get; set; } = true;
        public bool AllowMultiSentence { get; set; } = true;
    }

    public class AnswerExtractionResult
    {
        public string QuestionId { get; set; }
        public GeneratedQuestion Question { get; set; }
        public string ChunkId { get; set; }
        public ExtractedAnswer Answer { get; set; }
        public bool Success { get; set; }
        public string NoAnswerReason { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime ExtractedAt { get; set; }
    }

    public class BatchAnswerExtractionResult
    {
        public GeneratedQuestion Question { get; set; }
        public List<ExtractedAnswer> AnswersFromChunks { get; set; } = new List<ExtractedAnswer>();
        public ExtractedAnswer IntegratedAnswer { get; set; }
        public bool Success { get; set; }
        public DateTime ExtractedAt { get; set; }
    }

    public class QAExtractionResult
    {
        public string ChunkId { get; set; }
        public List<QAPair> QAPairs { get; set; } = new List<QAPair>();
        public QAQualityMetrics QualityMetrics { get; set; }
        public bool Success { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class ExtractedAnswer
    {
        public string Text { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public AnswerType Type { get; set; }
        public double Confidence { get; set; }
        public bool IsVerified { get; set; }
        public double VerificationScore { get; set; }
        public string Context { get; set; }
        public List<string> SupportingEvidence { get; set; } = new List<string>();
        public string ExtractionMethod { get; set; }
    }

    public partial class QAPair
    {
        public GeneratedQuestion Question { get; set; }
        public ExtractedAnswer Answer { get; set; }
        public string ChunkId { get; set; }
        public double Quality { get; set; }
    }

    public class AnswerSpan
    {
        public string Text { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public double Score { get; set; }
        public string ExtractionMethod { get; set; }
    }

    public class QAQualityMetrics
    {
        public double AverageQuality { get; set; }
        public double AnswerCoverage { get; set; }
        public double AverageConfidence { get; set; }
        public double VerificationRate { get; set; }
        public double MinQuality { get; set; }
        public double MaxQuality { get; set; }
        public double StandardDeviation { get; set; }
        public int CompletePairs { get; set; }
        public int VerifiedPairs { get; set; }
    }

    public class VerificationResult
    {
        public bool IsValid { get; set; }
        public double Score { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    public enum AnswerType
    {
        Entity,
        Numerical,
        Date,
        Phrase,
        Explanation
    }
}