using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileFlux.Core.Infrastructure.Interop;

/// <summary>
/// High-level wrapper for undoc document handle with lazy property loading.
/// Provides safe access to document content and resources.
/// </summary>
public sealed class UndocDocument : IDisposable
{
    private readonly IntPtr _handle;
    private readonly UndocNativeLoader _loader;
    private bool _disposed;

    // Lazy-loaded cached values
    private string? _markdown;
    private string? _plainText;
    private string? _title;
    private string? _author;
    private int? _sectionCount;
    private int? _resourceCount;
    private IReadOnlyList<UndocResourceInfo>? _resources;

    /// <summary>
    /// Gets the native document handle.
    /// </summary>
    internal IntPtr Handle => _handle;

    /// <summary>
    /// Gets whether this document handle is valid.
    /// </summary>
    public bool IsValid => _handle != IntPtr.Zero && !_disposed;

    /// <summary>
    /// Gets the markdown representation of the document (lazy-loaded).
    /// </summary>
    public string Markdown => _markdown ??= GetMarkdown();

    /// <summary>
    /// Gets the plain text content of the document (lazy-loaded).
    /// </summary>
    public string PlainText => _plainText ??= GetPlainText();

    /// <summary>
    /// Gets the document title (lazy-loaded).
    /// </summary>
    public string? Title => _title ??= GetTitle();

    /// <summary>
    /// Gets the document author (lazy-loaded).
    /// </summary>
    public string? Author => _author ??= GetAuthor();

    /// <summary>
    /// Gets the section count (lazy-loaded).
    /// </summary>
    public int SectionCount => _sectionCount ??= GetSectionCount();

    /// <summary>
    /// Gets the resource count (lazy-loaded).
    /// </summary>
    public int ResourceCount => _resourceCount ??= GetResourceCount();

    /// <summary>
    /// Gets all resources in the document (lazy-loaded).
    /// </summary>
    public IReadOnlyList<UndocResourceInfo> Resources => _resources ??= LoadResources();

    /// <summary>
    /// Gets only image resources from the document.
    /// </summary>
    public IEnumerable<UndocResourceInfo> Images => Resources.Where(r => r.Type == "image");

    private UndocDocument(IntPtr handle, UndocNativeLoader loader)
    {
        _handle = handle;
        _loader = loader;
    }

    /// <summary>
    /// Parses a document from a file path.
    /// </summary>
    public static UndocDocument? ParseFile(string filePath)
    {
        var loader = UndocNativeLoader.Instance;
        if (!loader.IsLoaded || loader.ParseFile == null)
            return null;

        var handle = loader.ParseFile(filePath);
        if (handle == IntPtr.Zero)
            return null;

        return new UndocDocument(handle, loader);
    }

    /// <summary>
    /// Parses a document from a byte array.
    /// </summary>
    public static UndocDocument? ParseBytes(byte[] data)
    {
        var loader = UndocNativeLoader.Instance;
        if (!loader.IsLoaded || loader.ParseBytes == null)
            return null;

        var gcHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var dataPtr = gcHandle.AddrOfPinnedObject();
            var handle = loader.ParseBytes(dataPtr, (nuint)data.Length);
            if (handle == IntPtr.Zero)
                return null;

            return new UndocDocument(handle, loader);
        }
        finally
        {
            gcHandle.Free();
        }
    }

    private string GetMarkdown()
    {
        ThrowIfDisposed();
        if (_loader.ToMarkdown == null)
            return string.Empty;

        var ptr = _loader.ToMarkdown(_handle, (int)UndocMarkdownFlags.None);
        if (ptr == IntPtr.Zero)
            return string.Empty;

        try
        {
            return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }
        finally
        {
            _loader.FreeString?.Invoke(ptr);
        }
    }

    private string GetPlainText()
    {
        ThrowIfDisposed();
        if (_loader.PlainText == null)
            return string.Empty;

        var ptr = _loader.PlainText(_handle);
        if (ptr == IntPtr.Zero)
            return string.Empty;

        try
        {
            return Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
        }
        finally
        {
            _loader.FreeString?.Invoke(ptr);
        }
    }

    private string? GetTitle()
    {
        ThrowIfDisposed();
        if (_loader.GetTitle == null)
            return null;

        var ptr = _loader.GetTitle(_handle);
        if (ptr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            _loader.FreeString?.Invoke(ptr);
        }
    }

    private string? GetAuthor()
    {
        ThrowIfDisposed();
        if (_loader.GetAuthor == null)
            return null;

        var ptr = _loader.GetAuthor(_handle);
        if (ptr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            _loader.FreeString?.Invoke(ptr);
        }
    }

    private int GetSectionCount()
    {
        ThrowIfDisposed();
        return _loader.SectionCount?.Invoke(_handle) ?? 0;
    }

    private int GetResourceCount()
    {
        ThrowIfDisposed();
        return _loader.ResourceCount?.Invoke(_handle) ?? 0;
    }

    private static readonly ReadOnlyCollection<UndocResourceInfo> EmptyResources =
        new ReadOnlyCollection<UndocResourceInfo>([]);

    private ReadOnlyCollection<UndocResourceInfo> LoadResources()
    {
        ThrowIfDisposed();

        // Check if resource API is available (v0.1.8+)
        if (_loader.GetResourceIds == null)
            return EmptyResources;

        var idsPtr = _loader.GetResourceIds(_handle);
        if (idsPtr == IntPtr.Zero)
            return EmptyResources;

        string[] ids;
        try
        {
            var idsJson = Marshal.PtrToStringUTF8(idsPtr);
            if (string.IsNullOrEmpty(idsJson))
                return EmptyResources;

            ids = JsonSerializer.Deserialize<string[]>(idsJson) ?? [];
        }
        finally
        {
            _loader.FreeString?.Invoke(idsPtr);
        }

        if (ids.Length == 0)
            return EmptyResources;

        var resources = new List<UndocResourceInfo>(ids.Length);
        foreach (var id in ids)
        {
            var info = GetResourceInfo(id);
            if (info != null)
                resources.Add(info);
        }

        return resources.AsReadOnly();
    }

    /// <summary>
    /// Gets detailed information about a specific resource.
    /// </summary>
    public UndocResourceInfo? GetResourceInfo(string resourceId)
    {
        ThrowIfDisposed();
        if (_loader.GetResourceInfo == null)
            return null;

        var infoPtr = _loader.GetResourceInfo(_handle, resourceId);
        if (infoPtr == IntPtr.Zero)
            return null;

        try
        {
            var infoJson = Marshal.PtrToStringUTF8(infoPtr);
            if (string.IsNullOrEmpty(infoJson))
                return null;

            return JsonSerializer.Deserialize<UndocResourceInfo>(infoJson);
        }
        finally
        {
            _loader.FreeString?.Invoke(infoPtr);
        }
    }

    /// <summary>
    /// Gets the binary data for a specific resource.
    /// </summary>
    public byte[] GetResourceData(string resourceId)
    {
        ThrowIfDisposed();
        if (_loader.GetResourceData == null || _loader.FreeBytes == null)
            return [];

        var ptr = _loader.GetResourceData(_handle, resourceId, out var len);
        if (ptr == IntPtr.Zero || len == 0)
            return [];

        try
        {
            var data = new byte[(int)len];
            Marshal.Copy(ptr, data, 0, (int)len);
            return data;
        }
        finally
        {
            _loader.FreeBytes(ptr, len);
        }
    }

    /// <summary>
    /// Extracts all images from the document.
    /// </summary>
    public IEnumerable<ExtractedResource> ExtractImages()
    {
        ThrowIfDisposed();

        foreach (var image in Images)
        {
            var data = GetResourceData(image.Id);
            if (data.Length == 0)
                continue;

            yield return new ExtractedResource
            {
                Id = image.Id,
                Filename = image.Filename ?? $"{image.Id}.png",
                MimeType = image.MimeType ?? "image/png",
                Data = data,
                Width = image.Width,
                Height = image.Height,
                AltText = image.AltText
            };
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_handle != IntPtr.Zero)
        {
            _loader.FreeDocument?.Invoke(_handle);
        }

        _disposed = true;
    }
}

/// <summary>
/// Resource metadata from undoc document.
/// </summary>
public record UndocResourceInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("filename")] string? Filename,
    [property: JsonPropertyName("mime_type")] string? MimeType,
    [property: JsonPropertyName("size")] int Size,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("alt_text")] string? AltText
);

/// <summary>
/// Extracted resource with binary data.
/// </summary>
public sealed class ExtractedResource
{
    /// <summary>Resource identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Filename for the resource.</summary>
    public required string Filename { get; init; }

    /// <summary>MIME type of the resource.</summary>
    public required string MimeType { get; init; }

    /// <summary>Binary data of the resource.</summary>
    public required byte[] Data { get; init; }

    /// <summary>Width in pixels (for images).</summary>
    public int? Width { get; init; }

    /// <summary>Height in pixels (for images).</summary>
    public int? Height { get; init; }

    /// <summary>Alternative text (for images).</summary>
    public string? AltText { get; init; }
}
