using System.Text.Json;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// The resource ids of an Undoc document that are images. Undoc lists more than pictures: a PowerPoint HD Photo
/// effects layer (<c>a14:imgLayer</c>, a <c>.wdp</c>) comes back as <c>type: "other"</c> beside the picture it belongs
/// to, and audio/video/OLE parts carry their own types. Only <c>type: "image"</c> is an image the document shows.
/// </summary>
internal static class UndocImageResources
{
    /// <summary>
    /// The ids whose resource type is <c>image</c>. An id whose metadata cannot be read is kept — the type is unknown,
    /// and dropping it would lose a picture silently.
    /// </summary>
    public static IEnumerable<string> ImageIds(UndocDocument doc)
    {
        foreach (var id in doc.GetResourceIds())
        {
            if (IsImage(doc, id))
                yield return id;
        }
    }

    private static bool IsImage(UndocDocument doc, string id)
    {
        try
        {
            using var info = doc.GetResourceInfo(id);
            return info is null
                   || !info.RootElement.TryGetProperty("type", out var type)
                   || type.ValueKind != JsonValueKind.String
                   || string.Equals(type.GetString(), "image", StringComparison.OrdinalIgnoreCase);
        }
        catch (UndocException)
        {
            return true;
        }
    }
}
