using System.Text.Json;
using System.Text.Json.Serialization;
using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// The data of a list structure (<see cref="StructuredElement.Data"/> for <see cref="StructureType.List"/>).
/// </summary>
internal sealed record ListBlockData(bool Ordered, List<string> Items);

/// <summary>
/// Source-generated serialization for the data refinement attaches to structured elements, so refining works in an
/// application that disables reflection-based JSON (trimmed, native AOT, file-based <c>dotnet run app.cs</c>).
/// </summary>
[JsonSerializable(typeof(CodeBlockData))]
[JsonSerializable(typeof(List<Dictionary<string, string>>))]
[JsonSerializable(typeof(ListBlockData))]
internal sealed partial class StructureJsonContext : JsonSerializerContext
{
    internal static JsonElement ToElement(CodeBlockData data) =>
        JsonSerializer.SerializeToElement(data, Default.CodeBlockData);

    internal static JsonElement ToElement(List<Dictionary<string, string>> rows) =>
        JsonSerializer.SerializeToElement(rows, Default.ListDictionaryStringString);

    internal static JsonElement ToElement(ListBlockData list) =>
        JsonSerializer.SerializeToElement(list, Default.ListBlockData);
}
