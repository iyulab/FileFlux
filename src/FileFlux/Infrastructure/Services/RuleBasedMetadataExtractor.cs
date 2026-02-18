using FileFlux.Core;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Globalization;

namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Rule-based metadata extractor (AI-free fallback).
/// Uses pattern matching and heuristics to extract basic metadata.
/// </summary>
public partial class RuleBasedMetadataExtractor
{
    private static readonly string[] s_paragraphSeparators = ["\n\n", "\r\n\r\n"];
    private readonly ILogger<RuleBasedMetadataExtractor> _logger;

    public RuleBasedMetadataExtractor(ILogger<RuleBasedMetadataExtractor>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RuleBasedMetadataExtractor>.Instance;
    }

    /// <summary>
    /// Extract metadata using rule-based patterns.
    /// </summary>
    public Task<IDictionary<string, object>> ExtractAsync(
        string content,
        MetadataSchema schema,
        CancellationToken cancellationToken = default)
    {
        LogStartingExtraction(_logger, schema);

        var metadata = schema switch
        {
            MetadataSchema.ProductManual => ExtractProductManualMetadata(content),
            MetadataSchema.TechnicalDoc => ExtractTechnicalDocMetadata(content),
            MetadataSchema.General => ExtractGeneralMetadata(content),
            MetadataSchema.Custom => ExtractGeneralMetadata(content),
            _ => new Dictionary<string, object>()
        };

        metadata["confidence"] = CalculateConfidence(metadata);
        metadata["extractionMethod"] = "rule-based";

        LogExtractionComplete(_logger, metadata.Count);

        return Task.FromResult<IDictionary<string, object>>(metadata);
    }

    /// <summary>
    /// Extract product manual metadata.
    /// </summary>
    private static Dictionary<string, object> ExtractProductManualMetadata(string content)
    {
        var metadata = new Dictionary<string, object>();

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        // Product name patterns
        var productPatterns = new[]
        {
            @"([A-Za-z0-9\s\-]+)\s+(Manual|User\s+Guide|Guide|Instructions)",
            @"^([A-Z][A-Za-z0-9\s\-]+)\s*\r?\n",  // Title line
            @"Product:\s*([A-Za-z0-9\s\-]+)",
            @"Model:\s*([A-Za-z0-9\s\-]+)"
        };

        foreach (var pattern in productPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata["productName"] = match.Groups[1].Value.Trim();
                break;
            }
        }

        // Company name patterns
        var companyPatterns = new[]
        {
            @"©\s*\d{4}\s+([A-Z][A-Za-z\s]+?)(?:\s+Inc\.|Corporation|Ltd\.|LLC|Co\.|,|$)",
            @"Copyright\s+(?:\d{4}\s+)?([A-Z][A-Za-z\s]+?)(?:\s+Inc\.|Corporation|Ltd\.|LLC|Co\.|,|$)",
            @"Manufacturer:\s*([A-Za-z\s]+)",
            @"Company:\s*([A-Za-z\s]+)"
        };

        foreach (var pattern in companyPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata["company"] = match.Groups[1].Value.Trim();
                break;
            }
        }

        // Version patterns
        var versionPatterns = new[]
        {
            @"(?:Version|Ver\.|v)\s*(\d+\.\d+(?:\.\d+)?)",
            @"(?:Firmware|Software)\s+(?:Version\s+)?(\d+\.\d+(?:\.\d+)?)",
            @"(?:iOS|Android|Windows)\s+(\d+(?:\.\d+)?)",
            @"Rev(?:ision)?\s*(\d+(?:\.\d+)?)"
        };

        foreach (var pattern in versionPatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                metadata["version"] = match.Groups[1].Value;
                break;
            }
        }

        // Release date patterns
        var datePatterns = new[]
        {
            @"(?:Released?|Published?|Date):\s*(\d{1,2}[\/-]\d{1,2}[\/-]\d{4})",
            @"(?:Released?|Published?|Date):\s*([A-Za-z]+\s+\d{1,2},?\s+\d{4})",
            @"(\d{4}-\d{2}-\d{2})"  // ISO format
        };

        foreach (var pattern in datePatterns)
        {
            var match = Regex.Match(content, pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                if (DateTime.TryParse(match.Groups[1].Value, out var date))
                {
                    metadata["releaseDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                break;
            }
        }

        // Extract topics from section headers
        var topics = ExtractTopicsFromHeaders(content);
        if (topics.Count != 0)
        {
            metadata["topics"] = topics.ToArray();
        }

        // Extract keywords
        var keywords = ExtractKeywords(content, isManual: true);
        if (keywords.Count != 0)
        {
            metadata["keywords"] = keywords.ToArray();
        }

        // Document type
        metadata["documentType"] = "manual";

        return metadata;
    }

    /// <summary>
    /// Extract technical documentation metadata.
    /// </summary>
    private static Dictionary<string, object> ExtractTechnicalDocMetadata(string content)
    {
        var metadata = new Dictionary<string, object>();

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        // Libraries and packages
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var libPatterns = new[]
        {
            @"import\s+([a-zA-Z0-9_\.@\/\-]+)",           // JavaScript/Python
            @"from\s+([a-zA-Z0-9_\.]+)\s+import",        // Python
            @"using\s+([a-zA-Z0-9_\.]+);",               // C#
            @"require\(['""]([^'""]+)['""]\)",           // Node.js
            @"#include\s+[<""]([^>""]+)[>""]",           // C/C++
            @"use\s+([a-zA-Z0-9_\\]+);",                 // PHP
            @"import\s+\{[^}]+\}\s+from\s+['""]([^'""]+)['""]" // ES6
        };

        foreach (var pattern in libPatterns)
        {
            var matches = Regex.Matches(content, pattern);
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var lib = match.Groups[1].Value;
                    // Filter out relative paths and common words
                    if (!lib.StartsWith('.') && !lib.StartsWith('/') && lib.Length > 2)
                    {
                        libraries.Add(lib);
                    }
                }
            }
        }

        if (libraries.Count != 0)
        {
            metadata["libraries"] = libraries.Take(15).ToArray();
        }

        // Frameworks
        var frameworkKeywords = new[]
        {
            "React", "Vue", "Angular", "Svelte", "Next.js", "Nuxt",
            "Express", "FastAPI", "Django", "Flask", "Spring", "ASP.NET",
            "TensorFlow", "PyTorch", "scikit-learn",
            "Docker", "Kubernetes", "AWS", "Azure", "GCP"
        };

        var foundFrameworks = frameworkKeywords
            .Where(fw => content.Contains(fw, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();

        if (foundFrameworks.Length != 0)
        {
            metadata["frameworks"] = foundFrameworks;
        }

        // Technologies
        var techKeywords = new[]
        {
            "JavaScript", "TypeScript", "Python", "C#", "Java", "Go", "Rust", "C++",
            "API", "REST", "GraphQL", "gRPC", "WebSocket",
            "SQL", "NoSQL", "MongoDB", "PostgreSQL", "MySQL", "Redis",
            "HTML", "CSS", "SCSS", "Tailwind"
        };

        var foundTech = techKeywords
            .Where(tech => content.Contains(tech, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToArray();

        if (foundTech.Length != 0)
        {
            metadata["technologies"] = foundTech;
        }

        // Extract topics
        var topics = ExtractTopicsFromHeaders(content);
        if (topics.Count != 0)
        {
            metadata["topics"] = topics.ToArray();
        }

        // Extract keywords
        var keywords = ExtractKeywords(content, isTechnical: true);
        if (keywords.Count != 0)
        {
            metadata["keywords"] = keywords.ToArray();
        }

        // Document type detection
        var docType = DetectDocumentType(content);
        metadata["documentType"] = docType;

        return metadata;
    }

    /// <summary>
    /// Extract general document metadata.
    /// </summary>
    private static Dictionary<string, object> ExtractGeneralMetadata(string content)
    {
        var metadata = new Dictionary<string, object>();

        if (string.IsNullOrWhiteSpace(content))
            return metadata;

        // Extract topics
        var topics = ExtractTopicsFromHeaders(content);
        if (topics.Count != 0)
        {
            metadata["topics"] = topics.ToArray();
        }

        // Extract keywords
        var keywords = ExtractKeywords(content);
        if (keywords.Count != 0)
        {
            metadata["keywords"] = keywords.ToArray();
        }

        // Generate description (first meaningful sentence)
        var description = ExtractDescription(content);
        if (!string.IsNullOrEmpty(description))
        {
            metadata["description"] = description;
        }

        // Document type
        var docType = DetectDocumentType(content);
        metadata["documentType"] = docType;

        // Language detection (simple heuristic)
        var language = DetectLanguage(content);
        metadata["language"] = language;

        return metadata;
    }

    /// <summary>
    /// Extract topics from section headers.
    /// </summary>
    private static List<string> ExtractTopicsFromHeaders(string content)
    {
        var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Markdown headers
        var mdHeaders = Regex.Matches(content, @"^#{1,3}\s+(.+)$", RegexOptions.Multiline);
        foreach (Match match in mdHeaders)
        {
            if (match.Groups.Count > 1)
            {
                var header = match.Groups[1].Value.Trim();
                if (header.Length > 3 && header.Length < 60)
                {
                    topics.Add(header);
                }
            }
        }

        // Numbered sections
        var numberedSections = Regex.Matches(content, @"^\d+[\.\)]\s+([A-Z][^\r\n]{3,60})", RegexOptions.Multiline);
        foreach (Match match in numberedSections)
        {
            if (match.Groups.Count > 1)
            {
                topics.Add(match.Groups[1].Value.Trim());
            }
        }

        return topics.Take(5).ToList();
    }

    /// <summary>
    /// Extract keywords from content.
    /// </summary>
    private static List<string> ExtractKeywords(string content, bool isManual = false, bool isTechnical = false)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Common stop words to exclude
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
            "in", "with", "to", "for", "of", "as", "by", "this", "that", "these", "those"
        };

        // Split into words
        var words = Regex.Split(content, @"\W+")
            .Where(w => w.Length > 3 && w.Length < 30)
            .Where(w => !stopWords.Contains(w))
            .Where(w => !Regex.IsMatch(w, @"^\d+$")); // Exclude pure numbers

        // Count word frequency
        var wordCounts = words
            .GroupBy(w => w.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(20);

        foreach (var group in wordCounts)
        {
            keywords.Add(group.Key);
        }

        return keywords.Take(10).ToList();
    }

    /// <summary>
    /// Extract description from first meaningful paragraph.
    /// </summary>
    private static string ExtractDescription(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        // Find first paragraph with meaningful content
        var paragraphs = content.Split(s_paragraphSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            // Skip headers, short lines, and lines starting with special chars
            if (trimmed.Length > 50 && trimmed.Length < 300 &&
                !trimmed.StartsWith('#') &&
                !Regex.IsMatch(trimmed, @"^[\d\.\)]+\s"))
            {
                // Take first sentence
                var sentences = Regex.Split(trimmed, @"(?<=[.!?])\s+");
                if (sentences.Length > 0)
                {
                    var desc = sentences[0].Trim();
                    if (desc.Length > 20)
                    {
                        return desc.Length > 200 ? string.Concat(desc.AsSpan(0, 200), "...") : desc;
                    }
                }
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Detect document type based on content patterns.
    /// </summary>
    private static string DetectDocumentType(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "unknown";

        var lowerContent = content.ToLowerInvariant();

        // Manual indicators
        if (lowerContent.Contains("user guide") || lowerContent.Contains("manual") ||
            lowerContent.Contains("instructions") || lowerContent.Contains("how to use"))
        {
            return "manual";
        }

        // Tutorial indicators
        if (lowerContent.Contains("tutorial") || lowerContent.Contains("getting started") ||
            lowerContent.Contains("step by step") || lowerContent.Contains("walkthrough"))
        {
            return "tutorial";
        }

        // Reference indicators
        if (lowerContent.Contains("api reference") || lowerContent.Contains("documentation") ||
            lowerContent.Contains("specification") || lowerContent.Contains("reference guide"))
        {
            return "reference";
        }

        // Guide indicators
        if (lowerContent.Contains("guide") || lowerContent.Contains("overview") ||
            lowerContent.Contains("introduction"))
        {
            return "guide";
        }

        // Article indicators
        if (lowerContent.Contains("abstract") || lowerContent.Contains("conclusion") ||
            lowerContent.Contains("methodology"))
        {
            return "article";
        }

        return "document";
    }

    /// <summary>
    /// Simple language detection.
    /// </summary>
    private static string DetectLanguage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "unknown";

        // Korean
        if (content.Any(c => c >= 0xAC00 && c <= 0xD7AF))
            return "ko";

        // Chinese
        if (content.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            return "zh";

        // Japanese
        if (content.Any(c => (c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF)))
            return "ja";

        // Default to English
        return "en";
    }

    /// <summary>
    /// Calculate confidence score based on extracted fields.
    /// </summary>
    private static double CalculateConfidence(Dictionary<string, object> metadata)
    {
        var score = 0.0;
        var maxScore = 0.0;

        // Score for each field
        if (metadata.ContainsKey("productName")) { score += 0.15; maxScore += 0.15; }
        if (metadata.ContainsKey("company")) { score += 0.10; maxScore += 0.10; }
        if (metadata.ContainsKey("version")) { score += 0.10; maxScore += 0.10; }
        if (metadata.ContainsKey("releaseDate")) { score += 0.05; maxScore += 0.05; }
        if (metadata.ContainsKey("topics")) { score += 0.15; maxScore += 0.15; }
        if (metadata.ContainsKey("keywords")) { score += 0.15; maxScore += 0.15; }
        if (metadata.ContainsKey("description")) { score += 0.10; maxScore += 0.10; }
        if (metadata.ContainsKey("libraries")) { score += 0.10; maxScore += 0.10; }
        if (metadata.ContainsKey("frameworks")) { score += 0.05; maxScore += 0.05; }
        if (metadata.ContainsKey("technologies")) { score += 0.05; maxScore += 0.05; }

        maxScore = Math.Max(maxScore, 0.01); // Avoid division by zero

        return Math.Min(score / maxScore, 1.0);
    }

    #region LoggerMessage

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting rule-based metadata extraction with schema: {Schema}")]
    private static partial void LogStartingExtraction(ILogger logger, MetadataSchema schema);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rule-based extraction complete. Found {Count} fields")]
    private static partial void LogExtractionComplete(ILogger logger, int count);

    #endregion
}
