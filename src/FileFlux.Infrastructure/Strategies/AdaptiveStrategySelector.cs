using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FileFlux.Core;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// LLM 기반 적응형 전략 선택기
/// 문서 분석을 통해 최적의 청킹 전략을 자동으로 선택
/// </summary>
public class AdaptiveStrategySelector : IAdaptiveStrategySelector
{
    private readonly ITextCompletionService _llmService;
    private readonly IChunkingStrategyFactory _strategyFactory;
    private readonly Dictionary<string, IChunkingStrategyMetadata> _strategyMetadata;
    private readonly IDocumentReader? _documentReader;

    public AdaptiveStrategySelector(
        ITextCompletionService llmService,
        IChunkingStrategyFactory strategyFactory,
        IDocumentReader? documentReader = null)
    {
        _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
        _strategyFactory = strategyFactory ?? throw new ArgumentNullException(nameof(strategyFactory));
        _documentReader = documentReader;
        _strategyMetadata = InitializeStrategyMetadata();
    }

    /// <summary>
    /// 문서를 분석하여 최적의 전략을 선택
    /// </summary>
    public async Task<StrategySelectionResult> SelectOptimalStrategyAsync(
        string filePath,
        DocumentContent? extractedContent = null,
        CancellationToken cancellationToken = default)
    {
        // 1. 파일 정보 수집
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        
        // 2. 문서 샘플 추출 (첫 2000 토큰)
        var sampleContent = await ExtractSampleContentAsync(
            filePath, 
            extractedContent, 
            cancellationToken);
        
        // 3. 문서 특성 분석
        var documentCharacteristics = await AnalyzeDocumentCharacteristicsAsync(
            sampleContent,
            extension,
            cancellationToken);
        
        // 4. LLM을 통한 전략 추천
        var recommendedStrategy = await GetLLMRecommendationAsync(
            documentCharacteristics,
            _strategyMetadata.Values.ToList(),
            cancellationToken);
        
        // 5. 최종 전략 선택 및 검증
        var selectedStrategy = ValidateAndFinalizeSelection(
            recommendedStrategy,
            documentCharacteristics);
        
        return new StrategySelectionResult
        {
            StrategyName = selectedStrategy.StrategyName,
            Confidence = selectedStrategy.Confidence,
            Reasoning = selectedStrategy.Reasoning,
            UsedLLM = true
        };
    }

    /// <summary>
    /// 문서 샘플 추출
    /// </summary>
    private async Task<string> ExtractSampleContentAsync(
        string filePath,
        DocumentContent? existingContent,
        CancellationToken cancellationToken)
    {
        // 이미 추출된 내용이 있으면 사용
        if (existingContent != null && !string.IsNullOrWhiteSpace(existingContent.Text))
        {
            var text = existingContent.Text;
            // 첫 2000자 정도 추출 (약 500 토큰)
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }
        
        // 파일에서 직접 읽기
        if (_documentReader != null && _documentReader.CanRead(filePath))
        {
            var rawContent = await _documentReader.ExtractAsync(filePath, cancellationToken);
            var text = rawContent.Text;
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }
        
        // 텍스트 파일 직접 읽기 (fallback)
        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken);
            return text.Length > 2000 ? text.Substring(0, 2000) : text;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 문서 특성 분석
    /// </summary>
    private async Task<DocumentCharacteristics> AnalyzeDocumentCharacteristicsAsync(
        string sampleContent,
        string fileExtension,
        CancellationToken cancellationToken)
    {
        var characteristics = new DocumentCharacteristics
        {
            FileExtension = fileExtension,
            SampleContent = sampleContent,
            EstimatedTokenCount = EstimateTokenCount(sampleContent)
        };
        
        // 구조적 특징 감지
        characteristics.HasMarkdownHeaders = DetectMarkdownHeaders(sampleContent);
        characteristics.HasCodeBlocks = DetectCodeBlocks(sampleContent);
        characteristics.HasTables = DetectTables(sampleContent);
        characteristics.HasLists = DetectLists(sampleContent);
        characteristics.HasMathFormulas = DetectMathFormulas(sampleContent);
        characteristics.HasNumberedSections = DetectNumberedSections(sampleContent);
        characteristics.HasStructuredRequirements = DetectStructuredRequirements(sampleContent);
        
        // 콘텐츠 타입 추론
        characteristics.ContentType = InferContentType(sampleContent, fileExtension);
        characteristics.Language = DetectLanguage(sampleContent);
        characteristics.Domain = InferDomain(sampleContent);
        
        // 텍스트 특성 분석
        characteristics.AverageSentenceLength = CalculateAverageSentenceLength(sampleContent);
        characteristics.ParagraphCount = CountParagraphs(sampleContent);
        characteristics.StructureComplexity = CalculateStructureComplexity(characteristics);
        
        return characteristics;
    }

    /// <summary>
    /// LLM을 통한 전략 추천
    /// </summary>
    private async Task<LLMStrategyRecommendation> GetLLMRecommendationAsync(
        DocumentCharacteristics characteristics,
        List<IChunkingStrategyMetadata> availableStrategies,
        CancellationToken cancellationToken)
    {
        // 전략 설명 준비
        var strategiesJson = JsonSerializer.Serialize(
            availableStrategies.Select(s => new
            {
                Name = s.StrategyName,
                Description = s.Description,
                Strengths = s.Strengths,
                OptimalFor = s.OptimalForDocumentTypes,
                Scenarios = s.RecommendedScenarios,
                Performance = s.Performance
            }),
            new JsonSerializerOptions { WriteIndented = true });

        // LLM 프롬프트 구성
        var prompt = $@"You are an expert in document processing and RAG (Retrieval-Augmented Generation) systems.
Analyze the following document characteristics and recommend the best chunking strategy.

DOCUMENT CHARACTERISTICS:
- File Extension: {characteristics.FileExtension}
- Content Type: {characteristics.ContentType}
- Domain: {characteristics.Domain}
- Language: {characteristics.Language}
- Has Markdown Headers: {characteristics.HasMarkdownHeaders}
- Has Code Blocks: {characteristics.HasCodeBlocks}
- Has Tables: {characteristics.HasTables}
- Has Lists: {characteristics.HasLists}
- Has Numbered Sections: {characteristics.HasNumberedSections}
- Has Structured Requirements: {characteristics.HasStructuredRequirements}
- Average Sentence Length: {characteristics.AverageSentenceLength:F1} words
- Structure Complexity: {characteristics.StructureComplexity}/10

IMPORTANT: If the document has numbered sections or structured requirements,
strongly consider Smart or Intelligent strategies for optimal section-aware chunking.

SAMPLE CONTENT (first 500 chars):
{(characteristics.SampleContent.Length > 500 ? characteristics.SampleContent.Substring(0, 500) : characteristics.SampleContent)}

AVAILABLE STRATEGIES:
{strategiesJson}

Please recommend the BEST strategy for this document and explain why.
Also suggest 2 alternative strategies as fallbacks.

Return your response in the following JSON format:
{{
  ""primaryStrategy"": ""StrategyName"",
  ""confidence"": 0.95,
  ""reasoning"": ""Detailed explanation of why this strategy is best"",
  ""alternatives"": [
    {{
      ""strategy"": ""AlternativeStrategy1"",
      ""confidence"": 0.85,
      ""reasoning"": ""Why this could also work""
    }},
    {{
      ""strategy"": ""AlternativeStrategy2"",
      ""confidence"": 0.75,
      ""reasoning"": ""Another viable option""
    }}
  ],
  ""keyFactors"": [""factor1"", ""factor2"", ""factor3""]
}}";

        try
        {
            var response = await _llmService.GenerateAsync(prompt, cancellationToken);
            
            // Parse LLM response
            var recommendation = ParseLLMResponse(response);
            return recommendation;
        }
        catch (Exception ex)
        {
            // Fallback to rule-based selection if LLM fails
            return GetFallbackRecommendation(characteristics);
        }
    }

    /// <summary>
    /// LLM 응답 파싱
    /// </summary>
    private LLMStrategyRecommendation ParseLLMResponse(string response)
    {
        try
        {
            // JSON 부분 추출
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}') + 1;
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart);
                var parsed = JsonDocument.Parse(jsonString);
                var root = parsed.RootElement;
                
                var recommendation = new LLMStrategyRecommendation
                {
                    StrategyName = root.GetProperty("primaryStrategy").GetString() ?? "Smart",
                    Confidence = root.GetProperty("confidence").GetDouble(),
                    Reasoning = root.GetProperty("reasoning").GetString() ?? "",
                    Alternatives = new List<AlternativeStrategy>()
                };
                
                if (root.TryGetProperty("alternatives", out var alternatives))
                {
                    foreach (var alt in alternatives.EnumerateArray())
                    {
                        recommendation.Alternatives.Add(new AlternativeStrategy
                        {
                            StrategyName = alt.GetProperty("strategy").GetString() ?? "",
                            Confidence = alt.GetProperty("confidence").GetDouble(),
                            Reasoning = alt.GetProperty("reasoning").GetString() ?? ""
                        });
                    }
                }
                
                return recommendation;
            }
        }
        catch
        {
            // Parsing failed
        }
        
        // Default fallback
        return new LLMStrategyRecommendation
        {
            StrategyName = "Smart",
            Confidence = 0.7,
            Reasoning = "Default selection due to parsing error",
            Alternatives = new List<AlternativeStrategy>()
        };
    }

    /// <summary>
    /// 규칙 기반 폴백 추천
    /// </summary>
    private LLMStrategyRecommendation GetFallbackRecommendation(DocumentCharacteristics characteristics)
    {
        var recommendation = new LLMStrategyRecommendation
        {
            Alternatives = new List<AlternativeStrategy>()
        };
        
        // 규칙 기반 선택 로직 - 구조화 문서 우선 처리
        if (characteristics.HasNumberedSections || characteristics.HasStructuredRequirements)
        {
            recommendation.StrategyName = "Smart";
            recommendation.Confidence = 0.95;
            recommendation.Reasoning = "Structured document with numbered sections or requirements - Smart strategy provides optimal section-aware chunking";
        }
        else if (characteristics.HasCodeBlocks && characteristics.HasMarkdownHeaders)
        {
            recommendation.StrategyName = "Intelligent";
            recommendation.Confidence = 0.85;
            recommendation.Reasoning = "Technical documentation with code and structure - Intelligent strategy preserves formatting";
        }
        else if (characteristics.Domain == "Legal" || characteristics.Domain == "Medical")
        {
            recommendation.StrategyName = "Smart";
            recommendation.Confidence = 0.9;
            recommendation.Reasoning = "Critical domain requiring high sentence integrity - Smart strategy ensures completeness";
        }
        else if (characteristics.ContentType == "Narrative" || characteristics.AverageSentenceLength > 20)
        {
            recommendation.StrategyName = "Semantic";
            recommendation.Confidence = 0.8;
            recommendation.Reasoning = "Long-form narrative content - Semantic strategy maintains context flow";
        }
        else if (characteristics.StructureComplexity < 3)
        {
            recommendation.StrategyName = "Paragraph";
            recommendation.Confidence = 0.75;
            recommendation.Reasoning = "Simple structure - Paragraph strategy is efficient";
        }
        else
        {
            recommendation.StrategyName = "Smart";
            recommendation.Confidence = 0.7;
            recommendation.Reasoning = "Default selection - Smart strategy provides best general quality";
        }
        
        // Add alternatives
        if (recommendation.StrategyName != "Smart")
        {
            recommendation.Alternatives.Add(new AlternativeStrategy
            {
                StrategyName = "Smart",
                Confidence = 0.7,
                Reasoning = "Universal fallback with quality guarantee"
            });
        }
        
        if (recommendation.StrategyName != "Intelligent")
        {
            recommendation.Alternatives.Add(new AlternativeStrategy
            {
                StrategyName = "Intelligent",
                Confidence = 0.65,
                Reasoning = "Good for structured documents"
            });
        }
        
        return recommendation;
    }

    /// <summary>
    /// 선택 검증 및 최종화
    /// </summary>
    private LLMStrategyRecommendation ValidateAndFinalizeSelection(
        LLMStrategyRecommendation recommendation,
        DocumentCharacteristics characteristics)
    {
        // 전략이 실제로 존재하는지 확인
        if (!_strategyMetadata.ContainsKey(recommendation.StrategyName))
        {
            // 존재하지 않으면 기본값으로 대체
            recommendation.StrategyName = "Smart";
            recommendation.Reasoning = "Selected strategy not available, using Smart as default";
            recommendation.Confidence *= 0.8; // 신뢰도 감소
        }
        
        // Phase 10: 파일 형식별 최적화 (Phase 9 평가 결과 기반)
        var phase10OptimalStrategy = GetPhase10OptimalStrategy(characteristics.FileExtension);
        if (!string.IsNullOrEmpty(phase10OptimalStrategy) && phase10OptimalStrategy != recommendation.StrategyName)
        {
            // 원래 추천 전략을 대안으로 보존
            recommendation.Alternatives.Insert(0, new AlternativeStrategy
            {
                StrategyName = recommendation.StrategyName,
                Confidence = recommendation.Confidence,
                Reasoning = recommendation.Reasoning
            });
            
            recommendation.StrategyName = phase10OptimalStrategy;
            recommendation.Reasoning = $"Phase 10 optimization: {characteristics.FileExtension} files perform best with {phase10OptimalStrategy} strategy (evaluation-based)";
            recommendation.Confidence = 0.92; // 높은 신뢰도 (실증 데이터 기반)
        }
        
        // 특정 조건에서 추가 오버라이드
        if (characteristics.FileExtension == ".pdf" && characteristics.HasTables)
        {
            // PDF with tables - force Intelligent (기존 로직 유지)
            if (recommendation.StrategyName != "Intelligent")
            {
                recommendation.Alternatives.Insert(0, new AlternativeStrategy
                {
                    StrategyName = recommendation.StrategyName,
                    Confidence = recommendation.Confidence,
                    Reasoning = recommendation.Reasoning
                });
                
                recommendation.StrategyName = "Intelligent";
                recommendation.Reasoning = "Override: PDF with tables requires Intelligent strategy for structure preservation";
                recommendation.Confidence = 0.95;
            }
        }
        
        return recommendation;
    }

    /// <summary>
    /// 전략 메타데이터 초기화
    /// </summary>
    private Dictionary<string, IChunkingStrategyMetadata> InitializeStrategyMetadata()
    {
        var metadata = new Dictionary<string, IChunkingStrategyMetadata>();
        
        // Smart 전략
        metadata["Smart"] = new ChunkingStrategyMetadata
        {
            StrategyName = "Smart",
            Description = "Advanced sentence-boundary aware chunking with 70% minimum completeness guarantee. Never breaks sentences in the middle.",
            OptimalForDocumentTypes = new[] { "Legal", "Medical", "Academic", "Q&A", "FAQ" },
            Strengths = new[] 
            { 
                "Guaranteed 70% chunk completeness",
                "Perfect sentence integrity",
                "Smart overlap with context preservation",
                "High RAG retrieval quality"
            },
            Limitations = new[] 
            { 
                "Slightly slower processing",
                "May create uneven chunk sizes"
            },
            RecommendedScenarios = new[] 
            { 
                "RAG systems requiring high accuracy",
                "Q&A applications",
                "Legal/medical document processing",
                "Customer-facing content"
            },
            SelectionHints = new[] 
            { 
                "sentence integrity", "completeness", "accuracy critical",
                "legal", "medical", "compliance"
            },
            PriorityScore = 90,
            Performance = new PerformanceCharacteristics
            {
                Speed = 4,
                Quality = 5,
                MemoryEfficiency = 4,
                RequiresLLM = false
            }
        };
        
        // Intelligent 전략
        metadata["Intelligent"] = new ChunkingStrategyMetadata
        {
            StrategyName = "Intelligent",
            Description = "Structure-aware chunking that preserves document formatting, headers, code blocks, and tables. Optimized for technical documentation.",
            OptimalForDocumentTypes = new[] { "Technical", "Markdown", "Code", "Documentation" },
            Strengths = new[] 
            { 
                "Preserves document structure",
                "Maintains code blocks intact",
                "Table and list preservation",
                "Markdown-aware processing"
            },
            Limitations = new[] 
            { 
                "May break sentences",
                "No completeness guarantee"
            },
            RecommendedScenarios = new[] 
            { 
                "Technical documentation",
                "API documentation",
                "Code-heavy content",
                "Structured markdown files"
            },
            SelectionHints = new[] 
            { 
                "markdown", "code blocks", "technical", "documentation",
                "tables", "structured content"
            },
            PriorityScore = 85,
            Performance = new PerformanceCharacteristics
            {
                Speed = 3,
                Quality = 4,
                MemoryEfficiency = 4,
                RequiresLLM = true
            }
        };
        
        // Semantic 전략
        metadata["Semantic"] = new ChunkingStrategyMetadata
        {
            StrategyName = "Semantic",
            Description = "Meaning-based chunking that groups related concepts together. Best for narrative and conceptual content.",
            OptimalForDocumentTypes = new[] { "Narrative", "Essay", "Article", "Report" },
            Strengths = new[] 
            { 
                "Maintains semantic coherence",
                "Groups related concepts",
                "Good for long-form content",
                "Natural topic boundaries"
            },
            Limitations = new[] 
            { 
                "Requires semantic analysis",
                "Variable chunk sizes"
            },
            RecommendedScenarios = new[] 
            { 
                "Blog posts and articles",
                "Research papers",
                "Narrative documents",
                "Conceptual content"
            },
            SelectionHints = new[] 
            { 
                "narrative", "article", "essay", "semantic",
                "concepts", "topics"
            },
            PriorityScore = 75,
            Performance = new PerformanceCharacteristics
            {
                Speed = 3,
                Quality = 4,
                MemoryEfficiency = 3,
                RequiresLLM = false
            }
        };
        
        // Paragraph 전략
        metadata["Paragraph"] = new ChunkingStrategyMetadata
        {
            StrategyName = "Paragraph",
            Description = "Simple paragraph-based chunking. Fast and efficient for well-structured documents.",
            OptimalForDocumentTypes = new[] { "Book", "Novel", "Simple Text" },
            Strengths = new[] 
            { 
                "Fast processing",
                "Preserves natural paragraphs",
                "Low resource usage",
                "Predictable results"
            },
            Limitations = new[] 
            { 
                "May create very small/large chunks",
                "No semantic awareness"
            },
            RecommendedScenarios = new[] 
            { 
                "Books and novels",
                "Simple text documents",
                "Well-paragraphed content",
                "High-volume processing"
            },
            SelectionHints = new[] 
            { 
                "paragraph", "book", "novel", "simple",
                "fast processing", "high volume"
            },
            PriorityScore = 60,
            Performance = new PerformanceCharacteristics
            {
                Speed = 5,
                Quality = 3,
                MemoryEfficiency = 5,
                RequiresLLM = false
            }
        };
        
        // FixedSize 전략
        metadata["FixedSize"] = new ChunkingStrategyMetadata
        {
            StrategyName = "FixedSize",
            Description = "Token-based fixed size chunking. Baseline strategy for consistent chunk sizes.",
            OptimalForDocumentTypes = new[] { "Log", "Data", "Uniform" },
            Strengths = new[] 
            { 
                "Predictable chunk sizes",
                "Fast processing",
                "Memory efficient",
                "Good for embeddings"
            },
            Limitations = new[] 
            { 
                "Breaks semantic units",
                "No structure awareness"
            },
            RecommendedScenarios = new[] 
            { 
                "Log files",
                "Data processing",
                "Uniform content",
                "Baseline testing"
            },
            SelectionHints = new[] 
            { 
                "fixed", "uniform", "logs", "data",
                "baseline", "consistent size"
            },
            PriorityScore = 50,
            Performance = new PerformanceCharacteristics
            {
                Speed = 5,
                Quality = 2,
                MemoryEfficiency = 5,
                RequiresLLM = false
            }
        };
        
        // Phase 10: 메모리 최적화된 Intelligent 전략
        metadata["MemoryOptimizedIntelligent"] = new ChunkingStrategyMetadata
        {
            StrategyName = "MemoryOptimizedIntelligent",
            Description = "Memory-optimized intelligent chunking with 50% reduced memory usage through object pooling and struct-based processing.",
            OptimalForDocumentTypes = new[] { "Large Documents", "Technical", "Markdown", "Memory-constrained environments" },
            Strengths = new[] 
            { 
                "50% lower memory usage",
                "Object pooling optimization",
                "Struct-based semantic units",
                "Streaming processing",
                "Structure preservation"
            },
            Limitations = new[] 
            { 
                "Slightly reduced feature set",
                "No complex semantic analysis"
            },
            RecommendedScenarios = new[] 
            { 
                "Large document processing",
                "Memory-constrained servers",
                "Batch processing scenarios",
                "High-throughput applications"
            },
            PriorityScore = 88,
            Performance = new PerformanceCharacteristics
            {
                Speed = 4,
                Quality = 4,
                MemoryEfficiency = 5,
                RequiresLLM = false
            }
        };
        
        return metadata;
    }

    // Helper methods for document analysis
    private bool DetectMarkdownHeaders(string content) =>
        content.Contains("\n#") || content.StartsWith("#");
    
    private bool DetectCodeBlocks(string content) =>
        content.Contains("```") || content.Contains("~~~");
    
    private bool DetectTables(string content) =>
        content.Contains(" | ") && content.Contains("\n|");
    
    private bool DetectLists(string content) =>
        content.Contains("\n- ") || content.Contains("\n* ") ||
        content.Contains("\n1. ") || content.Contains("\n1)");

    private bool DetectMathFormulas(string content) =>
        content.Contains("$$") || content.Contains("\\[") ||
        content.Contains("\\begin{equation}");

    /// <summary>
    /// Phase 15: 강화된 번호 체계 섹션 감지
    /// 다양한 번호 패턴과 계층 구조를 인식
    /// </summary>
    private bool DetectNumberedSections(string content)
    {
        var lines = content.Split('\n');
        int numberedLines = 0;
        int hierarchicalLines = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // 기본 번호 패턴: 1., 2., 3. 또는 1), 2), 3)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+[\.\)]\s+\w+"))
            {
                numberedLines++;
            }

            // 계층적 번호 패턴: 1.1, 1.2, 2.1, 2.2 등
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+\.\d+[\.\)]*\s+\w+"))
            {
                hierarchicalLines++;
                numberedLines++; // 계층적 번호도 번호 라인으로 카운트
            }

            // 로마 숫자 패턴: I., II., III., IV. 등
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[IVX]+[\.\)]\s+\w+"))
            {
                numberedLines++;
            }

            // 알파벳 패턴: a), b), c) 또는 A), B), C)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[a-zA-Z][\.\)]\s+\w+"))
            {
                numberedLines++;
            }

            // 한국어 번호 패턴: 가., 나., 다. 또는 (1), (2), (3)
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[가-힣][\.\)]\s+\w+") ||
                System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\(\d+\)\s+\w+"))
            {
                numberedLines++;
            }
        }

        // 계층적 구조가 발견되면 가중치 부여
        var structureWeight = hierarchicalLines > 0 ? 1.5 : 1.0;
        var effectiveNumberedLines = numberedLines * structureWeight;

        // 전체 라인의 8% 이상이 번호 체계를 가지거나, 계층 구조가 3개 이상이면 구조화된 문서로 판단
        var threshold = Math.Max(2, lines.Length * 0.08);
        return effectiveNumberedLines >= threshold || hierarchicalLines >= 3;
    }

    /// <summary>
    /// Phase 15: 강화된 구조화 요구사항 문서 감지
    /// 다양한 문서 구조 패턴과 키워드를 종합적으로 분석
    /// </summary>
    private bool DetectStructuredRequirements(string content)
    {
        // 핵심 요구사항 키워드
        var requirementKeywords = new[] {
            "요구사항", "변경사항", "항목", "변경", "프로그램", "시스템", "기능", "명세",
            "requirement", "change", "item", "specification", "feature", "function",
            "criteria", "policy", "rule", "standard", "guideline", "procedure"
        };

        // 문서 구조 키워드
        var structureKeywords = new[] {
            "section", "chapter", "part", "단락", "장", "절", "조", "항",
            "objective", "목적", "목표", "범위", "scope", "overview", "개요"
        };

        // 상태/프로세스 키워드
        var processKeywords = new[] {
            "status", "state", "process", "workflow", "step", "phase",
            "상태", "과정", "단계", "절차", "흐름", "처리"
        };

        var requirementKeywordCount = requirementKeywords.Count(keyword =>
            content.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        var structureKeywordCount = structureKeywords.Count(keyword =>
            content.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        var processKeywordCount = processKeywords.Count(keyword =>
            content.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        // 구조 표시자 확인 (강화)
        var checkboxMarkers = content.Contains("□") || content.Contains("▣") ||
                              content.Contains("☐") || content.Contains("☑") ||
                              content.Contains("✓") || content.Contains("✗");

        var numberingMarkers = content.Contains("No.") || content.Contains("항목") ||
                               content.Contains("#") || content.Contains("Item");

        var tableMarkers = content.Contains(" | ") && content.Contains("---");

        var bulletMarkers = content.Split('\n').Count(line =>
            line.Trim().StartsWith("- ") || line.Trim().StartsWith("* ") ||
            line.Trim().StartsWith("• ")) > 3;

        // 종합 점수 계산
        var score = 0;
        score += requirementKeywordCount * 2;  // 요구사항 키워드 가중치 높음
        score += structureKeywordCount;
        score += processKeywordCount;

        if (checkboxMarkers) score += 3;
        if (numberingMarkers) score += 2;
        if (tableMarkers) score += 2;
        if (bulletMarkers) score += 1;

        // 텍스트 밀도 기반 추가 검증
        var lines = content.Split('\n');
        var nonEmptyLines = lines.Count(line => !string.IsNullOrWhiteSpace(line));
        var averageLineLength = nonEmptyLines > 0 ?
            content.Length / (double)nonEmptyLines : 0;

        // 구조화된 문서는 보통 짧은 라인들로 구성됨
        if (averageLineLength < 50 && nonEmptyLines > 10) score += 2;

        return score >= 5; // 임계값을 5로 설정하여 더 정확한 감지
    }
    
    private string InferContentType(string content, string extension)
    {
        if (DetectCodeBlocks(content)) return "Technical";
        if (extension == ".md") return "Markdown";
        if (content.Length > 1000 && CountParagraphs(content) > 3) return "Narrative";
        return "General";
    }
    
    private string DetectLanguage(string content)
    {
        // Simple language detection
        if (content.Any(c => c >= 0xAC00 && c <= 0xD7AF)) return "Korean";
        if (content.Any(c => c >= 0x4E00 && c <= 0x9FFF)) return "Chinese";
        if (content.Any(c => c >= 0x3040 && c <= 0x309F)) return "Japanese";
        return "English";
    }
    
    private string InferDomain(string content)
    {
        var lowerContent = content.ToLowerInvariant();
        
        if (lowerContent.Contains("patient") || lowerContent.Contains("diagnosis") || 
            lowerContent.Contains("treatment")) return "Medical";
        
        if (lowerContent.Contains("pursuant") || lowerContent.Contains("whereas") || 
            lowerContent.Contains("jurisdiction")) return "Legal";
        
        if (lowerContent.Contains("function") || lowerContent.Contains("class") || 
            lowerContent.Contains("api")) return "Technical";
        
        if (lowerContent.Contains("revenue") || lowerContent.Contains("profit") || 
            lowerContent.Contains("market")) return "Business";
        
        if (lowerContent.Contains("research") || lowerContent.Contains("hypothesis") || 
            lowerContent.Contains("methodology")) return "Academic";
        
        return "General";
    }
    
    private double CalculateAverageSentenceLength(string content)
    {
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length == 0) return 0;
        
        var totalWords = sentences.Sum(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        return (double)totalWords / sentences.Length;
    }
    
    private int CountParagraphs(string content) =>
        content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length;
    
    private int CalculateStructureComplexity(DocumentCharacteristics characteristics)
    {
        var complexity = 0;

        if (characteristics.HasMarkdownHeaders) complexity += 2;
        if (characteristics.HasCodeBlocks) complexity += 2;
        if (characteristics.HasTables) complexity += 2;
        if (characteristics.HasLists) complexity += 1;
        if (characteristics.HasMathFormulas) complexity += 2;
        if (characteristics.HasNumberedSections) complexity += 3; // 구조화 문서 강한 지표
        if (characteristics.HasStructuredRequirements) complexity += 3; // 요구사항 문서 강한 지표
        if (characteristics.ParagraphCount > 10) complexity += 1;

        return Math.Min(complexity, 10);
    }
    
    private int EstimateTokenCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length * 4 / 3;
    
    /// <summary>
    /// Phase 10: 파일 형식별 최적 전략 매핑 (Phase 9 평가 결과 기반)
    /// RAG 품질 평가를 통해 검증된 파일 형식별 최적 전략 반환
    /// </summary>
    private string GetPhase10OptimalStrategy(string fileExtension)
    {
        // Phase 10: 메모리 효율성을 고려한 전략 선택
        // 대용량 파일이나 메모리 제약 환경에서는 메모리 최적화 전략 우선
        var isMemoryConstrained = CheckMemoryConstraints();
        
        return fileExtension.ToLowerInvariant() switch
        {
            ".pdf" => "Semantic",      // Phase 9: PDF는 Semantic이 최고 성능
            ".docx" => isMemoryConstrained ? "MemoryOptimizedIntelligent" : "Intelligent",  // 메모리 제약 시 최적화 버전
            ".doc" => isMemoryConstrained ? "MemoryOptimizedIntelligent" : "Intelligent",   
            ".md" => "Semantic",       // Phase 9: Markdown은 Semantic이 우수
            ".txt" => "Semantic",      // 일반 텍스트는 Semantic 전략 적합
            ".xlsx" => isMemoryConstrained ? "MemoryOptimizedIntelligent" : "Intelligent",  // 구조화된 데이터
            ".xls" => isMemoryConstrained ? "MemoryOptimizedIntelligent" : "Intelligent",   
            ".pptx" => isMemoryConstrained ? "MemoryOptimizedIntelligent" : "Intelligent",  // 프레젠테이션
            ".ppt" => isMemoryConstrained ? "MemoryOptimizedIntelligent" : "Intelligent",
            ".html" => "Semantic",     // HTML은 의미적 분할이 적합
            ".json" => "Smart",        // JSON은 Smart 전략으로 유연하게 처리
            ".csv" => "FixedSize",     // CSV는 고정 크기가 데이터 무결성에 유리
            _ => string.Empty          // 매핑 없음 - 기본 LLM 추천 사용
        };
    }
    
    /// <summary>
    /// 현재 메모리 상황을 체크하여 메모리 최적화가 필요한지 판단
    /// </summary>
    private bool CheckMemoryConstraints()
    {
        try
        {
            // 현재 메모리 사용량 체크
            var currentMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024;
            
            // 사용 가능한 물리 메모리가 낮거나 현재 메모리 사용량이 높은 경우
            return currentMemoryMB > 500; // 500MB 이상 사용 중이면 메모리 최적화 전략 선택
        }
        catch
        {
            // 메모리 정보를 가져올 수 없는 경우 안전하게 최적화 전략 사용
            return true;
        }
    }
}

// Supporting classes
public class DocumentCharacteristics
{
    public string FileExtension { get; set; } = "";
    public string SampleContent { get; set; } = "";
    public int EstimatedTokenCount { get; set; }
    public bool HasMarkdownHeaders { get; set; }
    public bool HasCodeBlocks { get; set; }
    public bool HasTables { get; set; }
    public bool HasLists { get; set; }
    public bool HasMathFormulas { get; set; }
    public bool HasNumberedSections { get; set; }
    public bool HasStructuredRequirements { get; set; }
    public string ContentType { get; set; } = "General";
    public string Language { get; set; } = "English";
    public string Domain { get; set; } = "General";
    public double AverageSentenceLength { get; set; }
    public int ParagraphCount { get; set; }
    public int StructureComplexity { get; set; }
}

public class LLMStrategyRecommendation
{
    public string StrategyName { get; set; } = "Smart";
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = "";
    public List<AlternativeStrategy> Alternatives { get; set; } = new();
}

public class AlternativeStrategy
{
    public string StrategyName { get; set; } = "";
    public double Confidence { get; set; }
    public string Reasoning { get; set; } = "";
}

public class ChunkingStrategyMetadata : IChunkingStrategyMetadata
{
    public string StrategyName { get; set; } = "";
    public string Description { get; set; } = "";
    public IEnumerable<string> OptimalForDocumentTypes { get; set; } = new List<string>();
    public IEnumerable<string> Strengths { get; set; } = new List<string>();
    public IEnumerable<string> Limitations { get; set; } = new List<string>();
    public IEnumerable<string> RecommendedScenarios { get; set; } = new List<string>();
    public IEnumerable<string> SelectionHints { get; set; } = new List<string>();
    public int PriorityScore { get; set; }
    public PerformanceCharacteristics Performance { get; set; } = new();
}