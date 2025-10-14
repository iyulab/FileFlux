namespace FileFlux.Domain;

/// <summary>
/// Stage 2 output: Structured document analysis result
/// </summary>
public class ParsedContent
{
    /// <summary>
    /// Unique parsing ID
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Reference to raw extraction stage
    /// </summary>
    public Guid RawId { get; set; }

    /// <summary>
    /// Structured text content
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Document structure analysis
    /// </summary>
    public DocumentStructure Structure { get; set; } = new();

    /// <summary>
    /// Document metadata
    /// </summary>
    public DocumentMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Quality metrics
    /// </summary>
    public QualityMetrics Quality { get; set; } = new();

    /// <summary>
    /// Parsing timestamp
    /// </summary>
    public DateTime ParsedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Processing duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Parsing process information
    /// </summary>
    public ParsingInfo Info { get; set; } = new();

    /// <summary>
    /// Processing status
    /// </summary>
    public ProcessingStatus Status { get; set; } = ProcessingStatus.Completed;

    /// <summary>
    /// Errors encountered during parsing
    /// </summary>
    public List<ProcessingError> Errors { get; set; } = new();

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool IsSuccess => Status == ProcessingStatus.Completed && Errors.Count == 0;
}

/// <summary>
/// Document structure analysis
/// </summary>
public class DocumentStructure
{
    /// <summary>
    /// Document type (Technical, Business, Legal, Academic, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Document topic/domain
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Document summary
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Key keywords
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Structured sections
    /// </summary>
    public List<Section> Sections { get; set; } = new();

    /// <summary>
    /// Extracted entities (people, organizations, locations, etc.)
    /// </summary>
    public List<Entity> Entities { get; set; } = new();
}

/// <summary>
/// Document section
/// </summary>
public class Section
{
    /// <summary>
    /// Section ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Section title
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Section type (Header, Paragraph, List, Table, Code, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Section content
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchy level (1=top, 2=sub, etc.)
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Start position in original document
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// End position in original document
    /// </summary>
    public int End { get; set; }

    /// <summary>
    /// Child sections
    /// </summary>
    public List<Section> Children { get; set; } = new();
}

/// <summary>
/// Document entity
/// </summary>
public class Entity
{
    /// <summary>
    /// Entity text
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Entity type (Person, Organization, Location, Date, etc.)
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Confidence score (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }
}

/// <summary>
/// Quality metrics for structured content
/// </summary>
public class QualityMetrics
{
    /// <summary>
    /// Structure score (0.0 - 1.0)
    /// </summary>
    public double StructureScore { get; set; }

    /// <summary>
    /// Consistency score (0.0 - 1.0)
    /// </summary>
    public double ConsistencyScore { get; set; }

    /// <summary>
    /// Information retention score (0.0 - 1.0)
    /// </summary>
    public double InformationRetentionScore { get; set; }

    /// <summary>
    /// Confidence score (0.0 - 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>
    /// Completeness score (0.0 - 1.0)
    /// </summary>
    public double CompletenessScore { get; set; }

    /// <summary>
    /// Overall quality score (weighted average)
    /// </summary>
    public double OverallScore => (StructureScore + ConsistencyScore + InformationRetentionScore) / 3.0;

    /// <summary>
    /// Detailed metrics
    /// </summary>
    public Dictionary<string, object> Details { get; set; } = new();
}

/// <summary>
/// Parsing process information
/// </summary>
public class ParsingInfo
{
    /// <summary>
    /// Parser type used
    /// </summary>
    public string ParserType { get; set; } = string.Empty;

    /// <summary>
    /// Whether LLM was used
    /// </summary>
    public bool UsedLlm { get; set; }

    /// <summary>
    /// LLM model name (if used)
    /// </summary>
    public string? LlmModel { get; set; }

    /// <summary>
    /// Tokens consumed (if LLM used)
    /// </summary>
    public int? TokensUsed { get; set; }

    /// <summary>
    /// Processing warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}
