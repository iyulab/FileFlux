using System.Text.Json.Serialization;

namespace FileFlux.Core.Infrastructure.Interop;

/// <summary>
/// Source-generated reading of the JSON the native Office parser returns (resource ids and resource info), so the readers
/// work in an application that disables reflection-based JSON (trimmed, native AOT, file-based <c>dotnet run app.cs</c>).
/// </summary>
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(UndocResourceInfo))]
internal sealed partial class UndocJsonContext : JsonSerializerContext;
