using FileFlux.Domain;
using System.Text.Json;

namespace FileFlux.CLI.Output;

/// <summary>
/// JSON output writer
/// </summary>
public class JsonOutputWriter : IOutputWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string Extension => ".json";

    public async Task WriteAsync(IEnumerable<DocumentChunk> chunks, string outputPath, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var json = JsonSerializer.Serialize(chunkList, Options);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }
}
