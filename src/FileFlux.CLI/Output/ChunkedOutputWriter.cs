using FileFlux.Core;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.CLI.Output;

/// <summary>
/// Output writer that creates individual chunk files in a directory
/// </summary>
public class ChunkedOutputWriter : IOutputWriter
{
    private readonly string _format;

    public ChunkedOutputWriter(string format = "md")
    {
        _format = format.ToLowerInvariant();
    }

    public string Extension => _format switch
    {
        "json" => ".json",
        "jsonl" => ".jsonl",
        _ => ".md"
    };

    public async Task WriteAsync(IEnumerable<DocumentChunk> chunks, string outputPath, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var totalChunks = chunkList.Count;

        // Create chunks directory
        Directory.CreateDirectory(outputPath);

        // Write individual chunk files
        foreach (var chunk in chunkList)
        {
            var chunkFileName = $"chunk_{chunk.Index + 1}{Extension}";
            var chunkPath = Path.Combine(outputPath, chunkFileName);

            var content = _format switch
            {
                "json" => FormatAsJson(chunk, totalChunks),
                "jsonl" => FormatAsJson(chunk, totalChunks),
                _ => FormatAsMarkdown(chunk, totalChunks)
            };

            await File.WriteAllTextAsync(chunkPath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatAsMarkdown(DocumentChunk chunk, int totalChunks)
    {
        var sb = new StringBuilder();
        var chunkNum = chunk.Index + 1;

        // YAML frontmatter with navigation
        sb.AppendLine("---");
        sb.AppendLine($"chunk: {chunkNum}");
        sb.AppendLine($"total: {totalChunks}");
        if (chunkNum > 1)
            sb.AppendLine($"prev: chunk_{chunkNum - 1}.md");
        if (chunkNum < totalChunks)
            sb.AppendLine($"next: chunk_{chunkNum + 1}.md");
        sb.AppendLine($"tokens: {chunk.Tokens}");
        sb.AppendLine($"quality: {chunk.Quality:F2}");
        if (!string.IsNullOrEmpty(chunk.Metadata.FileName))
            sb.AppendLine($"source: \"{chunk.Metadata.FileName}\"");
        if (chunk.Location.StartPage.HasValue)
            sb.AppendLine($"page: {chunk.Location.StartPage}");
        if (!string.IsNullOrEmpty(chunk.Location.Section))
            sb.AppendLine($"section: \"{chunk.Location.Section}\"");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine($"# Chunk {chunkNum} of {totalChunks}");
        sb.AppendLine();

        // Enrichment metadata if present
        if (chunk.Metadata.CustomProperties.TryGetValue("enriched_topics", out var topics) && topics is string topicsStr)
        {
            sb.AppendLine($"**Topics:** {topicsStr}");
        }

        if (chunk.Metadata.CustomProperties.TryGetValue("enriched_keywords", out var keywords) && keywords is string keywordsStr)
        {
            sb.AppendLine($"**Keywords:** {keywordsStr}");
        }

        if (chunk.Metadata.CustomProperties.TryGetValue("enriched_summary", out var summary) && summary is string summaryStr)
        {
            sb.AppendLine($"**Summary:** {summaryStr}");
            sb.AppendLine();
        }

        // Content
        sb.AppendLine(chunk.Content);
        sb.AppendLine();

        // Navigation footer
        sb.AppendLine("---");
        var nav = new List<string>();
        if (chunkNum > 1)
            nav.Add($"[← Previous](chunk_{chunkNum - 1}.md)");
        nav.Add($"[Info](info.json)");
        if (chunkNum < totalChunks)
            nav.Add($"[Next →](chunk_{chunkNum + 1}.md)");
        sb.AppendLine(string.Join(" | ", nav));

        return sb.ToString();
    }

    private static string FormatAsJson(DocumentChunk chunk, int totalChunks)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var chunkNum = chunk.Index + 1;

        var data = new
        {
            index = chunk.Index,
            chunk = chunkNum,
            total = totalChunks,
            navigation = new
            {
                prev = chunkNum > 1 ? $"chunk_{chunkNum - 1}.json" : null,
                next = chunkNum < totalChunks ? $"chunk_{chunkNum + 1}.json" : null
            },
            content = chunk.Content,
            tokens = chunk.Tokens,
            quality = chunk.Quality,
            importance = chunk.Importance,
            density = chunk.Density,
            contextDependency = chunk.ContextDependency,
            metadata = new
            {
                fileName = chunk.Metadata.FileName,
                fileType = chunk.Metadata.FileType,
                language = chunk.Metadata.Language,
                customProperties = chunk.Metadata.CustomProperties.Count > 0 ? chunk.Metadata.CustomProperties : null
            },
            location = new
            {
                startPage = chunk.Location.StartPage,
                endPage = chunk.Location.EndPage,
                section = chunk.Location.Section,
                headingPath = chunk.Location.HeadingPath.Count > 0 ? chunk.Location.HeadingPath : null
            },
            sourceInfo = chunk.SourceInfo != null ? new
            {
                sourceId = chunk.SourceInfo.SourceId,
                sourceType = chunk.SourceInfo.SourceType,
                title = chunk.SourceInfo.Title
            } : null
        };

        return JsonSerializer.Serialize(data, options);
    }
}
