namespace FileFlux.Core;

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
