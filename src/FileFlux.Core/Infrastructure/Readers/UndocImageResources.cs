using System.Text.Json;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// An image an Undoc document shows: its resource id and the alt text of the picture that shows it (if any).
/// </summary>
internal readonly record struct UndocImageResource(string Id, string? AltText);

/// <summary>
/// The resources of an Undoc document that are images the document shows. Undoc lists more than that: audio, video
/// and OLE parts carry their own <c>type</c>, and each resource carries a <c>role</c> — <c>primary</c> for an image the
/// document shows, <c>alternate</c> for another encoding of one (the SVG original Office writes beside a picture's
/// raster rendering), <c>layer</c> for a layer composited onto one (an HD Photo <c>.wdp</c> effects layer). Only
/// primaries of type <c>image</c> are kept, so one picture is read once.
/// </summary>
internal static class UndocImageResources
{
    /// <summary>
    /// The image resources the document shows. A resource whose metadata cannot be read, or that carries no
    /// <c>type</c>/<c>role</c>, is kept — what it is is unknown, and dropping it would lose a picture silently.
    /// </summary>
    public static IEnumerable<UndocImageResource> Shown(UndocDocument doc)
    {
        foreach (var id in doc.GetResourceIds())
        {
            if (TryRead(doc, id, out var altText))
                yield return new UndocImageResource(id, altText);
        }
    }

    /// <summary>
    /// The 1-based sections each image resource is shown in, ascending — for a presentation, its slides. Read from the
    /// document's JSON, where the content names the images it shows by <c>resource_id</c>, pictures inside groups too.
    /// A resource shown in several sections lists each; one the content does not reference is absent (Undoc 0.16 does
    /// not reference picture bullets or picture fills).
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<int>> SectionsOfResources(UndocDocument doc)
    {
        var sections = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        try
        {
            using var json = JsonDocument.Parse(doc.ToJson(compact: true));
            if (!json.RootElement.TryGetProperty("sections", out var list) || list.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);

            var number = 0;
            foreach (var section in list.EnumerateArray())
            {
                number++;
                Collect(section, number, sections);
            }
        }
        catch (Exception ex) when (ex is UndocException or JsonException)
        {
            // Without the content's JSON no image can be placed; the images themselves are unaffected.
        }

        return sections.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value, StringComparer.Ordinal);

        static void Collect(JsonElement element, int number, Dictionary<string, List<int>> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("resource_id") && property.Value.ValueKind == JsonValueKind.String)
                        {
                            var id = property.Value.GetString()!;
                            if (!into.TryGetValue(id, out var numbers))
                                into[id] = numbers = [];
                            if (numbers.Count == 0 || numbers[^1] != number)
                                numbers.Add(number);
                        }
                        else
                            Collect(property.Value, number, into);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        Collect(item, number, into);
                    break;
            }
        }
    }

    private static bool TryRead(UndocDocument doc, string id, out string? altText)
    {
        altText = null;
        try
        {
            using var info = doc.GetResourceInfo(id);
            if (info is null)
                return true;

            var root = info.RootElement;
            if (!IsUnsetOr(root, "type", "image") || !IsUnsetOr(root, "role", "primary"))
                return false;

            if (root.TryGetProperty("alt_text", out var alt) && alt.ValueKind == JsonValueKind.String)
            {
                var text = alt.GetString();
                altText = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            return true;
        }
        catch (UndocException)
        {
            return true;
        }
    }

    private static bool IsUnsetOr(JsonElement root, string property, string expected) =>
        !root.TryGetProperty(property, out var value)
        || value.ValueKind != JsonValueKind.String
        || string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);
}
