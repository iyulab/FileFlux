using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// 문서 타입별 최적화 구현 - 연구 기반 파라미터 자동 설정
/// </summary>
public class DocumentTypeOptimizer : IDocumentTypeOptimizer
{
    private readonly Dictionary<DocumentCategory, PerformanceMetrics> _performanceMetrics;

    public DocumentTypeOptimizer()
    {
        _performanceMetrics = InitializePerformanceMetrics();
    }

    public async Task<DocumentTypeInfo> DetectDocumentTypeAsync(
        string content,
        DocumentMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // 비동기 컨텍스트 유지

        var typeInfo = new DocumentTypeInfo
        {
            Language = DetectLanguage(content),
            AverageSentenceLength = CalculateAverageSentenceLength(content),
            ComplexityScore = CalculateComplexity(content),
            StructuralElements = DetectStructuralElements(content),
            Characteristics = new Dictionary<string, object>()
        };

        // 키워드 기반 카테고리 감지
        var (category, confidence, subType) = DetectCategoryByContent(content, metadata);
        typeInfo.Category = category;
        typeInfo.Confidence = confidence;
        typeInfo.SubType = subType;

        // 특성 추가
        AddCharacteristics(typeInfo, content);

        return typeInfo;
    }

    public ChunkingOptions GetOptimalOptions(DocumentTypeInfo documentType)
    {
        var metrics = _performanceMetrics[documentType.Category];

        var options = new ChunkingOptions
        {
            Strategy = metrics.RecommendedStrategy,
            MaxChunkSize = (metrics.OptimalTokenRange.Min + metrics.OptimalTokenRange.Max) / 2,
            OverlapSize = (int)(((metrics.OptimalOverlapRange.Min + metrics.OptimalOverlapRange.Max) / 2)
                * ((metrics.OptimalTokenRange.Min + metrics.OptimalTokenRange.Max) / 2) / 100)
        };

        // 복잡도에 따른 조정
        if (documentType.ComplexityScore > 0.7)
        {
            options.MaxChunkSize = metrics.OptimalTokenRange.Min; // 복잡한 문서는 작은 청크
            options.OverlapSize = (int)(metrics.OptimalOverlapRange.Max * options.MaxChunkSize / 100);
        }
        else if (documentType.ComplexityScore < 0.3)
        {
            options.MaxChunkSize = metrics.OptimalTokenRange.Max; // 단순한 문서는 큰 청크
            options.OverlapSize = (int)(metrics.OptimalOverlapRange.Min * options.MaxChunkSize / 100);
        }

        // 구조적 요소에 따른 전략 조정
        AdjustStrategyByStructure(options, documentType);

        // 전략별 옵션은 향후 구현 예정
        // TODO: ChunkingOptions에 StrategyOptions 추가 후 구현

        return options;
    }

    public async Task<ChunkingOptions> GetOptimalOptionsAsync(
        string content,
        DocumentMetadata? metadata,
        CancellationToken cancellationToken = default)
    {
        var documentType = await DetectDocumentTypeAsync(content, metadata, cancellationToken);
        return GetOptimalOptions(documentType);
    }

    public Dictionary<DocumentCategory, PerformanceMetrics> GetPerformanceMetrics()
    {
        return new Dictionary<DocumentCategory, PerformanceMetrics>(_performanceMetrics);
    }

    private Dictionary<DocumentCategory, PerformanceMetrics> InitializePerformanceMetrics()
    {
        return new Dictionary<DocumentCategory, PerformanceMetrics>
        {
            [DocumentCategory.Technical] = new PerformanceMetrics
            {
                OptimalTokenRange = (500, 800),
                OptimalOverlapRange = (20, 30),
                ExpectedF1Score = 0.85,
                ExpectedAccuracy = 0.82,
                ProcessingSpeedFactor = 1.0,
                RecommendedStrategy = ChunkingStrategies.Auto,
                OptimizationHints = new List<string>
                {
                    "Preserve code blocks intact",
                    "Maintain API documentation structure",
                    "Keep examples with explanations"
                }
            },
            [DocumentCategory.Legal] = new PerformanceMetrics
            {
                OptimalTokenRange = (300, 500),
                OptimalOverlapRange = (15, 25),
                ExpectedF1Score = 0.88,
                ExpectedAccuracy = 0.85,
                ProcessingSpeedFactor = 0.9,
                RecommendedStrategy = ChunkingStrategies.Semantic,
                OptimizationHints = new List<string>
                {
                    "Preserve clause boundaries",
                    "Maintain legal citations",
                    "Keep definitions with context"
                }
            },
            [DocumentCategory.Academic] = new PerformanceMetrics
            {
                OptimalTokenRange = (200, 400),
                OptimalOverlapRange = (25, 35),
                ExpectedF1Score = 0.90,
                ExpectedAccuracy = 0.87,
                ProcessingSpeedFactor = 0.8,
                RecommendedStrategy = ChunkingStrategies.Auto,
                OptimizationHints = new List<string>
                {
                    "Preserve citations and references",
                    "Maintain methodology sections",
                    "Keep conclusions with supporting data"
                }
            },
            [DocumentCategory.Financial] = new PerformanceMetrics
            {
                OptimalTokenRange = (400, 600),
                OptimalOverlapRange = (20, 30),
                ExpectedF1Score = 0.86,
                ExpectedAccuracy = 0.83,
                ProcessingSpeedFactor = 1.1,
                RecommendedStrategy = ChunkingStrategies.Auto,
                OptimizationHints = new List<string>
                {
                    "Use element-based chunking for tables",
                    "Preserve numerical data integrity",
                    "Maintain temporal context"
                }
            },
            [DocumentCategory.Medical] = new PerformanceMetrics
            {
                OptimalTokenRange = (350, 550),
                OptimalOverlapRange = (20, 30),
                ExpectedF1Score = 0.87,
                ExpectedAccuracy = 0.84,
                ProcessingSpeedFactor = 0.95,
                RecommendedStrategy = ChunkingStrategies.Semantic,
                OptimizationHints = new List<string>
                {
                    "Preserve medical terminology context",
                    "Maintain patient information boundaries",
                    "Keep treatment protocols together"
                }
            },
            [DocumentCategory.Business] = new PerformanceMetrics
            {
                OptimalTokenRange = (400, 700),
                OptimalOverlapRange = (15, 25),
                ExpectedF1Score = 0.83,
                ExpectedAccuracy = 0.80,
                ProcessingSpeedFactor = 1.2,
                RecommendedStrategy = ChunkingStrategies.Paragraph,
                OptimizationHints = new List<string>
                {
                    "Maintain executive summary integrity",
                    "Preserve bullet points and lists",
                    "Keep recommendations with rationale"
                }
            },
            [DocumentCategory.Creative] = new PerformanceMetrics
            {
                OptimalTokenRange = (500, 900),
                OptimalOverlapRange = (10, 20),
                ExpectedF1Score = 0.78,
                ExpectedAccuracy = 0.75,
                ProcessingSpeedFactor = 1.3,
                RecommendedStrategy = ChunkingStrategies.Paragraph,
                OptimizationHints = new List<string>
                {
                    "Preserve narrative flow",
                    "Maintain character and plot continuity",
                    "Keep dialogue sections intact"
                }
            },
            [DocumentCategory.General] = new PerformanceMetrics
            {
                OptimalTokenRange = (400, 600),
                OptimalOverlapRange = (15, 25),
                ExpectedF1Score = 0.80,
                ExpectedAccuracy = 0.77,
                ProcessingSpeedFactor = 1.0,
                RecommendedStrategy = ChunkingStrategies.Auto,
                OptimizationHints = new List<string>
                {
                    "Use adaptive chunking",
                    "Detect and preserve structure",
                    "Balance size and coherence"
                }
            }
        };
    }

    private (DocumentCategory Category, double Confidence, string? SubType) DetectCategoryByContent(
        string content,
        DocumentMetadata? metadata)
    {
        var scores = new Dictionary<DocumentCategory, double>();

        // 키워드 기반 점수 계산
        scores[DocumentCategory.Technical] = ScoreTechnical(content);
        scores[DocumentCategory.Legal] = ScoreLegal(content);
        scores[DocumentCategory.Academic] = ScoreAcademic(content);
        scores[DocumentCategory.Financial] = ScoreFinancial(content);
        scores[DocumentCategory.Medical] = ScoreMedical(content);
        scores[DocumentCategory.Business] = ScoreBusiness(content);
        scores[DocumentCategory.Creative] = ScoreCreative(content);

        // 메타데이터 기반 보정
        if (metadata != null)
        {
            AdjustScoresByMetadata(scores, metadata);
        }

        // 최고 점수 카테고리 선택
        var bestCategory = scores.OrderByDescending(kvp => kvp.Value).First();

        // 신뢰도 계산 (최고 점수와 두 번째 점수의 차이)
        var sortedScores = scores.Values.OrderByDescending(s => s).ToList();
        var confidence = sortedScores.Count > 1
            ? Math.Min(1.0, (sortedScores[0] - sortedScores[1]) * 2 + sortedScores[0])
            : sortedScores[0];

        // 서브타입 결정
        string? subType = DetermineSubType(bestCategory.Key, content);

        // 점수가 너무 낮으면 General로 분류
        if (bestCategory.Value < 0.3)
        {
            return (DocumentCategory.General, 0.5, null);
        }

        return (bestCategory.Key, confidence, subType);
    }

    private double ScoreTechnical(string content)
    {
        var keywords = new[] { "code", "function", "api", "algorithm", "system", "software",
                               "implementation", "class", "method", "debug", "compile", "syntax" };
        return CalculateKeywordScore(content, keywords);
    }

    private double ScoreLegal(string content)
    {
        var keywords = new[] { "legal", "law", "contract", "agreement", "clause", "liability",
                               "court", "statute", "regulation", "compliance", "attorney", "pursuant" };
        return CalculateKeywordScore(content, keywords);
    }

    private double ScoreAcademic(string content)
    {
        var keywords = new[] { "research", "study", "hypothesis", "methodology", "abstract",
                               "conclusion", "literature", "citation", "reference", "analysis", "findings" };
        return CalculateKeywordScore(content, keywords);
    }

    private double ScoreFinancial(string content)
    {
        var keywords = new[] { "finance", "investment", "revenue", "profit", "market", "portfolio",
                               "asset", "equity", "dividend", "fiscal", "budget", "earnings" };
        return CalculateKeywordScore(content, keywords);
    }

    private double ScoreMedical(string content)
    {
        var keywords = new[] { "patient", "diagnosis", "treatment", "medical", "clinical", "symptom",
                               "disease", "therapy", "medication", "health", "physician", "hospital" };
        return CalculateKeywordScore(content, keywords);
    }

    private double ScoreBusiness(string content)
    {
        var keywords = new[] { "business", "strategy", "management", "market", "customer", "product",
                               "service", "growth", "performance", "stakeholder", "competitive" };
        return CalculateKeywordScore(content, keywords);
    }

    private double ScoreCreative(string content)
    {
        var keywords = new[] { "story", "character", "plot", "narrative", "creative", "artistic",
                               "design", "aesthetic", "inspiration", "imagination", "expression" };
        return CalculateKeywordScore(content, keywords);
    }

    private double CalculateKeywordScore(string content, string[] keywords)
    {
        var lowerContent = content.ToLower();
        var wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount == 0) return 0;

        var keywordCount = keywords.Sum(keyword =>
            Regex.Matches(lowerContent, $@"\b{Regex.Escape(keyword)}\b").Count);

        // 정규화된 점수 (0-1)
        return Math.Min(1.0, keywordCount / (double)wordCount * 100);
    }

    private void AdjustScoresByMetadata(Dictionary<DocumentCategory, double> scores, DocumentMetadata metadata)
    {
        // 파일 확장자 기반 조정
        if (!string.IsNullOrEmpty(metadata.FileType))
        {
            switch (metadata.FileType.ToLower())
            {
                case ".cs":
                case ".py":
                case ".js":
                case ".java":
                    scores[DocumentCategory.Technical] *= 1.5;
                    break;
                case ".docx":
                case ".doc":
                    scores[DocumentCategory.Business] *= 1.2;
                    break;
                case ".pdf":
                    scores[DocumentCategory.Academic] *= 1.1;
                    break;
            }
        }
    }

    private string? DetermineSubType(DocumentCategory category, string content)
    {
        return category switch
        {
            DocumentCategory.Technical => DetectTechnicalSubType(content),
            DocumentCategory.Legal => DetectLegalSubType(content),
            DocumentCategory.Academic => DetectAcademicSubType(content),
            _ => null
        };
    }

    private string DetectTechnicalSubType(string content)
    {
        if (content.Contains("API") || content.Contains("endpoint")) return "API Documentation";
        if (content.Contains("README") || content.Contains("Installation")) return "README";
        if (content.Contains("class") && content.Contains("method")) return "Code Documentation";
        return "Technical Documentation";
    }

    private string DetectLegalSubType(string content)
    {
        if (content.Contains("agreement") || content.Contains("contract")) return "Contract";
        if (content.Contains("patent")) return "Patent";
        if (content.Contains("compliance")) return "Compliance";
        return "Legal Document";
    }

    private string DetectAcademicSubType(string content)
    {
        if (content.Contains("abstract") && content.Contains("methodology")) return "Research Paper";
        if (content.Contains("thesis")) return "Thesis";
        if (content.Contains("review")) return "Literature Review";
        return "Academic Paper";
    }

    private string DetectLanguage(string content)
    {
        // 간단한 언어 감지 (실제로는 더 정교한 방법 필요)
        if (Regex.IsMatch(content, @"[\u3131-\uD79D]")) return "ko"; // 한글
        if (Regex.IsMatch(content, @"[\u4E00-\u9FFF]")) return "zh"; // 중국어
        if (Regex.IsMatch(content, @"[\u3040-\u309F\u30A0-\u30FF]")) return "ja"; // 일본어
        return "en"; // 기본 영어
    }

    private double CalculateAverageSentenceLength(string content)
    {
        var sentences = Regex.Split(content, @"[.!?]+");
        if (sentences.Length == 0) return 0;

        var totalWords = sentences.Sum(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        return totalWords / (double)sentences.Length;
    }

    private double CalculateComplexity(string content)
    {
        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;

        // 복잡도 요소들
        var avgWordLength = words.Average(w => w.Length);
        var avgSentenceLength = CalculateAverageSentenceLength(content);
        var uniqueWordRatio = words.Distinct().Count() / (double)words.Length;

        // 가중 평균으로 복잡도 계산
        var complexity = (avgWordLength / 10.0) * 0.3 +
                        (avgSentenceLength / 30.0) * 0.4 +
                        (1 - uniqueWordRatio) * 0.3;

        return Math.Min(1.0, Math.Max(0, complexity));
    }

    private List<DocumentStructuralElement> DetectStructuralElements(string content)
    {
        var elements = new List<DocumentStructuralElement>();

        // 헤더 감지
        var headers = Regex.Matches(content, @"^#{1,6}\s+.+$", RegexOptions.Multiline);
        if (headers.Count > 0)
        {
            elements.Add(new DocumentStructuralElement
            {
                Type = "Header",
                Count = headers.Count,
                AverageSize = headers.Average(m => m.Length),
                Importance = 0.9
            });
        }

        // 코드 블록 감지
        var codeBlocks = Regex.Matches(content, @"```[\s\S]*?```");
        if (codeBlocks.Count > 0)
        {
            elements.Add(new DocumentStructuralElement
            {
                Type = "CodeBlock",
                Count = codeBlocks.Count,
                AverageSize = codeBlocks.Average(m => m.Length),
                Importance = 0.8
            });
        }

        // 리스트 감지
        var lists = Regex.Matches(content, @"^\s*[-*+]\s+", RegexOptions.Multiline);
        if (lists.Count > 0)
        {
            elements.Add(new DocumentStructuralElement
            {
                Type = "List",
                Count = lists.Count,
                AverageSize = 50, // 예상 평균 크기
                Importance = 0.6
            });
        }

        // 테이블 감지
        var tables = Regex.Matches(content, @"^\|.*\|$", RegexOptions.Multiline);
        if (tables.Count > 3) // 최소 3줄 이상이어야 테이블로 간주
        {
            elements.Add(new DocumentStructuralElement
            {
                Type = "Table",
                Count = 1, // 간단히 1개로 계산
                AverageSize = tables.Sum(m => m.Length),
                Importance = 0.7
            });
        }

        return elements;
    }

    private void AddCharacteristics(DocumentTypeInfo typeInfo, string content)
    {
        typeInfo.Characteristics["WordCount"] = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        typeInfo.Characteristics["LineCount"] = content.Split('\n').Length;
        typeInfo.Characteristics["HasCode"] = content.Contains("```") || content.Contains("def ") || content.Contains("function ");
        typeInfo.Characteristics["HasTables"] = content.Contains('|') && content.Count(c => c == '|') > 5;
        typeInfo.Characteristics["HasLists"] = Regex.IsMatch(content, @"^\s*[-*+]\s+", RegexOptions.Multiline);
        typeInfo.Characteristics["HasHeaders"] = content.Contains('#') || content.Contains("Chapter") || content.Contains("Section");
    }

    private void AdjustStrategyByStructure(ChunkingOptions options, DocumentTypeInfo documentType)
    {
        var hasCode = documentType.StructuralElements.Any(e => e.Type == "CodeBlock");
        var hasTables = documentType.StructuralElements.Any(e => e.Type == "Table");
        var hasHeaders = documentType.StructuralElements.Any(e => e.Type == "Header");

        // 코드가 많으면 Intelligent 전략 선호
        if (hasCode && documentType.StructuralElements.First(e => e.Type == "CodeBlock").Count > 5)
        {
            options.Strategy = ChunkingStrategies.Auto;
        }

        // 테이블이 있으면 청크 크기 증가
        if (hasTables)
        {
            options.MaxChunkSize = (int)(options.MaxChunkSize * 1.5);
        }

        // 명확한 헤더 구조가 있으면 Semantic 전략 고려
        if (hasHeaders && documentType.StructuralElements.First(e => e.Type == "Header").Count > 10)
        {
            if (options.Strategy == ChunkingStrategies.Paragraph)
            {
                options.Strategy = ChunkingStrategies.Semantic;
            }
        }
    }

    private Dictionary<string, object> GetStrategySpecificOptions(
        DocumentTypeInfo documentType,
        PerformanceMetrics metrics)
    {
        var options = new Dictionary<string, object>
        {
            ["PreserveStructure"] = true,
            ["IncludeMetadata"] = true,
            ["QualityThreshold"] = 0.6
        };

        // 카테고리별 특별 옵션
        switch (documentType.Category)
        {
            case DocumentCategory.Technical:
                options["PreserveCodeBlocks"] = true;
                options["MaintainIndentation"] = true;
                break;

            case DocumentCategory.Legal:
                options["PreserveClauseBoundaries"] = true;
                options["MaintainCitations"] = true;
                break;

            case DocumentCategory.Academic:
                options["PreserveCitations"] = true;
                options["MaintainSectionStructure"] = true;
                break;

            case DocumentCategory.Financial:
                options["PreserveTables"] = true;
                options["MaintainNumericalContext"] = true;
                options["ElementBasedChunking"] = true;
                break;
        }

        return options;
    }
}
