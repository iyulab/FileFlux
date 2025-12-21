using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileFlux.Core.Infrastructure.Interop;

/// <summary>
/// Lazy loader for undoc native library.
/// Downloads platform-specific binary from GitHub releases on first use.
/// Supports DOCX, XLSX, PPTX document processing.
/// </summary>
/// <remarks>
/// Cache structure:
/// <code>
/// %LOCALAPPDATA%/FileFlux/undoc/
/// ├── lib/     - Native library only (undoc.dll, libundoc.so, libundoc.dylib)
/// └── bin/     - CLI executable (undoc.exe, undoc)
/// </code>
/// </remarks>
public sealed class UndocNativeLoader : IDisposable
{
    private const string GitHubRepo = "iyulab/undoc";
    private const string LatestReleaseApi = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
    private const string CacheFolder = "undoc";
    private const string LibSubfolder = "lib";
    private const string BinSubfolder = "bin";
    private const string VersionFile = "version.txt";
    private const string AssetPrefix = "undoc-lib-"; // Use library, not CLI

    private static readonly Lazy<UndocNativeLoader> _instance = new(() => new UndocNativeLoader());
    public static UndocNativeLoader Instance => _instance.Value;

    private IntPtr _libraryHandle = IntPtr.Zero;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoaded;
    private bool _disposed;
    private string? _libraryPath;
    private string? _loadedVersion;
    private int _backgroundUpdateStarted; // 0 = not started, 1 = started
    private const string PendingFolder = "pending";

    // ========================================
    // Delegate types for native functions
    // ========================================

    /// <summary>const char* undoc_version()</summary>
    public delegate IntPtr VersionDelegate();

    /// <summary>const char* undoc_last_error()</summary>
    public delegate IntPtr LastErrorDelegate();

    /// <summary>void* undoc_parse_file(const char* path)</summary>
    public delegate IntPtr ParseFileDelegate(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    /// <summary>void* undoc_parse_bytes(const uint8_t* data, size_t len)</summary>
    public delegate IntPtr ParseBytesDelegate(
        IntPtr data,
        nuint len);

    /// <summary>void undoc_free_document(void* doc)</summary>
    public delegate void FreeDocumentDelegate(IntPtr doc);

    /// <summary>char* undoc_to_markdown(void* doc, int flags)</summary>
    public delegate IntPtr ToMarkdownDelegate(IntPtr doc, int flags);

    /// <summary>char* undoc_to_text(void* doc)</summary>
    public delegate IntPtr ToTextDelegate(IntPtr doc);

    /// <summary>char* undoc_to_json(void* doc, int format)</summary>
    public delegate IntPtr ToJsonDelegate(IntPtr doc, int format);

    /// <summary>char* undoc_plain_text(void* doc)</summary>
    public delegate IntPtr PlainTextDelegate(IntPtr doc);

    /// <summary>int undoc_section_count(void* doc)</summary>
    public delegate int SectionCountDelegate(IntPtr doc);

    /// <summary>int undoc_resource_count(void* doc)</summary>
    public delegate int ResourceCountDelegate(IntPtr doc);

    /// <summary>char* undoc_get_title(void* doc)</summary>
    public delegate IntPtr GetTitleDelegate(IntPtr doc);

    /// <summary>char* undoc_get_author(void* doc)</summary>
    public delegate IntPtr GetAuthorDelegate(IntPtr doc);

    /// <summary>void undoc_free_string(char* str)</summary>
    public delegate void FreeStringDelegate(IntPtr str);

    // ========================================
    // Resource Access API Delegates (v0.1.8+)
    // ========================================

    /// <summary>char* undoc_get_resource_ids(void* doc) - Returns JSON array of resource IDs</summary>
    public delegate IntPtr GetResourceIdsDelegate(IntPtr doc);

    /// <summary>char* undoc_get_resource_info(void* doc, const char* resource_id) - Returns JSON resource metadata</summary>
    public delegate IntPtr GetResourceInfoDelegate(
        IntPtr doc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string resourceId);

    /// <summary>uint8_t* undoc_get_resource_data(void* doc, const char* resource_id, size_t* out_len) - Returns binary data</summary>
    public delegate IntPtr GetResourceDataDelegate(
        IntPtr doc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string resourceId,
        out nuint outLen);

    /// <summary>void undoc_free_bytes(uint8_t* data, size_t len) - Frees binary data</summary>
    public delegate void FreeBytesDelegate(IntPtr data, nuint len);

    // ========================================
    // Function pointers
    // ========================================
    public VersionDelegate? Version { get; private set; }
    public LastErrorDelegate? LastError { get; private set; }
    public ParseFileDelegate? ParseFile { get; private set; }
    public ParseBytesDelegate? ParseBytes { get; private set; }
    public FreeDocumentDelegate? FreeDocument { get; private set; }
    public ToMarkdownDelegate? ToMarkdown { get; private set; }
    public ToTextDelegate? ToText { get; private set; }
    public ToJsonDelegate? ToJson { get; private set; }
    public PlainTextDelegate? PlainText { get; private set; }
    public SectionCountDelegate? SectionCount { get; private set; }
    public ResourceCountDelegate? ResourceCount { get; private set; }
    public GetTitleDelegate? GetTitle { get; private set; }
    public GetAuthorDelegate? GetAuthor { get; private set; }
    public FreeStringDelegate? FreeString { get; private set; }

    // Resource Access API (v0.1.8+)
    public GetResourceIdsDelegate? GetResourceIds { get; private set; }
    public GetResourceInfoDelegate? GetResourceInfo { get; private set; }
    public GetResourceDataDelegate? GetResourceData { get; private set; }
    public FreeBytesDelegate? FreeBytes { get; private set; }

    /// <summary>
    /// Gets whether the native library is loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// Gets the path to the loaded native library.
    /// </summary>
    public string? LibraryPath => _libraryPath;

    /// <summary>
    /// Gets the loaded library version (e.g., "0.1.0").
    /// </summary>
    public string? LoadedVersion => _loadedVersion;

    private UndocNativeLoader() { }

    /// <summary>
    /// Ensures the native library is loaded, downloading if necessary.
    /// Automatically triggers background update check after loading.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded)
        {
            ScheduleBackgroundUpdateCheck();
            return;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isLoaded) return;

            // Check for pending update first
            ApplyPendingUpdateIfExists();

            var libraryPath = await GetOrDownloadLibraryAsync(cancellationToken).ConfigureAwait(false);
            LoadLibrary(libraryPath);
            _libraryPath = libraryPath;
            _isLoaded = true;

            // Start background update check (fire-and-forget)
            ScheduleBackgroundUpdateCheck();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Schedules a background update check. Non-blocking fire-and-forget operation.
    /// </summary>
    private void ScheduleBackgroundUpdateCheck()
    {
        // Only start once per instance
        if (Interlocked.CompareExchange(ref _backgroundUpdateStarted, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false); // Delay to avoid startup contention
                await CheckAndDownloadUpdateInBackgroundAsync().ConfigureAwait(false);
            }
            catch
            {
                // Silently ignore background update failures
            }
        });
    }

    /// <summary>
    /// Background update check and download. Downloads to pending folder for next restart.
    /// </summary>
    private async Task CheckAndDownloadUpdateInBackgroundAsync()
    {
        var (isUpdateAvailable, latestVersion, _) = await CheckForUpdateAsync().ConfigureAwait(false);
        if (!isUpdateAvailable || string.IsNullOrEmpty(latestVersion))
            return;

        await DownloadPendingUpdateAsync(latestVersion, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads update to pending folder (staging area).
    /// </summary>
    private async Task DownloadPendingUpdateAsync(string version, CancellationToken cancellationToken)
    {
        var cacheDir = GetCacheDirectory();
        var pendingDir = Path.Combine(cacheDir, PendingFolder);
        Directory.CreateDirectory(pendingDir);

        var libraryName = GetPlatformLibraryName();
        if (libraryName == null) return;

        var (platformSuffix, archiveExt) = GetPlatformAssetInfo() ?? default;
        if (platformSuffix == null) return;

        var expectedAssetName = $"{AssetPrefix}{platformSuffix}";

        using var httpClient = CreateHttpClient();

        var releaseJson = await httpClient.GetStringAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        var release = JsonDocument.Parse(releaseJson);

        string? downloadUrl = null;
        foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name != null && name.StartsWith(expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (downloadUrl == null) return;

        var archivePath = Path.Combine(pendingDir, $"{expectedAssetName}{archiveExt}");
        var response = await httpClient.GetAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var fileStream = File.Create(archivePath))
        {
            await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        var extractDir = Path.Combine(pendingDir, "extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);

        Directory.CreateDirectory(extractDir);

        if (archiveExt == ".zip")
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        else if (archiveExt == ".tar.gz")
            await ExtractTarGzAsync(archivePath, extractDir, cancellationToken).ConfigureAwait(false);

        var libraryFile = Directory.GetFiles(extractDir, libraryName, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (libraryFile == null)
        {
            libraryFile = Directory.GetFiles(extractDir, "*undoc*", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.EndsWith(".dll") || f.EndsWith(".so") || f.EndsWith(".dylib"));
        }

        if (libraryFile == null) return;

        var targetPath = Path.Combine(pendingDir, libraryName);
        File.Copy(libraryFile, targetPath, overwrite: true);

        var versionPath = Path.Combine(pendingDir, VersionFile);
        await File.WriteAllTextAsync(versionPath, version, cancellationToken).ConfigureAwait(false);

        // Cleanup
        try
        {
            File.Delete(archivePath);
            Directory.Delete(extractDir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Applies pending update if exists (called on startup before loading library).
    /// </summary>
    private void ApplyPendingUpdateIfExists()
    {
        try
        {
            var cacheDir = GetCacheDirectory();
            var pendingDir = Path.Combine(cacheDir, PendingFolder);
            var libraryName = GetPlatformLibraryName();
            if (libraryName == null) return;

            var pendingLibrary = Path.Combine(pendingDir, libraryName);
            var pendingVersion = Path.Combine(pendingDir, VersionFile);

            if (!File.Exists(pendingLibrary) || !File.Exists(pendingVersion))
                return;

            var targetLibrary = Path.Combine(cacheDir, libraryName);
            var targetVersion = Path.Combine(cacheDir, VersionFile);

            // Move pending files to main cache
            File.Copy(pendingLibrary, targetLibrary, overwrite: true);
            File.Copy(pendingVersion, targetVersion, overwrite: true);

            // Clean up pending folder
            Directory.Delete(pendingDir, true);
        }
        catch
        {
            // Ignore errors during pending update application
        }
    }

    /// <summary>
    /// Tries to load the library synchronously if already downloaded.
    /// Returns false if download is needed.
    /// </summary>
    public bool TryLoadSync()
    {
        if (_isLoaded) return true;

        var libraryPath = GetCachedLibraryPath();
        if (libraryPath == null || !File.Exists(libraryPath))
            return false;

        _loadLock.Wait();
        try
        {
            if (_isLoaded) return true;

            LoadLibrary(libraryPath);
            _libraryPath = libraryPath;
            _isLoaded = true;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<string> GetOrDownloadLibraryAsync(CancellationToken cancellationToken)
    {
        var cachedPath = GetCachedLibraryPath();
        var versionPath = GetVersionFilePath();

        // Check if already cached with version info
        if (cachedPath != null && File.Exists(cachedPath) && File.Exists(versionPath))
        {
            _loadedVersion = await File.ReadAllTextAsync(versionPath, cancellationToken).ConfigureAwait(false);
            return cachedPath;
        }

        // Download from GitHub
        return await DownloadLibraryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string GetVersionFilePath()
    {
        var cacheDir = GetCacheDirectory();
        return Path.Combine(cacheDir, VersionFile);
    }

    /// <summary>
    /// Checks if a newer version is available on GitHub.
    /// </summary>
    /// <returns>Tuple of (isUpdateAvailable, latestVersion, currentVersion)</returns>
    public async Task<(bool IsUpdateAvailable, string? LatestVersion, string? CurrentVersion)> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            var releaseJson = await httpClient.GetStringAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
            var release = JsonDocument.Parse(releaseJson);
            var latestVersion = release.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');

            var versionPath = GetVersionFilePath();
            var currentVersion = File.Exists(versionPath)
                ? await File.ReadAllTextAsync(versionPath, cancellationToken).ConfigureAwait(false)
                : null;

            var isUpdateAvailable = !string.IsNullOrEmpty(latestVersion) &&
                                   !string.Equals(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase);

            return (isUpdateAvailable, latestVersion, currentVersion);
        }
        catch
        {
            return (false, null, _loadedVersion);
        }
    }

    /// <summary>
    /// Forces re-download of the latest version.
    /// </summary>
    public async Task UpdateAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Unload current library if loaded
            if (_libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(_libraryHandle);
                _libraryHandle = IntPtr.Zero;
                _isLoaded = false;
            }

            // Delete cached files
            var cacheDir = GetCacheDirectory();
            if (Directory.Exists(cacheDir))
            {
                var libraryName = GetPlatformLibraryName();
                if (libraryName != null)
                {
                    var libraryPath = Path.Combine(cacheDir, libraryName);
                    if (File.Exists(libraryPath))
                        File.Delete(libraryPath);
                }
                var versionPath = GetVersionFilePath();
                if (File.Exists(versionPath))
                    File.Delete(versionPath);
            }

            // Download and load new version
            var newLibraryPath = await DownloadLibraryAsync(cancellationToken).ConfigureAwait(false);
            LoadLibrary(newLibraryPath);
            _libraryPath = newLibraryPath;
            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FileFlux", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return httpClient;
    }

    private string? GetCachedLibraryPath()
    {
        var cacheDir = GetCacheDirectory();
        var libraryName = GetPlatformLibraryName();

        if (libraryName == null) return null;

        var path = Path.Combine(cacheDir, libraryName);
        return path;
    }

    private async Task<string> DownloadLibraryAsync(CancellationToken cancellationToken)
    {
        var cacheDir = GetCacheDirectory();
        Directory.CreateDirectory(cacheDir);

        var libraryName = GetPlatformLibraryName()
            ?? throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");

        var (platformSuffix, archiveExt) = GetPlatformAssetInfo()
            ?? throw new PlatformNotSupportedException($"Unsupported platform: {RuntimeInformation.OSDescription}");

        // Build expected asset name: undoc-lib-{platform}.{ext}
        var expectedAssetName = $"{AssetPrefix}{platformSuffix}";

        using var httpClient = CreateHttpClient();

        // Get latest release info
        var releaseJson = await httpClient.GetStringAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        var release = JsonDocument.Parse(releaseJson);

        // Extract version from release tag
        var version = release.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "unknown";

        string? downloadUrl = null;
        foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            // Match exactly: undoc-lib-{platform}.{ext}
            if (name != null && name.StartsWith(expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (downloadUrl == null)
        {
            throw new FileNotFoundException($"Could not find library asset '{expectedAssetName}' in latest release. " +
                                           $"Make sure the release contains undoc-lib-* archives (not undoc-cli-*).");
        }

        // Download the archive
        var archivePath = Path.Combine(cacheDir, $"{expectedAssetName}{archiveExt}");
        var response = await httpClient.GetAsync(downloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using (var fileStream = File.Create(archivePath))
        {
            await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }

        // Extract library from archive
        var extractDir = Path.Combine(cacheDir, "extract");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, true);

        Directory.CreateDirectory(extractDir);

        if (archiveExt == ".zip")
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else if (archiveExt == ".tar.gz")
        {
            await ExtractTarGzAsync(archivePath, extractDir, cancellationToken).ConfigureAwait(false);
        }

        // Find the library file in extracted contents
        var libraryFile = Directory.GetFiles(extractDir, libraryName, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (libraryFile == null)
        {
            // Try alternative: library might be directly in the archive
            libraryFile = Directory.GetFiles(extractDir, "*undoc*", SearchOption.AllDirectories)
                .FirstOrDefault(f => f.EndsWith(".dll") || f.EndsWith(".so") || f.EndsWith(".dylib"));
        }

        if (libraryFile == null)
        {
            throw new FileNotFoundException($"Library '{libraryName}' not found in downloaded archive");
        }

        var targetPath = Path.Combine(cacheDir, libraryName);
        File.Copy(libraryFile, targetPath, overwrite: true);

        // Save version info
        var versionPath = GetVersionFilePath();
        await File.WriteAllTextAsync(versionPath, version, cancellationToken).ConfigureAwait(false);
        _loadedVersion = version;

        // Cleanup
        try
        {
            File.Delete(archivePath);
            Directory.Delete(extractDir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }

        return targetPath;
    }

    private static async Task ExtractTarGzAsync(string archivePath, string extractDir, CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzipStream, extractDir, overwriteFiles: true, cancellationToken).ConfigureAwait(false);
    }

    private void LoadLibrary(string libraryPath)
    {
        _libraryHandle = NativeLibrary.Load(libraryPath);

        // Bind function pointers
        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_version", out var versionPtr))
            Version = Marshal.GetDelegateForFunctionPointer<VersionDelegate>(versionPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_last_error", out var lastErrorPtr))
            LastError = Marshal.GetDelegateForFunctionPointer<LastErrorDelegate>(lastErrorPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_parse_file", out var parseFilePtr))
            ParseFile = Marshal.GetDelegateForFunctionPointer<ParseFileDelegate>(parseFilePtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_parse_bytes", out var parseBytesPtr))
            ParseBytes = Marshal.GetDelegateForFunctionPointer<ParseBytesDelegate>(parseBytesPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_free_document", out var freeDocumentPtr))
            FreeDocument = Marshal.GetDelegateForFunctionPointer<FreeDocumentDelegate>(freeDocumentPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_to_markdown", out var toMarkdownPtr))
            ToMarkdown = Marshal.GetDelegateForFunctionPointer<ToMarkdownDelegate>(toMarkdownPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_to_text", out var toTextPtr))
            ToText = Marshal.GetDelegateForFunctionPointer<ToTextDelegate>(toTextPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_to_json", out var toJsonPtr))
            ToJson = Marshal.GetDelegateForFunctionPointer<ToJsonDelegate>(toJsonPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_plain_text", out var plainTextPtr))
            PlainText = Marshal.GetDelegateForFunctionPointer<PlainTextDelegate>(plainTextPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_section_count", out var sectionCountPtr))
            SectionCount = Marshal.GetDelegateForFunctionPointer<SectionCountDelegate>(sectionCountPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_resource_count", out var resourceCountPtr))
            ResourceCount = Marshal.GetDelegateForFunctionPointer<ResourceCountDelegate>(resourceCountPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_get_title", out var getTitlePtr))
            GetTitle = Marshal.GetDelegateForFunctionPointer<GetTitleDelegate>(getTitlePtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_get_author", out var getAuthorPtr))
            GetAuthor = Marshal.GetDelegateForFunctionPointer<GetAuthorDelegate>(getAuthorPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_free_string", out var freeStringPtr))
            FreeString = Marshal.GetDelegateForFunctionPointer<FreeStringDelegate>(freeStringPtr);

        // ========================================
        // Bind Resource Access API (v0.1.8+)
        // ========================================
        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_get_resource_ids", out var getResourceIdsPtr))
            GetResourceIds = Marshal.GetDelegateForFunctionPointer<GetResourceIdsDelegate>(getResourceIdsPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_get_resource_info", out var getResourceInfoPtr))
            GetResourceInfo = Marshal.GetDelegateForFunctionPointer<GetResourceInfoDelegate>(getResourceInfoPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_get_resource_data", out var getResourceDataPtr))
            GetResourceData = Marshal.GetDelegateForFunctionPointer<GetResourceDataDelegate>(getResourceDataPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "undoc_free_bytes", out var freeBytesPtr))
            FreeBytes = Marshal.GetDelegateForFunctionPointer<FreeBytesDelegate>(freeBytesPtr);
    }

    private static string GetCacheDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FileFlux", CacheFolder);
    }

    private static string? GetPlatformLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "undoc.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "libundoc.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libundoc.dylib";
        return null;
    }

    private static (string AssetName, string Extension)? GetPlatformAssetInfo()
    {
        var arch = RuntimeInformation.OSArchitecture;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return arch == Architecture.X64 ? ("x86_64-pc-windows-msvc", ".zip") : null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return arch == Architecture.X64 ? ("x86_64-unknown-linux-gnu", ".tar.gz") : null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return arch switch
            {
                Architecture.X64 => ("x86_64-apple-darwin", ".tar.gz"),
                Architecture.Arm64 => ("aarch64-apple-darwin", ".tar.gz"),
                _ => null
            };
        }

        return null;
    }

    /// <summary>
    /// Gets the last error message from the native library.
    /// </summary>
    public string GetLastError()
    {
        if (LastError == null) return "Library not loaded";

        var errorPtr = LastError();
        if (errorPtr == IntPtr.Zero) return "Unknown error";

        return Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error";
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_libraryHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_libraryHandle);
            _libraryHandle = IntPtr.Zero;
        }

        _loadLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Markdown rendering flags for undoc.
/// </summary>
[Flags]
public enum UndocMarkdownFlags
{
    /// <summary>No flags.</summary>
    None = 0,
    /// <summary>Include YAML frontmatter with metadata.</summary>
    Frontmatter = 1,
    /// <summary>Escape special Markdown characters.</summary>
    EscapeSpecial = 2,
    /// <summary>Add blank lines between paragraphs.</summary>
    ParagraphSpacing = 4
}

/// <summary>
/// JSON format options for undoc.
/// </summary>
public enum UndocJsonFormat
{
    /// <summary>Pretty-printed JSON with indentation.</summary>
    Pretty = 0,
    /// <summary>Compact JSON without whitespace.</summary>
    Compact = 1
}
