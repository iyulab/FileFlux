using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Output;

/// <summary>
/// Builds the JSON the output writer puts on disk without reflection-based serialization, so it works in trimmed, AOT and
/// file-based apps (which turn reflection-based <see cref="JsonSerializer"/> off). Values that come from open property bags
/// (<see cref="DocumentChunk.Props"/>, <see cref="DocumentMetadata.CustomProperties"/>) are converted by kind: strings,
/// numbers, booleans, dates, GUIDs, enums (as numbers), dictionaries and sequences. Any other object is serialized by
/// reflection when the app allows it, and written as its <see cref="object.ToString"/> text when it does not.
/// </summary>
internal static class OutputJson
{
    private static readonly JsonSerializerOptions ReflectionOptions = new();
    private static readonly JsonSerializerOptions ReflectionCamelCaseOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static JsonNode? ToNode(object? value, JsonNamingPolicy? namingPolicy) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        JsonElement element => JsonNode.Parse(element.GetRawText()),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        float f => JsonValue.Create(f),
        decimal m => JsonValue.Create(m),
        short sh => JsonValue.Create(sh),
        byte by => JsonValue.Create(by),
        uint ui => JsonValue.Create(ui),
        ulong ul => JsonValue.Create(ul),
        DateTime dt => JsonValue.Create(dt),
        DateTimeOffset dto => JsonValue.Create(dto),
        Guid g => JsonValue.Create(g),
        TimeSpan ts => JsonValue.Create(ts.ToString("c", CultureInfo.InvariantCulture)),
        Enum e => JsonValue.Create(Convert.ToInt64(e, CultureInfo.InvariantCulture)),
        IDictionary dictionary => Object(dictionary, namingPolicy),
        IEnumerable sequence => SequenceNode(sequence, namingPolicy),
        _ => OtherNode(value, namingPolicy)
    };

    /// <summary>All public members of <see cref="DocumentMetadata"/>, named by <paramref name="namingPolicy"/>.</summary>
    public static JsonObject Metadata(DocumentMetadata metadata, JsonNamingPolicy? namingPolicy)
    {
        string Name(string member) => namingPolicy?.ConvertName(member) ?? member;

        return new JsonObject
        {
            [Name(nameof(DocumentMetadata.FileName))] = metadata.FileName,
            [Name(nameof(DocumentMetadata.FileType))] = metadata.FileType,
            [Name(nameof(DocumentMetadata.FileSize))] = metadata.FileSize,
            [Name(nameof(DocumentMetadata.Title))] = metadata.Title,
            [Name(nameof(DocumentMetadata.Author))] = metadata.Author,
            [Name(nameof(DocumentMetadata.CreatedAt))] = metadata.CreatedAt,
            [Name(nameof(DocumentMetadata.ModifiedAt))] = metadata.ModifiedAt,
            [Name(nameof(DocumentMetadata.ProcessedAt))] = metadata.ProcessedAt,
            [Name(nameof(DocumentMetadata.Language))] = metadata.Language,
            [Name(nameof(DocumentMetadata.LanguageConfidence))] = metadata.LanguageConfidence,
            [Name(nameof(DocumentMetadata.PageCount))] = metadata.PageCount,
            [Name(nameof(DocumentMetadata.WordCount))] = metadata.WordCount,
            // Dictionary keys are data and keep their spelling, as the serializer did.
            [Name(nameof(DocumentMetadata.CustomProperties))] = Object(metadata.CustomProperties, namingPolicy),
        };
    }

    /// <summary>A dictionary whose keys are data (kept as written) and whose values go through <see cref="ToNode"/>.</summary>
    public static JsonObject Object(IDictionary dictionary, JsonNamingPolicy? namingPolicy)
    {
        var obj = new JsonObject();
        foreach (DictionaryEntry entry in dictionary)
        {
            obj[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = ToNode(entry.Value, namingPolicy);
        }

        return obj;
    }

    private static JsonArray SequenceNode(IEnumerable sequence, JsonNamingPolicy? namingPolicy)
    {
        var array = new JsonArray();
        foreach (var item in sequence)
        {
            array.Add(ToNode(item, namingPolicy));
        }

        return array;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only when reflection-based serialization is enabled; trimmed and AOT apps turn that switch off, which removes the branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Reached only when reflection-based serialization is enabled; trimmed and AOT apps turn that switch off, which removes the branch.")]
    private static JsonNode? OtherNode(object value, JsonNamingPolicy? namingPolicy)
    {
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            return JsonSerializer.SerializeToNode(value, value.GetType(), namingPolicy == null ? ReflectionOptions : ReflectionCamelCaseOptions);
        }

        return JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture));
    }
}
