using FileFlux.Core;
using System.Text;

namespace FileFlux.CLI.Output;

/// <summary>
/// Markdown output writer
/// </summary>
public class MarkdownOutputWriter : IOutputWriter
{
    public string Extension => ".md";

    public async Task WriteAsync(IEnumerable<DocumentChunk> chunks, string outputPath, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        var chunkList = chunks.ToList();

        sb.AppendLine($"# Document Chunks ({chunkList.Count} chunks)");
        sb.AppendLine();

        foreach (var chunk in chunkList)
        {
            sb.AppendLine($"## Chunk {chunk.Index + 1}");
            sb.AppendLine();
            sb.AppendLine($"**Length:** {chunk.Content.Length} characters");

            if (ChunkPropsKeys.TryGetValue<string>(chunk.Props, ChunkPropsKeys.EnrichedTopics, out var topicsStr) && !string.IsNullOrEmpty(topicsStr))
            {
                sb.AppendLine($"**Topics:** {topicsStr}");
            }

            if (chunk.EnrichedKeywords is { Count: > 0 } keywordsList)
            {
                sb.AppendLine($"**Keywords:** {string.Join(", ", keywordsList)}");
            }

            sb.AppendLine();
            sb.AppendLine("### Content");
            sb.AppendLine();
            sb.AppendLine(chunk.Content);
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }
}
