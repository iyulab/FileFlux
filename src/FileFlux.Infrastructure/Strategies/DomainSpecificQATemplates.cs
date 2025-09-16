using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies
{
    /// <summary>
    /// Phase 13: T13-004 - Domain-Specific Q&amp;A Templates
    /// Provides specialized question generation templates for different domains
    /// </summary>
    public class DomainSpecificQATemplates
    {
        private readonly Dictionary<DomainType, IDomainQATemplate> _domainTemplates;
        private readonly DomainDetector _domainDetector;
        private readonly TemplateCustomizer _templateCustomizer;

        public DomainSpecificQATemplates()
        {
            _domainTemplates = new Dictionary<DomainType, IDomainQATemplate>
            {
                { DomainType.Technical, new TechnicalDocumentQATemplate() },
                { DomainType.Legal, new LegalDocumentQATemplate() },
                { DomainType.Medical, new MedicalDocumentQATemplate() },
                { DomainType.Educational, new EducationalMaterialQATemplate() },
                { DomainType.Business, new BusinessDocumentQATemplate() },
                { DomainType.Scientific, new ScientificPaperQATemplate() }
            };

            _domainDetector = new DomainDetector();
            _templateCustomizer = new TemplateCustomizer();
        }

        /// <summary>
        /// Generate domain-specific Q&amp;A pairs
        /// </summary>
        public DomainQAResult GenerateDomainSpecificQA(
            DocumentChunk chunk,
            DomainQAOptions? options = null)
        {
            options ??= new DomainQAOptions();
            
            var result = new DomainQAResult
            {
                ChunkId = chunk.Id,
                GeneratedAt = DateTime.UtcNow
            };

            // Detect domain if not specified
            var domain = options.Domain ?? _domainDetector.DetectDomain(chunk);
            result.DetectedDomain = domain;

            // Get appropriate template
            if (!_domainTemplates.TryGetValue(domain, out var template))
            {
                template = _domainTemplates[DomainType.General];
            }

            // Generate domain-specific questions
            var questions = template.GenerateQuestions(chunk, options);

            // Customize questions if needed
            if (options.CustomizeTemplates)
            {
                questions = _templateCustomizer.CustomizeQuestions(questions, chunk, domain);
            }

            // Extract domain-specific answers if enabled
            if (options.ExtractAnswers)
            {
                var answerExtractor = new DomainAwareAnswerExtractor(domain);
                foreach (var question in questions)
                {
                    var answer = answerExtractor.ExtractAnswer(question, chunk);
                    result.QAPairs.Add(new DomainQAPair
                    {
                        Question = question,
                        Answer = answer,
                        Domain = domain,
                        ChunkId = chunk.Id
                    });
                }
            }
            else
            {
                result.Questions = questions;
            }

            // Calculate domain-specific quality metrics
            result.QualityMetrics = CalculateDomainQualityMetrics(result, domain);

            return result;
        }

        /// <summary>
        /// Apply domain-specific templates to existing questions
        /// </summary>
        public List<DomainEnhancedQuestion> EnhanceWithDomainKnowledge(
            List<GeneratedQuestion> questions,
            DomainType domain)
        {
            var enhanced = new List<DomainEnhancedQuestion>();
            var template = _domainTemplates.GetValueOrDefault(domain) ?? 
                          _domainTemplates[DomainType.General];

            foreach (var question in questions)
            {
                var enhancedQuestion = new DomainEnhancedQuestion
                {
                    OriginalQuestion = question,
                    Domain = domain,
                    DomainSpecificMetadata = template.GetDomainMetadata(question),
                    ExpectedAnswerPattern = template.GetExpectedAnswerPattern(question),
                    DomainKeywords = template.GetDomainKeywords(question.QuestionText)
                };

                enhanced.Add(enhancedQuestion);
            }

            return enhanced;
        }

        private DomainQualityMetrics CalculateDomainQualityMetrics(
            DomainQAResult result, 
            DomainType domain)
        {
            var metrics = new DomainQualityMetrics
            {
                Domain = domain,
                TotalQuestions = result.Questions?.Count ?? result.QAPairs.Count,
                DomainRelevance = CalculateDomainRelevance(result, domain),
                TemplateConformance = CalculateTemplateConformance(result, domain),
                CoverageScore = CalculateDomainCoverage(result, domain)
            };

            return metrics;
        }

        private double CalculateDomainRelevance(DomainQAResult result, DomainType domain)
        {
            var template = _domainTemplates.GetValueOrDefault(domain);
            if (template == null) return 0.5;

            var domainKeywords = template.GetDomainKeywords("");
            var questions = result.Questions ?? result.QAPairs.Select(p => p.Question).ToList();

            if (!questions.Any()) return 0.0;

            var relevantQuestions = questions.Count(q => 
                domainKeywords.Any(k => q.QuestionText.Contains(k, StringComparison.OrdinalIgnoreCase)));

            return (double)relevantQuestions / questions.Count;
        }

        private double CalculateTemplateConformance(DomainQAResult result, DomainType domain)
        {
            var template = _domainTemplates.GetValueOrDefault(domain);
            if (template == null) return 0.5;

            var questions = result.Questions ?? result.QAPairs.Select(p => p.Question).ToList();
            if (!questions.Any()) return 0.0;

            var conformingQuestions = questions.Count(q => 
                template.ValidateQuestion(q));

            return (double)conformingQuestions / questions.Count;
        }

        private double CalculateDomainCoverage(DomainQAResult result, DomainType domain)
        {
            var template = _domainTemplates.GetValueOrDefault(domain);
            if (template == null) return 0.5;

            var requiredTopics = template.GetRequiredTopics();
            if (!requiredTopics.Any()) return 1.0;

            var questions = result.Questions ?? result.QAPairs.Select(p => p.Question).ToList();
            var coveredTopics = new HashSet<string>();

            foreach (var question in questions)
            {
                foreach (var topic in requiredTopics)
                {
                    if (question.QuestionText.Contains(topic, StringComparison.OrdinalIgnoreCase))
                    {
                        coveredTopics.Add(topic);
                    }
                }
            }

            return (double)coveredTopics.Count / requiredTopics.Count;
        }
    }

    // Domain-specific template implementations
    public class TechnicalDocumentQATemplate : IDomainQATemplate
    {
        private readonly List<QuestionTemplate> _templates = new List<QuestionTemplate>
        {
            new QuestionTemplate { Pattern = "What is the purpose of {FUNCTION}?", Type = QuestionType.Conceptual },
            new QuestionTemplate { Pattern = "How does {COMPONENT} interact with {COMPONENT2}?", Type = QuestionType.Inferential },
            new QuestionTemplate { Pattern = "What are the parameters of {FUNCTION}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What is the return type of {FUNCTION}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the dependencies of {MODULE}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "How is {CONCEPT} implemented?", Type = QuestionType.Conceptual },
            new QuestionTemplate { Pattern = "What is the time complexity of {ALGORITHM}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What design pattern is used in {COMPONENT}?", Type = QuestionType.Conceptual },
            new QuestionTemplate { Pattern = "What are the error handling mechanisms for {OPERATION}?", Type = QuestionType.Inferential },
            new QuestionTemplate { Pattern = "How does {SYSTEM} ensure {QUALITY_ATTRIBUTE}?", Type = QuestionType.Inferential }
        };

        public List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options)
        {
            var questions = new List<GeneratedQuestion>();
            
            // Extract technical entities
            var functions = ExtractFunctions(chunk.Content);
            var components = ExtractComponents(chunk.Content);
            var concepts = ExtractTechnicalConcepts(chunk.Content);

            // Generate questions from templates
            foreach (var template in _templates.Take(options.MaxQuestionsPerDomain))
            {
                var question = GenerateFromTemplate(template, functions, components, concepts);
                if (question != null)
                {
                    questions.Add(question);
                }
            }

            return questions;
        }

        private List<string> ExtractFunctions(string content)
        {
            var functions = new List<string>();
            
            // Pattern for function names (simplified)
            var pattern = @"\b(?:function|def|func|method)\s+(\w+)";
            var matches = Regex.Matches(content, pattern);
            
            foreach (Match match in matches)
            {
                functions.Add(match.Groups[1].Value);
            }

            // Also look for camelCase or PascalCase function names
            pattern = @"\b([a-z]+[A-Z]\w+)\s*\(";
            matches = Regex.Matches(content, pattern);
            
            foreach (Match match in matches)
            {
                functions.Add(match.Groups[1].Value);
            }

            return functions.Distinct().ToList();
        }

        private List<string> ExtractComponents(string content)
        {
            var components = new List<string>();
            
            // Look for class names, module names, etc.
            var pattern = @"\b(?:class|module|component|service|controller)\s+(\w+)";
            var matches = Regex.Matches(content, pattern);
            
            foreach (Match match in matches)
            {
                components.Add(match.Groups[1].Value);
            }

            return components.Distinct().ToList();
        }

        private List<string> ExtractTechnicalConcepts(string content)
        {
            var concepts = new List<string>
            {
                "authentication", "authorization", "caching", "validation",
                "encryption", "compression", "serialization", "synchronization",
                "threading", "async", "database", "API", "REST", "GraphQL"
            };

            return concepts.Where(c => content.Contains(c, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private GeneratedQuestion? GenerateFromTemplate(
            QuestionTemplate template,
            List<string> functions,
            List<string> components,
            List<string> concepts)
        {
            var questionText = template.Pattern;

            // Replace placeholders
            if (questionText.Contains("{FUNCTION}") && functions.Any())
            {
                questionText = questionText.Replace("{FUNCTION}", functions.First());
            }
            else if (questionText.Contains("{FUNCTION}"))
            {
                return null; // Skip if no functions found
            }

            if (questionText.Contains("{COMPONENT}") && components.Any())
            {
                questionText = questionText.Replace("{COMPONENT}", components.First());
            }
            
            if (questionText.Contains("{COMPONENT2}") && components.Count > 1)
            {
                questionText = questionText.Replace("{COMPONENT2}", components[1]);
            }

            if (questionText.Contains("{CONCEPT}") && concepts.Any())
            {
                questionText = questionText.Replace("{CONCEPT}", concepts.First());
            }

            // Generic replacements
            questionText = questionText.Replace("{MODULE}", components.FirstOrDefault() ?? "module");
            questionText = questionText.Replace("{ALGORITHM}", functions.FirstOrDefault() ?? "algorithm");
            questionText = questionText.Replace("{SYSTEM}", "the system");
            questionText = questionText.Replace("{QUALITY_ATTRIBUTE}", "performance");
            questionText = questionText.Replace("{OPERATION}", functions.FirstOrDefault() ?? "operation");

            return new GeneratedQuestion
            {
                QuestionText = questionText,
                Type = template.Type,
                Keywords = ExtractKeywords(questionText),
                Quality = 0.8,
                Complexity = template.Type == QuestionType.MultiHop ? 0.8 : 0.5
            };
        }

        private List<string> ExtractKeywords(string questionText)
        {
            var words = questionText.Split(' ')
                .Where(w => w.Length > 3 && !IsStopWord(w))
                .Select(w => w.Trim('?', '.', ','))
                .ToList();

            return words;
        }

        private bool IsStopWord(string word)
        {
            var stopWords = new HashSet<string> { "what", "how", "does", "the", "are", "is", "of", "with" };
            return stopWords.Contains(word.ToLower());
        }

        public Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question)
        {
            return new Dictionary<string, string>
            {
                { "domain", "technical" },
                { "subdomain", DetectSubdomain(question.QuestionText) },
                { "complexity", "medium" }
            };
        }

        private string DetectSubdomain(string questionText)
        {
            if (questionText.Contains("algorithm") || questionText.Contains("complexity"))
                return "algorithms";
            if (questionText.Contains("pattern") || questionText.Contains("design"))
                return "architecture";
            if (questionText.Contains("error") || questionText.Contains("exception"))
                return "error-handling";
            if (questionText.Contains("API") || questionText.Contains("REST"))
                return "api-design";
            
            return "general";
        }

        public string GetExpectedAnswerPattern(GeneratedQuestion question)
        {
            if (question.QuestionText.StartsWith("What is the purpose"))
                return "The purpose of {X} is to {action/goal}";
            if (question.QuestionText.StartsWith("How does"))
                return "{Subject} {action} by {method/process}";
            if (question.QuestionText.Contains("parameters"))
                return "The parameters are: {param1}, {param2}, ...";
            if (question.QuestionText.Contains("return type"))
                return "The return type is {type}";
            
            return "Direct factual or explanatory answer";
        }

        public List<string> GetDomainKeywords(string text)
        {
            return new List<string>
            {
                "function", "method", "class", "module", "algorithm",
                "implementation", "code", "API", "interface", "parameter",
                "return", "exception", "error", "debug", "test"
            };
        }

        public bool ValidateQuestion(GeneratedQuestion question)
        {
            // Check if question contains technical terms
            var technicalTerms = GetDomainKeywords("");
            return technicalTerms.Any(term => 
                question.QuestionText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRequiredTopics()
        {
            return new List<string>
            {
                "functionality", "implementation", "performance", "error handling", "design"
            };
        }
    }

    public class LegalDocumentQATemplate : IDomainQATemplate
    {
        private readonly List<QuestionTemplate> _templates = new List<QuestionTemplate>
        {
            new QuestionTemplate { Pattern = "What are the obligations of {PARTY}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the conditions for {CLAUSE}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What is the liability of {PARTY} in case of {EVENT}?", Type = QuestionType.Inferential },
            new QuestionTemplate { Pattern = "What is the governing law for this {DOCUMENT_TYPE}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the termination conditions?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the dispute resolution mechanisms?", Type = QuestionType.Conceptual },
            new QuestionTemplate { Pattern = "What are the confidentiality requirements?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the indemnification provisions?", Type = QuestionType.Conceptual }
        };

        public List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options)
        {
            var questions = new List<GeneratedQuestion>();
            
            // Extract legal entities
            var parties = ExtractParties(chunk.Content);
            var clauses = ExtractClauses(chunk.Content);
            var legalConcepts = ExtractLegalConcepts(chunk.Content);

            foreach (var template in _templates.Take(options.MaxQuestionsPerDomain))
            {
                var questionText = template.Pattern;

                // Replace placeholders
                if (parties.Any())
                {
                    questionText = questionText.Replace("{PARTY}", parties.First());
                }

                if (clauses.Any())
                {
                    questionText = questionText.Replace("{CLAUSE}", clauses.First());
                }

                questionText = questionText.Replace("{EVENT}", "breach");
                questionText = questionText.Replace("{DOCUMENT_TYPE}", "agreement");

                questions.Add(new GeneratedQuestion
                {
                    QuestionText = questionText,
                    Type = template.Type,
                    Keywords = ExtractLegalKeywords(questionText),
                    Quality = 0.85
                });
            }

            return questions;
        }

        private List<string> ExtractParties(string content)
        {
            var parties = new List<string>();
            
            // Look for typical party designations
            var pattern = @"(?:party|parties|contractor|client|vendor|buyer|seller|lessor|lessee)\s+[A-Z]\w+";
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                parties.Add(match.Value);
            }

            return parties.Distinct().ToList();
        }

        private List<string> ExtractClauses(string content)
        {
            var clauses = new List<string>();
            
            // Look for clause references
            var pattern = @"(?:clause|section|article|paragraph)\s+\d+(?:\.\d+)*";
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                clauses.Add(match.Value);
            }

            return clauses.Distinct().ToList();
        }

        private List<string> ExtractLegalConcepts(string content)
        {
            var concepts = new List<string>
            {
                "liability", "indemnification", "warranty", "representation",
                "confidentiality", "termination", "breach", "damages",
                "jurisdiction", "arbitration", "force majeure"
            };

            return concepts.Where(c => content.Contains(c, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> ExtractLegalKeywords(string questionText)
        {
            var legalTerms = new List<string>
            {
                "obligation", "liability", "condition", "provision",
                "clause", "termination", "dispute", "confidentiality"
            };

            return legalTerms.Where(term => 
                questionText.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question)
        {
            return new Dictionary<string, string>
            {
                { "domain", "legal" },
                { "document_type", "contract" },
                { "complexity", "high" }
            };
        }

        public string GetExpectedAnswerPattern(GeneratedQuestion question)
        {
            if (question.QuestionText.Contains("obligations"))
                return "The obligations include: (1) {obligation1}, (2) {obligation2}...";
            if (question.QuestionText.Contains("conditions"))
                return "The conditions are: {condition1} and {condition2}";
            if (question.QuestionText.Contains("liability"))
                return "{Party} is liable for {scope} subject to {limitations}";
            
            return "Legal provision or requirement statement";
        }

        public List<string> GetDomainKeywords(string text)
        {
            return new List<string>
            {
                "agreement", "contract", "obligation", "liability", "clause",
                "provision", "term", "condition", "party", "breach"
            };
        }

        public bool ValidateQuestion(GeneratedQuestion question)
        {
            var legalTerms = GetDomainKeywords("");
            return legalTerms.Any(term => 
                question.QuestionText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRequiredTopics()
        {
            return new List<string>
            {
                "obligations", "rights", "liabilities", "termination", "dispute resolution"
            };
        }
    }

    public class MedicalDocumentQATemplate : IDomainQATemplate
    {
        private readonly List<QuestionTemplate> _templates = new List<QuestionTemplate>
        {
            new QuestionTemplate { Pattern = "What are the symptoms of {CONDITION}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What is the treatment for {CONDITION}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the side effects of {MEDICATION}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What is the dosage of {MEDICATION}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "What are the contraindications for {TREATMENT}?", Type = QuestionType.Factual },
            new QuestionTemplate { Pattern = "How is {CONDITION} diagnosed?", Type = QuestionType.Conceptual },
            new QuestionTemplate { Pattern = "What is the prognosis for {CONDITION}?", Type = QuestionType.Inferential }
        };

        public List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options)
        {
            var questions = new List<GeneratedQuestion>();
            
            // Extract medical entities
            var conditions = ExtractMedicalConditions(chunk.Content);
            var medications = ExtractMedications(chunk.Content);
            var treatments = ExtractTreatments(chunk.Content);

            foreach (var template in _templates.Take(options.MaxQuestionsPerDomain))
            {
                var questionText = template.Pattern;

                if (conditions.Any())
                {
                    questionText = questionText.Replace("{CONDITION}", conditions.First());
                }

                if (medications.Any())
                {
                    questionText = questionText.Replace("{MEDICATION}", medications.First());
                }

                if (treatments.Any())
                {
                    questionText = questionText.Replace("{TREATMENT}", treatments.First());
                }

                // Skip if placeholders couldn't be replaced
                if (questionText.Contains("{") && questionText.Contains("}"))
                    continue;

                questions.Add(new GeneratedQuestion
                {
                    QuestionText = questionText,
                    Type = template.Type,
                    Keywords = ExtractMedicalKeywords(questionText),
                    Quality = 0.9
                });
            }

            return questions;
        }

        private List<string> ExtractMedicalConditions(string content)
        {
            // Simplified extraction - in real implementation would use medical NER
            var commonConditions = new List<string>
            {
                "diabetes", "hypertension", "cancer", "infection", "inflammation",
                "disease", "syndrome", "disorder", "condition"
            };

            return commonConditions.Where(c => 
                content.Contains(c, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> ExtractMedications(string content)
        {
            // Look for medication patterns
            var medications = new List<string>();
            
            // Pattern for medications (often end in common suffixes)
            var pattern = @"\b\w+(?:cillin|mycin|azole|pril|sartan|statin|prazole)\b";
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                medications.Add(match.Value);
            }

            return medications.Distinct().ToList();
        }

        private List<string> ExtractTreatments(string content)
        {
            var treatments = new List<string>
            {
                "surgery", "therapy", "chemotherapy", "radiation", "medication",
                "treatment", "procedure", "intervention"
            };

            return treatments.Where(t => 
                content.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> ExtractMedicalKeywords(string questionText)
        {
            var medicalTerms = new List<string>
            {
                "symptom", "treatment", "diagnosis", "prognosis", "medication",
                "dosage", "side effect", "contraindication"
            };

            return medicalTerms.Where(term => 
                questionText.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question)
        {
            return new Dictionary<string, string>
            {
                { "domain", "medical" },
                { "category", "clinical" },
                { "sensitivity", "high" }
            };
        }

        public string GetExpectedAnswerPattern(GeneratedQuestion question)
        {
            if (question.QuestionText.Contains("symptoms"))
                return "The symptoms include: {symptom1}, {symptom2}, {symptom3}";
            if (question.QuestionText.Contains("treatment"))
                return "The treatment involves {method} for {duration}";
            if (question.QuestionText.Contains("dosage"))
                return "{amount} {unit} {frequency}";
            
            return "Medical information statement";
        }

        public List<string> GetDomainKeywords(string text)
        {
            return new List<string>
            {
                "patient", "diagnosis", "treatment", "symptom", "medication",
                "disease", "condition", "therapy", "clinical", "medical"
            };
        }

        public bool ValidateQuestion(GeneratedQuestion question)
        {
            var medicalTerms = GetDomainKeywords("");
            return medicalTerms.Any(term => 
                question.QuestionText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRequiredTopics()
        {
            return new List<string>
            {
                "diagnosis", "treatment", "symptoms", "prognosis", "prevention"
            };
        }
    }

    public class EducationalMaterialQATemplate : IDomainQATemplate
    {
        public List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options)
        {
            var questions = new List<GeneratedQuestion>();
            
            // Educational question patterns
            var patterns = new List<QuestionTemplate>
            {
                new QuestionTemplate { Pattern = "Define {CONCEPT}.", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "Explain the difference between {CONCEPT1} and {CONCEPT2}.", Type = QuestionType.Conceptual },
                new QuestionTemplate { Pattern = "Give an example of {CONCEPT}.", Type = QuestionType.Conceptual },
                new QuestionTemplate { Pattern = "What are the main characteristics of {TOPIC}?", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "How does {PROCESS} work?", Type = QuestionType.Conceptual },
                new QuestionTemplate { Pattern = "Why is {CONCEPT} important?", Type = QuestionType.Inferential }
            };

            var concepts = ExtractEducationalConcepts(chunk.Content);
            
            foreach (var pattern in patterns.Take(options.MaxQuestionsPerDomain))
            {
                if (concepts.Any())
                {
                    var questionText = pattern.Pattern;
                    questionText = questionText.Replace("{CONCEPT}", concepts.First());
                    questionText = questionText.Replace("{CONCEPT1}", concepts.First());
                    
                    if (concepts.Count > 1)
                        questionText = questionText.Replace("{CONCEPT2}", concepts[1]);
                    
                    questionText = questionText.Replace("{TOPIC}", concepts.First());
                    questionText = questionText.Replace("{PROCESS}", concepts.First());

                    questions.Add(new GeneratedQuestion
                    {
                        QuestionText = questionText,
                        Type = pattern.Type,
                        Keywords = concepts.Take(3).ToList(),
                        Quality = 0.85
                    });
                }
            }

            return questions;
        }

        private List<string> ExtractEducationalConcepts(string content)
        {
            // Extract key concepts (simplified)
            var concepts = new List<string>();
            
            // Look for defined terms
            var pattern = @"(?:is defined as|refers to|means|is called)\s+(\w+(?:\s+\w+)*)";
            var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                concepts.Add(match.Groups[1].Value.Trim());
            }

            // Also look for emphasized terms
            pattern = @"\*\*(\w+(?:\s+\w+)*)\*\*";
            matches = Regex.Matches(content, pattern);
            
            foreach (Match match in matches)
            {
                concepts.Add(match.Groups[1].Value);
            }

            return concepts.Distinct().ToList();
        }

        public Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question)
        {
            return new Dictionary<string, string>
            {
                { "domain", "educational" },
                { "level", "intermediate" },
                { "type", "conceptual" }
            };
        }

        public string GetExpectedAnswerPattern(GeneratedQuestion question)
        {
            if (question.QuestionText.StartsWith("Define"))
                return "{Term} is {definition}";
            if (question.QuestionText.Contains("difference"))
                return "{Concept1} is {description1}, while {Concept2} is {description2}";
            if (question.QuestionText.Contains("example"))
                return "An example of {concept} is {specific_example}";
            
            return "Educational explanation";
        }

        public List<string> GetDomainKeywords(string text)
        {
            return new List<string>
            {
                "define", "explain", "describe", "example", "concept",
                "theory", "principle", "method", "process", "characteristic"
            };
        }

        public bool ValidateQuestion(GeneratedQuestion question)
        {
            return question.QuestionText.Length > 10 && 
                   (question.QuestionText.EndsWith("?") || question.QuestionText.EndsWith("."));
        }

        public List<string> GetRequiredTopics()
        {
            return new List<string>
            {
                "definition", "explanation", "example", "comparison", "application"
            };
        }
    }

    public class BusinessDocumentQATemplate : IDomainQATemplate
    {
        public List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options)
        {
            var questions = new List<GeneratedQuestion>();
            
            var patterns = new List<QuestionTemplate>
            {
                new QuestionTemplate { Pattern = "What is the target market for {PRODUCT}?", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "What is the revenue model?", Type = QuestionType.Conceptual },
                new QuestionTemplate { Pattern = "What are the key performance indicators?", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "What is the competitive advantage?", Type = QuestionType.Inferential },
                new QuestionTemplate { Pattern = "What are the growth projections?", Type = QuestionType.Factual }
            };

            var businessTerms = ExtractBusinessTerms(chunk.Content);
            
            foreach (var pattern in patterns.Take(options.MaxQuestionsPerDomain))
            {
                var questionText = pattern.Pattern;
                
                if (businessTerms.Any())
                {
                    questionText = questionText.Replace("{PRODUCT}", businessTerms.First());
                }

                questions.Add(new GeneratedQuestion
                {
                    QuestionText = questionText,
                    Type = pattern.Type,
                    Keywords = ExtractBusinessKeywords(questionText),
                    Quality = 0.8
                });
            }

            return questions;
        }

        private List<string> ExtractBusinessTerms(string content)
        {
            var terms = new List<string>
            {
                "revenue", "profit", "market", "customer", "strategy",
                "growth", "competition", "product", "service", "ROI"
            };

            return terms.Where(t => content.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private List<string> ExtractBusinessKeywords(string questionText)
        {
            var keywords = new List<string>
            {
                "market", "revenue", "KPI", "competitive", "growth", "projection"
            };

            return keywords.Where(k => 
                questionText.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question)
        {
            return new Dictionary<string, string>
            {
                { "domain", "business" },
                { "focus", "strategy" }
            };
        }

        public string GetExpectedAnswerPattern(GeneratedQuestion question)
        {
            if (question.QuestionText.Contains("target market"))
                return "The target market is {demographic} with {characteristics}";
            if (question.QuestionText.Contains("revenue model"))
                return "The revenue model is based on {model_type}";
            
            return "Business metric or strategy statement";
        }

        public List<string> GetDomainKeywords(string text)
        {
            return new List<string>
            {
                "business", "revenue", "profit", "market", "strategy",
                "customer", "growth", "ROI", "KPI", "competitive"
            };
        }

        public bool ValidateQuestion(GeneratedQuestion question)
        {
            var businessTerms = GetDomainKeywords("");
            return businessTerms.Any(term => 
                question.QuestionText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRequiredTopics()
        {
            return new List<string>
            {
                "market", "revenue", "strategy", "competition", "growth"
            };
        }
    }

    public class ScientificPaperQATemplate : IDomainQATemplate
    {
        public List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options)
        {
            var questions = new List<GeneratedQuestion>();
            
            var patterns = new List<QuestionTemplate>
            {
                new QuestionTemplate { Pattern = "What is the hypothesis of this study?", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "What methodology was used?", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "What were the main findings?", Type = QuestionType.Factual },
                new QuestionTemplate { Pattern = "What are the limitations of this study?", Type = QuestionType.Inferential },
                new QuestionTemplate { Pattern = "How do these results compare to previous research?", Type = QuestionType.MultiHop }
            };

            foreach (var pattern in patterns.Take(options.MaxQuestionsPerDomain))
            {
                questions.Add(new GeneratedQuestion
                {
                    QuestionText = pattern.Pattern,
                    Type = pattern.Type,
                    Keywords = ExtractScientificKeywords(pattern.Pattern),
                    Quality = 0.9
                });
            }

            return questions;
        }

        private List<string> ExtractScientificKeywords(string questionText)
        {
            var keywords = new List<string>
            {
                "hypothesis", "methodology", "findings", "results", "research",
                "study", "experiment", "data", "analysis", "conclusion"
            };

            return keywords.Where(k => 
                questionText.Contains(k, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question)
        {
            return new Dictionary<string, string>
            {
                { "domain", "scientific" },
                { "type", "research" }
            };
        }

        public string GetExpectedAnswerPattern(GeneratedQuestion question)
        {
            if (question.QuestionText.Contains("hypothesis"))
                return "The hypothesis is that {statement}";
            if (question.QuestionText.Contains("methodology"))
                return "The study used {method} to {purpose}";
            if (question.QuestionText.Contains("findings"))
                return "The main findings were: (1) {finding1}, (2) {finding2}";
            
            return "Scientific statement or explanation";
        }

        public List<string> GetDomainKeywords(string text)
        {
            return new List<string>
            {
                "hypothesis", "experiment", "method", "result", "finding",
                "conclusion", "data", "analysis", "research", "study"
            };
        }

        public bool ValidateQuestion(GeneratedQuestion question)
        {
            var scientificTerms = GetDomainKeywords("");
            return scientificTerms.Any(term => 
                question.QuestionText.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRequiredTopics()
        {
            return new List<string>
            {
                "hypothesis", "methodology", "results", "discussion", "conclusion"
            };
        }
    }

    // Supporting classes
    public class DomainDetector
    {
        public DomainType DetectDomain(DocumentChunk chunk)
        {
            var content = chunk.Content.ToLower();
            var domainScores = new Dictionary<DomainType, int>();

            // Technical domain indicators
            var technicalTerms = new[] { "function", "class", "algorithm", "code", "implementation", "API" };
            domainScores[DomainType.Technical] = technicalTerms.Count(t => content.Contains(t));

            // Legal domain indicators
            var legalTerms = new[] { "agreement", "contract", "liability", "clause", "provision", "party" };
            domainScores[DomainType.Legal] = legalTerms.Count(t => content.Contains(t));

            // Medical domain indicators
            var medicalTerms = new[] { "patient", "diagnosis", "treatment", "symptom", "medication", "clinical" };
            domainScores[DomainType.Medical] = medicalTerms.Count(t => content.Contains(t));

            // Educational domain indicators
            var educationalTerms = new[] { "learn", "understand", "explain", "example", "concept", "theory" };
            domainScores[DomainType.Educational] = educationalTerms.Count(t => content.Contains(t));

            // Business domain indicators
            var businessTerms = new[] { "revenue", "market", "customer", "strategy", "profit", "ROI" };
            domainScores[DomainType.Business] = businessTerms.Count(t => content.Contains(t));

            // Scientific domain indicators
            var scientificTerms = new[] { "hypothesis", "experiment", "research", "data", "analysis", "study" };
            domainScores[DomainType.Scientific] = scientificTerms.Count(t => content.Contains(t));

            // Return domain with highest score
            var maxScore = domainScores.Values.Max();
            if (maxScore == 0)
                return DomainType.General;

            return domainScores.First(kvp => kvp.Value == maxScore).Key;
        }
    }

    public class TemplateCustomizer
    {
        public List<GeneratedQuestion> CustomizeQuestions(
            List<GeneratedQuestion> questions,
            DocumentChunk chunk,
            DomainType domain)
        {
            var customized = new List<GeneratedQuestion>();

            foreach (var question in questions)
            {
                var custom = new GeneratedQuestion
                {
                    QuestionText = CustomizeQuestionText(question.QuestionText, chunk, domain),
                    Type = question.Type,
                    Keywords = question.Keywords,
                    Quality = question.Quality,
                    Complexity = AdjustComplexity(question.Complexity, domain),
                    RequiredHops = question.RequiredHops,
                    ExpectedAnswerLocation = question.ExpectedAnswerLocation
                };

                customized.Add(custom);
            }

            return customized;
        }

        private string CustomizeQuestionText(string text, DocumentChunk chunk, DomainType domain)
        {
            // Add domain-specific prefixes or suffixes
            return domain switch
            {
                DomainType.Legal => $"According to the document, {text}",
                DomainType.Medical => $"From a clinical perspective, {text}",
                DomainType.Technical => text, // Keep as is
                _ => text
            };
        }

        private double AdjustComplexity(double baseComplexity, DomainType domain)
        {
            return domain switch
            {
                DomainType.Legal => Math.Min(1.0, baseComplexity + 0.2),
                DomainType.Medical => Math.Min(1.0, baseComplexity + 0.15),
                DomainType.Scientific => Math.Min(1.0, baseComplexity + 0.1),
                _ => baseComplexity
            };
        }
    }

    public class DomainAwareAnswerExtractor
    {
        private readonly DomainType _domain;

        public DomainAwareAnswerExtractor(DomainType domain)
        {
            _domain = domain;
        }

        public ExtractedAnswer ExtractAnswer(GeneratedQuestion question, DocumentChunk chunk)
        {
            // Simplified answer extraction - would use domain-specific patterns in real implementation
            var answer = new ExtractedAnswer
            {
                Text = ExtractDomainSpecificAnswer(question, chunk.Content),
                Type = DetermineAnswerType(question),
                Confidence = CalculateDomainConfidence(question, _domain),
                ExtractionMethod = $"domain-specific-{_domain}"
            };

            return answer;
        }

        private string ExtractDomainSpecificAnswer(GeneratedQuestion question, string content)
        {
            // Find sentences that might contain the answer
            var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var sentence in sentences)
            {
                if (question.Keywords.Any(k => sentence.Contains(k, StringComparison.OrdinalIgnoreCase)))
                {
                    return sentence.Trim();
                }
            }

            return "Answer not found in this chunk";
        }

        private AnswerType DetermineAnswerType(GeneratedQuestion question)
        {
            return question.Type switch
            {
                QuestionType.Factual => AnswerType.Entity,
                QuestionType.Conceptual => AnswerType.Explanation,
                QuestionType.Inferential => AnswerType.Explanation,
                QuestionType.MultiHop => AnswerType.Explanation,
                _ => AnswerType.Phrase
            };
        }

        private double CalculateDomainConfidence(GeneratedQuestion question, DomainType domain)
        {
            // Higher confidence for domain-specific questions
            return domain == DomainType.General ? 0.6 : 0.8;
        }
    }

    // Interfaces and data structures
    public interface IDomainQATemplate
    {
        List<GeneratedQuestion> GenerateQuestions(DocumentChunk chunk, DomainQAOptions options);
        Dictionary<string, string> GetDomainMetadata(GeneratedQuestion question);
        string GetExpectedAnswerPattern(GeneratedQuestion question);
        List<string> GetDomainKeywords(string text);
        bool ValidateQuestion(GeneratedQuestion question);
        List<string> GetRequiredTopics();
    }

    public class DomainQAOptions
    {
        public DomainType? Domain { get; set; }
        public int MaxQuestionsPerDomain { get; set; } = 5;
        public bool CustomizeTemplates { get; set; } = true;
        public bool ExtractAnswers { get; set; } = true;
    }

    public class DomainQAResult
    {
        public string ChunkId { get; set; }
        public DomainType DetectedDomain { get; set; }
        public List<GeneratedQuestion> Questions { get; set; } = new List<GeneratedQuestion>();
        public List<DomainQAPair> QAPairs { get; set; } = new List<DomainQAPair>();
        public DomainQualityMetrics QualityMetrics { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class DomainQAPair
    {
        public GeneratedQuestion Question { get; set; }
        public ExtractedAnswer Answer { get; set; }
        public DomainType Domain { get; set; }
        public string ChunkId { get; set; }
    }

    public class DomainEnhancedQuestion
    {
        public GeneratedQuestion OriginalQuestion { get; set; }
        public DomainType Domain { get; set; }
        public Dictionary<string, string> DomainSpecificMetadata { get; set; }
        public string ExpectedAnswerPattern { get; set; }
        public List<string> DomainKeywords { get; set; }
    }

    public class DomainQualityMetrics
    {
        public DomainType Domain { get; set; }
        public int TotalQuestions { get; set; }
        public double DomainRelevance { get; set; }
        public double TemplateConformance { get; set; }
        public double CoverageScore { get; set; }
    }

    public class QuestionTemplate
    {
        public string Pattern { get; set; }
        public QuestionType Type { get; set; }
    }

    public enum DomainType
    {
        General,
        Technical,
        Legal,
        Medical,
        Educational,
        Business,
        Scientific
    }
}