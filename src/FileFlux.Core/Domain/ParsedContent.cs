namespace FileFlux.Core;

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
    public string Type { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public List<Section> Sections { get; set; } = new();
    public List<Entity> Entities { get; set; } = new();
}

/// <summary>
/// Document section
/// </summary>
public class Section
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int Level { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    public List<Section> Children { get; set; } = new();
}

/// <summary>
/// Document entity
/// </summary>
public class Entity
{
    public string Text { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double Confidence { get; set; }
}

/// <summary>
/// Quality metrics for structured content
/// </summary>
public class QualityMetrics
{
    public double StructureScore { get; set; }
    public double ConsistencyScore { get; set; }
    public double InformationRetentionScore { get; set; }
    public double ConfidenceScore { get; set; }
    public double CompletenessScore { get; set; }
    public double OverallScore => (StructureScore + ConsistencyScore + InformationRetentionScore) / 3.0;
    public Dictionary<string, object> Details { get; set; } = new();
}

/// <summary>
/// Parsing process information
/// </summary>
public class ParsingInfo
{
    public string ParserType { get; set; } = string.Empty;
    public bool UsedLlm { get; set; }
    public string? LlmModel { get; set; }
    public int? TokensUsed { get; set; }
    public List<string> Warnings { get; set; } = new();
}
