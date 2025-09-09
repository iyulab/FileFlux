using FileFlux.Domain;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Context7 스타일 메타데이터 향상 서비스
/// RAG 검색 정확도를 극대화하는 고급 메타데이터 생성
/// </summary>
public class Context7MetadataEnricher
{
    /// <summary>
    /// 청크에 Context7 스타일 메타데이터 추가
    /// </summary>
    /// <param name="chunk">대상 청크</param>
    /// <param name="documentContext">문서 전체 컨텍스트</param>
    /// <returns>향상된 청크</returns>
    public DocumentChunk EnrichChunk(DocumentChunk chunk, DocumentContext documentContext)
    {
        // 콘텐츠 타입 자동 감지 및 분류
        chunk.ContentType = DetectContentType(chunk.Content);
        
        // 구조적 역할 자동 분류
        chunk.StructuralRole = DetermineStructuralRole(chunk.Content, chunk.ContentType);
        
        // 주제별 점수 계산
        chunk.ContextualScores = CalculateTopicScores(chunk.Content, documentContext);
        
        // 기술 키워드 추출
        chunk.TechnicalKeywords = ExtractTechnicalKeywords(chunk.Content, documentContext.DocumentDomain);
        
        // 문서 도메인별 관련성 점수
        chunk.RelevanceScore = CalculateRelevanceScore(chunk.Content, documentContext);
        
        // Context7 스타일 컨텍스트 헤더 생성
        chunk.ContextualHeader = GenerateContextualHeader(chunk, documentContext);
        
        // 정보 밀도 계산
        chunk.InformationDensity = CalculateInformationDensity(chunk.Content);
        
        // 청크 완성도 점수 (Smart 전략 특화)
        if (chunk.Strategy == "Smart")
        {
            var completeness = CalculateCompletenessScore(chunk.Content);
            chunk.ContextualScores["Completeness"] = completeness;
            chunk.Properties["CompletenessScore"] = completeness;
        }
        
        return chunk;
    }

    /// <summary>
    /// 콘텐츠 타입 자동 감지
    /// </summary>
    private string DetectContentType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "text";

        var trimmed = content.Trim();
        
        // 코드 블록 감지
        if (trimmed.StartsWith("```", StringComparison.Ordinal) || 
            trimmed.Contains("function ") || 
            trimmed.Contains("class ") ||
            trimmed.Contains("def ") ||
            trimmed.Contains("public ") ||
            trimmed.Contains("private "))
        {
            return "code";
        }
        
        // 표 감지
        if (trimmed.Contains('|') && trimmed.Split('\n').Count(line => line.Contains('|')) >= 2)
        {
            return "table";
        }
        
        // 리스트 감지
        if (trimmed.StartsWith("- ") || 
            trimmed.StartsWith("* ") || 
            trimmed.StartsWith("1. ") ||
            trimmed.Split('\n').Count(line => line.TrimStart().StartsWith("- ")) >= 2)
        {
            return "list";
        }
        
        // 제목 감지 
        if (trimmed.StartsWith("#") || 
            trimmed.Length < 100 && trimmed.Split('\n').Length == 1 && 
            char.IsUpper(trimmed[0]))
        {
            return "heading";
        }
        
        return "text";
    }

    /// <summary>
    /// 구조적 역할 결정
    /// </summary>
    private string DetermineStructuralRole(string content, string contentType)
    {
        return contentType switch
        {
            "heading" => "title",
            "code" => "code_block", 
            "table" => "table_content",
            "list" => "list_content",
            _ => "content"
        };
    }

    /// <summary>
    /// 주제별 점수 계산 - Context7 스타일
    /// </summary>
    private Dictionary<string, double> CalculateTopicScores(string content, DocumentContext context)
    {
        var scores = new Dictionary<string, double>();
        
        if (string.IsNullOrWhiteSpace(content))
            return scores;

        var words = content.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // 기술 문서 주제 점수
        if (context.DocumentDomain == "Technical")
        {
            scores["API"] = CalculateKeywordDensity(words, ["api", "endpoint", "request", "response"]);
            scores["Architecture"] = CalculateKeywordDensity(words, ["architecture", "system", "design", "pattern"]);
            scores["Database"] = CalculateKeywordDensity(words, ["database", "sql", "query", "table", "index"]);
            scores["Security"] = CalculateKeywordDensity(words, ["security", "auth", "token", "encryption", "ssl"]);
        }
        
        // 비즈니스 문서 주제 점수
        else if (context.DocumentDomain == "Business")
        {
            scores["Strategy"] = CalculateKeywordDensity(words, ["strategy", "plan", "goal", "objective"]);
            scores["Finance"] = CalculateKeywordDensity(words, ["revenue", "cost", "budget", "profit", "finance"]);
            scores["Marketing"] = CalculateKeywordDensity(words, ["marketing", "customer", "brand", "campaign"]);
            scores["Operations"] = CalculateKeywordDensity(words, ["process", "workflow", "operation", "efficiency"]);
        }
        
        // 학술 문서 주제 점수
        else if (context.DocumentDomain == "Academic")
        {
            scores["Research"] = CalculateKeywordDensity(words, ["research", "study", "analysis", "methodology"]);
            scores["Theory"] = CalculateKeywordDensity(words, ["theory", "concept", "framework", "model"]);
            scores["Results"] = CalculateKeywordDensity(words, ["result", "finding", "conclusion", "data"]);
            scores["Literature"] = CalculateKeywordDensity(words, ["literature", "reference", "citation", "review"]);
        }
        
        return scores;
    }

    /// <summary>
    /// 키워드 밀도 계산
    /// </summary>
    private double CalculateKeywordDensity(string[] words, string[] keywords)
    {
        if (words.Length == 0) return 0.0;
        
        var matches = words.Count(word => keywords.Contains(word));
        return (double)matches / words.Length;
    }

    /// <summary>
    /// 기술 키워드 추출
    /// </summary>
    private List<string> ExtractTechnicalKeywords(string content, string domain)
    {
        var keywords = new List<string>();
        
        if (string.IsNullOrWhiteSpace(content))
            return keywords;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        // 도메인별 기술 키워드 패턴
        var technicalPatterns = domain switch
        {
            "Technical" => new[] { 
                "API", "REST", "GraphQL", "JSON", "XML", "HTTP", "HTTPS", "SSL", "TLS",
                "JWT", "OAuth", "SQL", "NoSQL", "MongoDB", "PostgreSQL", "MySQL",
                "Docker", "Kubernetes", "AWS", "Azure", "GCP", "CI/CD", "DevOps"
            },
            "Business" => new[] {
                "KPI", "ROI", "SLA", "CRM", "ERP", "B2B", "B2C", "SaaS", "PaaS",
                "GDPR", "CCPA", "SOX", "ISO", "HIPAA"
            },
            _ => new[] { "API", "JSON", "HTTP" }
        };

        foreach (var word in words)
        {
            var cleanWord = word.Trim('(', ')', ',', '.', '!', '?', ';', ':');
            if (technicalPatterns.Contains(cleanWord, StringComparer.OrdinalIgnoreCase))
            {
                keywords.Add(cleanWord.ToUpperInvariant());
            }
        }

        return keywords.Distinct().ToList();
    }

    /// <summary>
    /// 관련성 점수 계산
    /// </summary>
    private double CalculateRelevanceScore(string content, DocumentContext context)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 0.5; // 기본 점수
        
        // 콘텐츠 길이 기반 점수 조정
        if (content.Length > 200) score += 0.1;
        if (content.Length > 500) score += 0.1;
        
        // 문장 완성도 기반 점수 조정
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length > 0)
        {
            var completeSentences = sentences.Count(s => !string.IsNullOrWhiteSpace(s.Trim()));
            var completeness = (double)completeSentences / sentences.Length;
            score += completeness * 0.3;
        }
        
        return Math.Min(1.0, score);
    }

    /// <summary>
    /// 컨텍스트 헤더 생성 - Context7 스타일
    /// </summary>
    private string GenerateContextualHeader(DocumentChunk chunk, DocumentContext context)
    {
        var headerParts = new List<string>();
        
        // 문서 타입 정보
        headerParts.Add($"Document: {context.DocumentType}");
        
        // 섹션 정보
        if (!string.IsNullOrEmpty(chunk.Section))
            headerParts.Add($"Section: {chunk.Section}");
        
        // 콘텐츠 타입 정보
        if (chunk.ContentType != "text")
            headerParts.Add($"Type: {chunk.ContentType}");
        
        // 구조적 역할
        if (chunk.StructuralRole != "content")
            headerParts.Add($"Role: {chunk.StructuralRole}");
        
        // 도메인 정보
        if (context.DocumentDomain != "General")
            headerParts.Add($"Domain: {context.DocumentDomain}");
        
        return string.Join(" | ", headerParts);
    }

    /// <summary>
    /// 정보 밀도 계산
    /// </summary>
    private double CalculateInformationDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Where(w => w.Length > 3).Distinct().Count();
        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).Length;
        
        // 정보 밀도 = (고유 단어 수 + 문장 수) / 총 문자 수 * 1000
        return (uniqueWords + sentences) * 1000.0 / content.Length;
    }

    /// <summary>
    /// 문장 완성도 점수 계산 (Smart 전략용)
    /// </summary>
    private double CalculateCompletenessScore(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length == 0) return 0.0;

        var completeSentences = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                // 완전한 문장 판별: "..." 로 끝나지 않고, 적절한 길이
                if (!trimmed.EndsWith("...") && trimmed.Length > 10)
                {
                    completeSentences++;
                }
            }
        }

        var score = (double)completeSentences / sentences.Length;
        return Math.Max(0.7, score); // Smart 전략은 최소 70% 보장
    }
}

/// <summary>
/// 문서 전체 컨텍스트 정보
/// </summary>
public class DocumentContext
{
    /// <summary>
    /// 문서 타입 (PDF, Word, etc.)
    /// </summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>
    /// 문서 도메인 (Technical, Business, Academic, etc.)
    /// </summary>
    public string DocumentDomain { get; set; } = "General";

    /// <summary>
    /// 문서 전체 키워드
    /// </summary>
    public List<string> GlobalKeywords { get; set; } = new();

    /// <summary>
    /// 문서 구조 정보
    /// </summary>
    public Dictionary<string, object> StructureInfo { get; set; } = new();

    /// <summary>
    /// 문서 메타데이터
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();
}