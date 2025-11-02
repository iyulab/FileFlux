using FileFlux;
using FileFlux.Domain;
using System.IO.Compression;
using System.Text;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// ZIP 아카이브 처리를 위한 문서 Reader
/// 아카이브 내 지원되는 문서 파일들을 자동으로 추출하고 처리
/// </summary>
public class ZipArchiveReader : IDocumentReader
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly ZipProcessingOptions _options;

    public string ReaderType => "ZipArchiveReader";
    public IEnumerable<string> SupportedExtensions => new[] { ".zip" };

    public ZipArchiveReader(IDocumentReaderFactory readerFactory, ZipProcessingOptions? options = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _options = options ?? new ZipProcessingOptions();
    }

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".zip";
    }

    public async Task<RawContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"ZIP file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        var fileInfo = new FileInfo(filePath);

        // 1. ZIP 파일 크기 사전 검증
        if (fileInfo.Length > _options.MaxZipFileSize)
        {
            throw new InvalidOperationException(
                $"ZIP file too large: {fileInfo.Length} bytes (max: {_options.MaxZipFileSize} bytes)");
        }

        var startTime = DateTime.UtcNow;
        var warnings = new List<string>();
        var hints = new Dictionary<string, object>();
        string? tempDir = null;

        try
        {
            // 2. ZIP 내용 사전 검증
            var validation = await ValidateZipArchiveAsync(filePath, warnings, cancellationToken);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"ZIP validation failed: {validation.ErrorMessage}");
            }

            hints["ArchiveName"] = Path.GetFileName(filePath);
            hints["TotalEntries"] = validation.TotalEntries;
            hints["SupportedFiles"] = validation.SupportedFiles.Count;
            hints["TotalCompressedSize"] = validation.TotalCompressedSize;
            hints["EstimatedUncompressedSize"] = validation.EstimatedUncompressedSize;
            hints["CompressionRatio"] = validation.CompressionRatio;

            // 3. 안전한 압축 해제
            tempDir = CreateTemporaryDirectory();
            var extractedFiles = await ExtractSupportedFilesAsync(
                filePath, validation.SupportedFiles, tempDir, cancellationToken);

            hints["ExtractedFiles"] = extractedFiles.Count;

            // 4. 추출된 파일 처리
            var processedContents = await ProcessExtractedFilesAsync(
                extractedFiles, cancellationToken);

            // 5. 결과 통합
            var combinedText = CombineExtractedTexts(processedContents, out var fileList);
            hints["ProcessedFileList"] = fileList;
            hints["SuccessfulProcessing"] = processedContents.Count;

            var duration = DateTime.UtcNow - startTime;

            return new RawContent
            {
                Text = combinedText,
                File = new SourceFileInfo
                {
                    Name = Path.GetFileName(filePath),
                    Extension = ".zip",
                    Size = fileInfo.Length,
                },
                Hints = hints,
                Warnings = warnings,
                ReaderType = ReaderType,
                Duration = duration,
                Status = ProcessingStatus.Completed
            };
        }
        catch (OperationCanceledException)
        {
            warnings.Add("Processing cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ZIP archive processing failed: {ex.Message}", ex);
        }
        finally
        {
            // 6. 임시 디렉토리 정리
            if (tempDir != null)
            {
                CleanupTemporaryDirectory(tempDir, warnings);
            }
        }
    }

    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        // Stream을 임시 파일로 저장한 후 처리
        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.zip");

        try
        {
            // Stream을 임시 파일로 복사
            using (var fileStream = File.Create(tempZipPath))
            {
                await stream.CopyToAsync(fileStream, cancellationToken);
            }

            // 임시 파일을 사용하여 처리
            return await ExtractAsync(tempZipPath, cancellationToken);
        }
        finally
        {
            // 임시 ZIP 파일 정리
            if (File.Exists(tempZipPath))
            {
                try { File.Delete(tempZipPath); }
                catch { /* 정리 실패는 무시 */ }
            }
        }
    }

    /// <summary>
    /// ZIP 아카이브 사전 검증
    /// </summary>
    private async Task<ZipValidationResult> ValidateZipArchiveAsync(
        string zipPath,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var result = new ZipValidationResult();

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var supportedExtensions = _readerFactory.GetSupportedExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                result.TotalEntries++;

                // 디렉토리 엔트리는 스킵
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    continue;

                // Path Traversal 공격 방지
                if (entry.FullName.Contains("..") || Path.IsPathRooted(entry.FullName))
                {
                    warnings.Add($"Skipping potentially unsafe entry: {entry.FullName}");
                    continue;
                }

                result.TotalCompressedSize += entry.CompressedLength;
                result.EstimatedUncompressedSize += entry.Length;

                var extension = Path.GetExtension(entry.Name).ToLowerInvariant();

                // 지원되는 파일 형식인지 확인
                if (supportedExtensions.Contains(extension))
                {
                    // 파일 크기 제한 체크
                    if (entry.Length > _options.MaxIndividualFileSize)
                    {
                        warnings.Add($"File too large, skipping: {entry.FullName} ({entry.Length} bytes)");
                        continue;
                    }

                    result.SupportedFiles.Add(entry.FullName);
                }
                else
                {
                    result.UnsupportedFiles.Add(entry.FullName);
                }
            }

            // Zip Bomb 검사
            if (result.TotalCompressedSize > 0)
            {
                result.CompressionRatio = (double)result.EstimatedUncompressedSize / result.TotalCompressedSize;
                if (result.CompressionRatio > _options.MaxCompressionRatio)
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Suspicious compression ratio detected (Zip Bomb): {result.CompressionRatio:F2}x (max: {_options.MaxCompressionRatio}x)";
                    return;
                }
            }

            // 총 압축 해제 크기 제한
            if (result.EstimatedUncompressedSize > _options.MaxExtractedSize)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Extracted size would exceed limit: {result.EstimatedUncompressedSize} bytes (max: {_options.MaxExtractedSize} bytes)";
                return;
            }

            // 파일 개수 제한
            if (result.SupportedFiles.Count > _options.MaxFileCount)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Too many files: {result.SupportedFiles.Count} (max: {_options.MaxFileCount})";
                return;
            }

            // 처리 가능한 파일이 없는 경우
            if (result.SupportedFiles.Count == 0)
            {
                result.IsValid = false;
                result.ErrorMessage = "No supported file formats found in archive";
                return;
            }

            result.IsValid = true;

        }, cancellationToken);

        return result;
    }

    /// <summary>
    /// 지원되는 파일만 안전하게 압축 해제
    /// </summary>
    private async Task<List<string>> ExtractSupportedFilesAsync(
        string zipPath,
        List<string> supportedEntries,
        string targetDir,
        CancellationToken cancellationToken)
    {
        var extractedFiles = new List<string>();

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);

            foreach (var entryName in supportedEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = archive.GetEntry(entryName);
                if (entry == null) continue;

                // 안전한 대상 경로 생성
                var safeFileName = Path.GetFileName(entry.Name);
                var targetPath = Path.Combine(targetDir, safeFileName);

                // 동일한 파일명이 있을 경우 고유한 이름 생성
                if (File.Exists(targetPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
                    var extension = Path.GetExtension(safeFileName);
                    targetPath = Path.Combine(targetDir, $"{nameWithoutExt}_{Guid.NewGuid():N}{extension}");
                }

                // 파일 추출
                entry.ExtractToFile(targetPath, overwrite: false);
                extractedFiles.Add(targetPath);
            }

        }, cancellationToken);

        return extractedFiles;
    }

    /// <summary>
    /// 추출된 파일들을 각각의 적절한 Reader로 처리
    /// </summary>
    private async Task<List<RawContent>> ProcessExtractedFilesAsync(
        List<string> filePaths,
        CancellationToken cancellationToken)
    {
        var results = new List<RawContent>();

        if (_options.EnableParallelProcessing && filePaths.Count > 1)
        {
            // 병렬 처리
            var tasks = filePaths.Select(async filePath =>
            {
                try
                {
                    var reader = _readerFactory.GetReader(Path.GetFileName(filePath));
                    if (reader != null && reader.ReaderType != ReaderType) // 자기 자신은 제외 (중첩 ZIP 방지)
                    {
                        return await reader.ExtractAsync(filePath, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    // 개별 파일 처리 실패는 로깅만 하고 계속 진행
                    return new RawContent
                    {
                        File = new SourceFileInfo { Name = Path.GetFileName(filePath) },
                        Status = ProcessingStatus.Failed,
                        Errors = new List<ProcessingError>
                        {
                            new ProcessingError
                            {
                                Code = "FILE_PROCESSING_ERROR",
                                Message = ex.Message,
                                Stage = "FileExtraction"
                            }
                        }
                    };
                }
                return null;
            });

            var processedResults = await Task.WhenAll(tasks);
            results.AddRange(processedResults.Where(r => r != null)!);
        }
        else
        {
            // 순차 처리
            foreach (var filePath in filePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var reader = _readerFactory.GetReader(Path.GetFileName(filePath));
                    if (reader != null && reader.ReaderType != ReaderType)
                    {
                        var content = await reader.ExtractAsync(filePath, cancellationToken);
                        results.Add(content);
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new RawContent
                    {
                        File = new SourceFileInfo { Name = Path.GetFileName(filePath) },
                        Status = ProcessingStatus.Failed,
                        Errors = new List<ProcessingError>
                        {
                            new ProcessingError
                            {
                                Code = "FILE_PROCESSING_ERROR",
                                Message = ex.Message,
                                Stage = "FileExtraction"
                            }
                        }
                    });
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 추출된 텍스트들을 통합
    /// </summary>
    private string CombineExtractedTexts(List<RawContent> contents, out List<string> fileList)
    {
        var builder = new StringBuilder();
        fileList = new List<string>();

        foreach (var content in contents)
        {
            if (content.Status == ProcessingStatus.Completed && !string.IsNullOrWhiteSpace(content.Text))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("---"); // 파일 구분자
                    builder.AppendLine();
                }

                builder.AppendLine($"# File: {content.File.Name}");
                builder.AppendLine();
                builder.AppendLine(content.Text);

                fileList.Add(content.File.Name);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 안전한 임시 디렉토리 생성
    /// </summary>
    private string CreateTemporaryDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "FileFlux_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    /// <summary>
    /// 임시 디렉토리 정리
    /// </summary>
    private void CleanupTemporaryDirectory(string directoryPath, List<string> warnings)
    {
        if (!Directory.Exists(directoryPath))
            return;

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to cleanup temporary directory: {ex.Message}");

            // 재시도 (파일 잠금 대응)
            try
            {
                Thread.Sleep(100);
                Directory.Delete(directoryPath, recursive: true);
            }
            catch
            {
                // 최종 실패는 경고로만 남김
            }
        }
    }
}

/// <summary>
/// ZIP 검증 결과
/// </summary>
internal class ZipValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int TotalEntries { get; set; }
    public List<string> SupportedFiles { get; set; } = new();
    public List<string> UnsupportedFiles { get; set; } = new();
    public long TotalCompressedSize { get; set; }
    public long EstimatedUncompressedSize { get; set; }
    public double CompressionRatio { get; set; }
}

/// <summary>
/// ZIP 처리 옵션
/// </summary>
public class ZipProcessingOptions
{
    /// <summary>최대 ZIP 파일 크기 (bytes)</summary>
    public long MaxZipFileSize { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>최대 압축 해제 크기 (bytes)</summary>
    public long MaxExtractedSize { get; set; } = 1024 * 1024 * 1024; // 1GB

    /// <summary>개별 파일 최대 크기 (bytes)</summary>
    public long MaxIndividualFileSize { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>최대 파일 개수</summary>
    public int MaxFileCount { get; set; } = 1000;

    /// <summary>최대 압축 비율 (Zip Bomb 방지)</summary>
    public int MaxCompressionRatio { get; set; } = 100;

    /// <summary>병렬 처리 활성화</summary>
    public bool EnableParallelProcessing { get; set; } = true;
}
