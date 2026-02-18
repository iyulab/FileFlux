using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileFlux.Core.Infrastructure.Interop;

/// <summary>
/// Lazy loader for unhwp native library.
/// Downloads platform-specific binary from GitHub releases on first use.
/// </summary>
/// <remarks>
/// Cache structure:
/// <code>
/// %LOCALAPPDATA%/FileFlux/unhwp/
/// ├── lib/     - Native library only (unhwp.dll, libunhwp.so, libunhwp.dylib)
/// └── bin/     - CLI executable (unhwp.exe, unhwp)
/// </code>
/// </remarks>
public sealed class UnhwpNativeLoader : IDisposable
{
    private const string GitHubRepo = "iyulab/unhwp";
    private const string LatestReleaseApi = $"https://api.github.com/repos/{GitHubRepo}/releases/latest";
    private const string CacheFolder = "unhwp";
    private const string LibSubfolder = "lib";
    private const string BinSubfolder = "bin";
    private const string VersionFile = "version.txt";
    private const string AssetPrefix = "unhwp-lib-"; // Use library, not CLI

    private static readonly Lazy<UnhwpNativeLoader> _instance = new(() => new UnhwpNativeLoader());
    public static UnhwpNativeLoader Instance => _instance.Value;

    private IntPtr _libraryHandle = IntPtr.Zero;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _isLoaded;
    private bool _disposed;
    private string? _libraryPath;
    private string? _loadedVersion;
    private int _backgroundUpdateStarted; // 0 = not started, 1 = started
    private const string PendingFolder = "pending";

    // ========================================
    // Structures
    // ========================================

    /// <summary>
    /// Represents image data returned from the native library.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ImageData
    {
        public IntPtr Name;      // Null-terminated UTF-8 string
        public IntPtr Data;      // Binary data pointer
        public nuint DataLen;    // Length in bytes
    }

    // ========================================
    // Delegate types for native functions - Simple API
    // ========================================

    // int unhwp_to_markdown(const char* path, char** out_markdown, char** out_error)
    public delegate int ToMarkdownFn(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr outMarkdown,
        out IntPtr outError);

    // int unhwp_to_markdown_with_cleanup(const char* path, char** out_markdown, char** out_error)
    public delegate int ToMarkdownWithCleanupFn(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr outMarkdown,
        out IntPtr outError);

    // int unhwp_bytes_to_markdown(const uint8_t* data, size_t data_len, char** out_markdown, char** out_error)
    public delegate int BytesToMarkdownFn(
        IntPtr data,
        nuint dataLen,
        out IntPtr outMarkdown,
        out IntPtr outError);

    // void unhwp_free_string(char* ptr)
    public delegate void FreeStringFn(IntPtr ptr);

    // int unhwp_detect_format(const char* path)
    public delegate int DetectFormatFn(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    // const char* unhwp_version()
    public delegate IntPtr VersionFn();

    // ========================================
    // Structured Result API Delegates
    // ========================================

    // IntPtr unhwp_parse(const char* path, RenderOptions* renderOptions, CleanupOptions* cleanupOptions)
    public delegate IntPtr ParseFn(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr renderOptions,
        IntPtr cleanupOptions);

    // IntPtr unhwp_parse_bytes(const uint8_t* data, size_t data_len, RenderOptions* renderOptions, CleanupOptions* cleanupOptions)
    public delegate IntPtr ParseBytesFn(
        IntPtr data,
        nuint dataLen,
        IntPtr renderOptions,
        IntPtr cleanupOptions);

    // IntPtr unhwp_result_get_markdown(IntPtr result)
    public delegate IntPtr ResultGetMarkdownFn(IntPtr result);

    // IntPtr unhwp_result_get_text(IntPtr result)
    public delegate IntPtr ResultGetTextFn(IntPtr result);

    // int unhwp_result_get_image_count(IntPtr result)
    public delegate int ResultGetImageCountFn(IntPtr result);

    // int unhwp_result_get_image(IntPtr result, int index, ImageData* outImage)
    public delegate int ResultGetImageFn(IntPtr result, int index, out ImageData outImage);

    // int unhwp_result_get_section_count(IntPtr result)
    public delegate int ResultGetSectionCountFn(IntPtr result);

    // int unhwp_result_get_paragraph_count(IntPtr result)
    public delegate int ResultGetParagraphCountFn(IntPtr result);

    // IntPtr unhwp_result_get_raw_content(IntPtr result)
    public delegate IntPtr ResultGetRawContentFn(IntPtr result);

    // IntPtr unhwp_result_get_error(IntPtr result)
    public delegate IntPtr ResultGetErrorFn(IntPtr result);

    // void unhwp_result_free(IntPtr result)
    public delegate void ResultFreeFn(IntPtr result);

    // ========================================
    // Function pointers - Simple API
    // ========================================
    public ToMarkdownFn? ToMarkdown { get; private set; }
    public ToMarkdownWithCleanupFn? ToMarkdownWithCleanup { get; private set; }
    public BytesToMarkdownFn? BytesToMarkdown { get; private set; }
    public FreeStringFn? FreeString { get; private set; }
    public DetectFormatFn? DetectFormat { get; private set; }
    public VersionFn? Version { get; private set; }

    // ========================================
    // Function pointers - Structured Result API
    // ========================================
    public ParseFn? Parse { get; private set; }
    public ParseBytesFn? ParseBytes { get; private set; }
    public ResultGetMarkdownFn? ResultGetMarkdown { get; private set; }
    public ResultGetTextFn? ResultGetText { get; private set; }
    public ResultGetImageCountFn? ResultGetImageCount { get; private set; }
    public ResultGetImageFn? ResultGetImage { get; private set; }
    public ResultGetSectionCountFn? ResultGetSectionCount { get; private set; }
    public ResultGetParagraphCountFn? ResultGetParagraphCount { get; private set; }
    public ResultGetRawContentFn? ResultGetRawContent { get; private set; }
    public ResultGetErrorFn? ResultGetError { get; private set; }
    public ResultFreeFn? ResultFree { get; private set; }

    /// <summary>
    /// Gets whether the native library is loaded.
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// Gets the path to the loaded native library.
    /// </summary>
    public string? LibraryPath => _libraryPath;

    /// <summary>
    /// Gets the loaded library version (e.g., "0.1.6").
    /// </summary>
    public string? LoadedVersion => _loadedVersion;

    private UnhwpNativeLoader() { }

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
    private static async Task DownloadPendingUpdateAsync(string version, CancellationToken cancellationToken)
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
            libraryFile = Directory.GetFiles(extractDir, "*unhwp*", SearchOption.AllDirectories)
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
    private static void ApplyPendingUpdateIfExists()
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

    private static string? GetCachedLibraryPath()
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

        // Build expected asset name: unhwp-lib-{platform}.{ext}
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
            // Match exactly: unhwp-lib-{platform}.{ext}
            if (name != null && name.StartsWith(expectedAssetName, StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }

        if (downloadUrl == null)
        {
            throw new FileNotFoundException($"Could not find library asset '{expectedAssetName}' in latest release. " +
                                           $"Make sure the release contains unhwp-lib-* archives (not unhwp-cli-*).");
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
            libraryFile = Directory.GetFiles(extractDir, "*unhwp*", SearchOption.AllDirectories)
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

        // ========================================
        // Bind Simple API function pointers
        // ========================================
        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_to_markdown", out var toMarkdownPtr))
            ToMarkdown = Marshal.GetDelegateForFunctionPointer<ToMarkdownFn>(toMarkdownPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_to_markdown_with_cleanup", out var toMarkdownWithCleanupPtr))
            ToMarkdownWithCleanup = Marshal.GetDelegateForFunctionPointer<ToMarkdownWithCleanupFn>(toMarkdownWithCleanupPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_bytes_to_markdown", out var bytesToMarkdownPtr))
            BytesToMarkdown = Marshal.GetDelegateForFunctionPointer<BytesToMarkdownFn>(bytesToMarkdownPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_free_string", out var freeStringPtr))
            FreeString = Marshal.GetDelegateForFunctionPointer<FreeStringFn>(freeStringPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_detect_format", out var detectFormatPtr))
            DetectFormat = Marshal.GetDelegateForFunctionPointer<DetectFormatFn>(detectFormatPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_version", out var versionPtr))
            Version = Marshal.GetDelegateForFunctionPointer<VersionFn>(versionPtr);

        // ========================================
        // Bind Structured Result API function pointers
        // ========================================
        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_parse", out var parsePtr))
            Parse = Marshal.GetDelegateForFunctionPointer<ParseFn>(parsePtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_parse_bytes", out var parseBytesPtr))
            ParseBytes = Marshal.GetDelegateForFunctionPointer<ParseBytesFn>(parseBytesPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_markdown", out var resultGetMarkdownPtr))
            ResultGetMarkdown = Marshal.GetDelegateForFunctionPointer<ResultGetMarkdownFn>(resultGetMarkdownPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_text", out var resultGetTextPtr))
            ResultGetText = Marshal.GetDelegateForFunctionPointer<ResultGetTextFn>(resultGetTextPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_image_count", out var resultGetImageCountPtr))
            ResultGetImageCount = Marshal.GetDelegateForFunctionPointer<ResultGetImageCountFn>(resultGetImageCountPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_image", out var resultGetImagePtr))
            ResultGetImage = Marshal.GetDelegateForFunctionPointer<ResultGetImageFn>(resultGetImagePtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_section_count", out var resultGetSectionCountPtr))
            ResultGetSectionCount = Marshal.GetDelegateForFunctionPointer<ResultGetSectionCountFn>(resultGetSectionCountPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_paragraph_count", out var resultGetParagraphCountPtr))
            ResultGetParagraphCount = Marshal.GetDelegateForFunctionPointer<ResultGetParagraphCountFn>(resultGetParagraphCountPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_raw_content", out var resultGetRawContentPtr))
            ResultGetRawContent = Marshal.GetDelegateForFunctionPointer<ResultGetRawContentFn>(resultGetRawContentPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_get_error", out var resultGetErrorPtr))
            ResultGetError = Marshal.GetDelegateForFunctionPointer<ResultGetErrorFn>(resultGetErrorPtr);

        if (NativeLibrary.TryGetExport(_libraryHandle, "unhwp_result_free", out var resultFreePtr))
            ResultFree = Marshal.GetDelegateForFunctionPointer<ResultFreeFn>(resultFreePtr);
    }

    private static string GetCacheDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FileFlux", CacheFolder);
    }

    private static string? GetPlatformLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "unhwp.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "libunhwp.so";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libunhwp.dylib";
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
/// Error codes from unhwp native library.
/// </summary>
public static class UnhwpErrorCodes
{
    public const int Ok = 0;
    public const int FileNotFound = -1;
    public const int ParseError = -2;
    public const int RenderError = -3;
    public const int InvalidFormat = -4;
}

/// <summary>
/// HWP document format types.
/// </summary>
public enum HwpFormat
{
    Unknown = 0,
    Hwp5 = 1,    // HWP 5.0 (OLE compound)
    Hwpx = 2     // HWPX (XML/ZIP based)
}
