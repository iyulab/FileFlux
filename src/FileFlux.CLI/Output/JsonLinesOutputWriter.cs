using FileFlux.Domain;
using System.Text.Json;

namespace FileFlux.CLI.Output;

/// <summary>
/// JSON Lines (JSONL) output writer - one JSON object per line
/// </summary>
public class JsonLinesOutputWriter : IOutputWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Extension => ".jsonl";

    public async Task WriteAsync(IEnumerable<DocumentChunk> chunks, string outputPath, CancellationToken cancellationToken = default)
    {
        using var writer = new StreamWriter(outputPath);

        foreach (var chunk in chunks)
        {
            var json = JsonSerializer.Serialize(chunk, Options);
            await writer.WriteLineAsync(json);
        }
    }
}
