using FileFlux.Core;
using System.Text.Encodings.Web;
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Preserve UTF-8 characters (Korean, Chinese, etc.)
    };

    public string Extension => ".json";

    public async Task WriteAsync(IEnumerable<DocumentChunk> chunks, string outputPath, CancellationToken cancellationToken = default)
    {
        var chunkList = chunks.ToList();
        var json = JsonSerializer.Serialize(chunkList, Options);
        await File.WriteAllTextAsync(outputPath, json, System.Text.Encoding.UTF8, cancellationToken);
    }
}
