using System.Text.Json;

namespace FileFlux.Core;

/// <summary>
/// Structured element extracted from document.
/// Preserves tables, code blocks, lists, and specs as JSON.
/// </summary>
public class StructuredElement
{
    /// <summary>
    /// Unique element ID.
    /// </summary>
    public string Id { get; init; } = $"struct_{Guid.NewGuid():N}";

    /// <summary>
    /// Type of structured element.
    /// </summary>
    public StructureType Type { get; init; }

    /// <summary>
    /// Chunk ID where this structure originates (set after chunking).
    /// </summary>
    public Guid? SourceChunkId { get; set; }

    /// <summary>
    /// Caption or title describing the structure.
    /// </summary>
    public string? Caption { get; init; }

    /// <summary>
    /// Context around the structure in the document.
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// Structured data as JSON element.
    /// </summary>
    public JsonElement Data { get; init; }

    /// <summary>
    /// Schema describing the data structure (for tables).
    /// </summary>
    public JsonElement? Schema { get; init; }

    /// <summary>
    /// Position in original document.
    /// </summary>
    public StructureLocation Location { get; init; } = new();

    /// <summary>
    /// Additional properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = new();

    /// <summary>
    /// Get data as specific type.
    /// </summary>
    public T? GetData<T>() where T : class
    {
        try
        {
            return Data.Deserialize<T>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get data as list of dictionaries (for tables).
    /// </summary>
    public IReadOnlyList<Dictionary<string, object>>? GetTableData()
    {
        if (Type != StructureType.Table)
            return null;

        try
        {
            return Data.Deserialize<List<Dictionary<string, object>>>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get data as key-value dictionary (for specs).
    /// </summary>
    public IReadOnlyDictionary<string, object>? GetSpecData()
    {
        if (Type != StructureType.Spec)
            return null;

        try
        {
            return Data.Deserialize<Dictionary<string, object>>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get code content (for code blocks).
    /// </summary>
    public CodeBlockData? GetCodeData()
    {
        if (Type != StructureType.Code)
            return null;

        try
        {
            return Data.Deserialize<CodeBlockData>();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Type of structured element.
/// </summary>
public enum StructureType
{
    /// <summary>
    /// Table data as JSON array of objects.
    /// </summary>
    Table,

    /// <summary>
    /// Code block with language metadata.
    /// </summary>
    Code,

    /// <summary>
    /// List items (ordered or unordered).
    /// </summary>
    List,

    /// <summary>
    /// Specification or key-value data.
    /// </summary>
    Spec,

    /// <summary>
    /// Form or input schema.
    /// </summary>
    Form,

    /// <summary>
    /// Definition list (term-definition pairs).
    /// </summary>
    Definition,

    /// <summary>
    /// Other structured content.
    /// </summary>
    Other
}

/// <summary>
/// Location of structure in document.
/// </summary>
public class StructureLocation
{
    /// <summary>
    /// Start character offset.
    /// </summary>
    public int StartChar { get; init; }

    /// <summary>
    /// End character offset.
    /// </summary>
    public int EndChar { get; init; }

    /// <summary>
    /// Page number (if applicable).
    /// </summary>
    public int? Page { get; init; }

    /// <summary>
    /// Section path in document hierarchy.
    /// </summary>
    public IReadOnlyList<string> SectionPath { get; init; } = [];
}

/// <summary>
/// Code block data structure.
/// </summary>
public class CodeBlockData
{
    /// <summary>
    /// Programming language.
    /// </summary>
    public string Language { get; init; } = string.Empty;

    /// <summary>
    /// Code content.
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Description or context of the code.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this is an executable example.
    /// </summary>
    public bool IsExecutable { get; init; }
}
