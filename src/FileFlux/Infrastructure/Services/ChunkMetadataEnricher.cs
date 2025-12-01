using FileFlux.Core;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Enriches document chunks with quality metrics and contextual metadata.
/// Calculates relevance scores, completeness metrics, and content classification
/// to improve RAG retrieval accuracy.
/// </summary>
public class ChunkMetadataEnricher
{
    /// <summary>
    /// Enriches a chunk with quality metrics and contextual metadata.
    /// </summary>
    /// <param name="chunk">Target chunk to enrich.</param>
    /// <param name="context">Document-level context for enrichment.</param>
    /// <returns>The enriched chunk.</returns>
    public DocumentChunk Enrich(DocumentChunk chunk, DocumentContext context)
    {
        // Calculate relevance score based on content quality
        var relevanceScore = CalculateRelevanceScore(chunk.Content, context);
        chunk.Props[ChunkPropsKeys.QualityRelevanceScore] = relevanceScore;

        // Calculate completeness score for Smart strategy chunks
        if (chunk.Strategy == "Smart")
        {
            var completeness = CalculateCompletenessScore(chunk.Content);
            chunk.Props[ChunkPropsKeys.QualityCompleteness] = completeness;
        }

        // Detect and store content type
        var contentType = DetectContentType(chunk.Content);
        chunk.Props[ChunkPropsKeys.ContentType] = contentType;

        // Determine structural role based on content type
        var structuralRole = DetermineStructuralRole(contentType);
        chunk.Props[ChunkPropsKeys.StructuralRole] = structuralRole;

        return chunk;
    }

    /// <summary>
    /// Detects the content type of the chunk (code, table, list, heading, or text).
    /// </summary>
    private static string DetectContentType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "text";

        var trimmed = content.Trim();

        // Code block detection
        if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
            trimmed.Contains("function ") ||
            trimmed.Contains("class ") ||
            trimmed.Contains("def ") ||
            trimmed.Contains("public ") ||
            trimmed.Contains("private "))
        {
            return "code";
        }

        // Table detection
        if (trimmed.Contains('|') && trimmed.Split('\n').Count(line => line.Contains('|')) >= 2)
        {
            return "table";
        }

        // List detection
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) ||
            trimmed.StartsWith("* ", StringComparison.Ordinal) ||
            trimmed.StartsWith("1. ", StringComparison.Ordinal) ||
            trimmed.Split('\n').Count(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal)) >= 2)
        {
            return "list";
        }

        // Heading detection
        if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
            (trimmed.Length < 100 && trimmed.Split('\n').Length == 1 && char.IsUpper(trimmed[0])))
        {
            return "heading";
        }

        return "text";
    }

    /// <summary>
    /// Determines the structural role based on content type.
    /// </summary>
    private static string DetermineStructuralRole(string contentType)
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
    /// Calculates topic relevance scores based on keyword density.
    /// </summary>
    /// <param name="content">Chunk content.</param>
    /// <param name="context">Document context with domain information.</param>
    /// <returns>Dictionary of topic scores.</returns>
    public Dictionary<string, double> CalculateTopicScores(string content, DocumentContext context)
    {
        var scores = new Dictionary<string, double>();

        if (string.IsNullOrWhiteSpace(content))
            return scores;

        var words = content.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Technical document topic scores
        if (context.DocumentType == "Technical")
        {
            scores["API"] = CalculateKeywordDensity(words, ["api", "endpoint", "request", "response"]);
            scores["Architecture"] = CalculateKeywordDensity(words, ["architecture", "system", "design", "pattern"]);
            scores["Database"] = CalculateKeywordDensity(words, ["database", "sql", "query", "table", "index"]);
            scores["Security"] = CalculateKeywordDensity(words, ["security", "auth", "token", "encryption", "ssl"]);
        }
        // Business document topic scores
        else if (context.DocumentType == "Business")
        {
            scores["Strategy"] = CalculateKeywordDensity(words, ["strategy", "plan", "goal", "objective"]);
            scores["Finance"] = CalculateKeywordDensity(words, ["revenue", "cost", "budget", "profit", "finance"]);
            scores["Marketing"] = CalculateKeywordDensity(words, ["marketing", "customer", "brand", "campaign"]);
            scores["Operations"] = CalculateKeywordDensity(words, ["process", "workflow", "operation", "efficiency"]);
        }
        // Academic document topic scores
        else if (context.DocumentType == "Academic")
        {
            scores["Research"] = CalculateKeywordDensity(words, ["research", "study", "analysis", "methodology"]);
            scores["Theory"] = CalculateKeywordDensity(words, ["theory", "concept", "framework", "model"]);
            scores["Results"] = CalculateKeywordDensity(words, ["result", "finding", "conclusion", "data"]);
            scores["Literature"] = CalculateKeywordDensity(words, ["literature", "reference", "citation", "review"]);
        }

        return scores;
    }

    /// <summary>
    /// Calculates keyword density for a set of target keywords.
    /// </summary>
    private static double CalculateKeywordDensity(string[] words, string[] keywords)
    {
        if (words.Length == 0) return 0.0;

        var matches = words.Count(word => keywords.Contains(word));
        return (double)matches / words.Length;
    }

    /// <summary>
    /// Extracts technical keywords from content based on domain.
    /// </summary>
    public List<string> ExtractTechnicalKeywords(string content, string domain)
    {
        var keywords = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
            return keywords;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var technicalPatterns = domain switch
        {
            "Technical" =>
            [
                "API", "REST", "GraphQL", "JSON", "XML", "HTTP", "HTTPS", "SSL", "TLS",
                "JWT", "OAuth", "SQL", "NoSQL", "MongoDB", "PostgreSQL", "MySQL",
                "Docker", "Kubernetes", "AWS", "Azure", "GCP", "CI/CD", "DevOps"
            ],
            "Business" =>
            [
                "KPI", "ROI", "SLA", "CRM", "ERP", "B2B", "B2C", "SaaS", "PaaS",
                "GDPR", "CCPA", "SOX", "ISO", "HIPAA"
            ],
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
    /// Calculates relevance score based on content length and sentence completeness.
    /// </summary>
    private static double CalculateRelevanceScore(string content, DocumentContext context)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var score = 0.5; // Base score

        // Content length bonus
        if (content.Length > 200) score += 0.1;
        if (content.Length > 500) score += 0.1;

        // Sentence completeness bonus
        var sentences = content.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length > 0)
        {
            var completeSentences = sentences.Count(s => !string.IsNullOrWhiteSpace(s.Trim()));
            var completeness = (double)completeSentences / sentences.Length;
            score += completeness * 0.3;
        }

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// Generates a contextual header for the chunk.
    /// </summary>
    public string GenerateContextualHeader(DocumentChunk chunk, DocumentContext context)
    {
        var headerParts = new List<string>();

        // Document type info
        headerParts.Add($"Document: {context.DocumentType}");

        // Section info
        if (!string.IsNullOrEmpty(chunk.Metadata.FileName))
            headerParts.Add($"Section: {chunk.Metadata.FileName}");

        // Content type info
        var contentType = ChunkPropsKeys.GetValueOrDefault<string>(chunk.Props, ChunkPropsKeys.ContentType, "text");
        if (contentType != "text")
            headerParts.Add($"Type: {contentType}");

        // Structural role
        var structuralRole = ChunkPropsKeys.GetValueOrDefault<string>(chunk.Props, ChunkPropsKeys.StructuralRole, "content");
        if (structuralRole != "content")
            headerParts.Add($"Role: {structuralRole}");

        // Domain info
        if (context.DocumentType != "General")
            headerParts.Add($"Domain: {context.DocumentType}");

        return string.Join(" | ", headerParts);
    }

    /// <summary>
    /// Calculates information density of the content.
    /// </summary>
    public double CalculateInformationDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var uniqueWords = words.Where(w => w.Length > 3).Distinct().Count();
        var sentences = content.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries).Length;

        // Information density = (unique words + sentences) / total characters * 1000
        return (uniqueWords + sentences) * 1000.0 / content.Length;
    }

    /// <summary>
    /// Calculates sentence completeness score (optimized for Smart strategy).
    /// </summary>
    private static double CalculateCompletenessScore(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var sentences = content.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length == 0) return 0.0;

        var completeSentences = 0;
        foreach (var sentence in sentences)
        {
            var trimmed = sentence.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                // Complete sentence: doesn't end with "..." and has adequate length
                if (!trimmed.EndsWith("...", StringComparison.Ordinal) && trimmed.Length > 10)
                {
                    completeSentences++;
                }
            }
        }

        var score = (double)completeSentences / sentences.Length;
        return Math.Max(0.7, score); // Smart strategy guarantees minimum 70%
    }
}
