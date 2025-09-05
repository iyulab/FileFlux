using FileFlux;
using FileFlux.Exceptions;
using FileFlux.Domain;
using System.Buffers;
using System.Text;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 텍스트 파일 전용 Reader - 순수 텍스트 추출에만 집중
/// LLM 기능 없이 파일 내용을 그대로 추출
/// 진행률 추적 기능 지원
/// </summary>
public class TextDocumentReader : IDocumentReader
{
    public IEnumerable<string> SupportedExtensions => new[] { ".txt", ".md", ".tmp" };

    public string ReaderType => "TextReader";
    private static readonly string[] separator = new[] { "\n\n", "\r\n\r\n" };

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public async Task<RawDocumentContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        if (!CanRead(filePath))
            throw new NotSupportedException($"File format not supported: {Path.GetExtension(filePath)}");

        try
        {
            // 파일 정보 수집
            var fileInfo = new FileInfo(filePath);

            // 텍스트 내용 읽기 (인코딩 자동 감지)
            var text = await ReadTextWithEncodingDetectionAsync(filePath, cancellationToken).ConfigureAwait(false);

            // 기본 구조적 힌트 감지 (LLM 없이 가능한 단순한 패턴)
            var structuralHints = DetectBasicStructuralHints(text, Path.GetExtension(filePath));

            // 추출 경고 수집
            var warnings = new List<string>();
            if (text.Length < 30)
            {
                warnings.Add($"Text content is very short ({text.Length} characters). May not be suitable for meaningful processing.");
            }

            // 순수 텍스트 추출 결과 반환
            return new RawDocumentContent
            {
                Text = text,
                FileInfo = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    FileExtension = Path.GetExtension(filePath).ToLowerInvariant(),
                    FileSize = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = ReaderType
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(filePath, $"Failed to read text file: {ex.Message}", ex);
        }
    }

    public async Task<RawDocumentContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be null or empty", nameof(fileName));

        try
        {
            // 스트림에서 텍스트 읽기
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            // 기본 구조적 힌트 감지
            var structuralHints = DetectBasicStructuralHints(text, Path.GetExtension(fileName));

            // 추출 경고 수집
            var warnings = new List<string>();
            if (text.Length < 30)
            {
                warnings.Add($"Text content is very short ({text.Length} characters). May not be suitable for meaningful processing.");
            }

            // 순수 텍스트 추출 결과 반환
            return new RawDocumentContent
            {
                Text = text,
                FileInfo = new FileMetadata
                {
                    FileName = fileName,
                    FileExtension = Path.GetExtension(fileName).ToLowerInvariant(),
                    FileSize = stream.Length,
                    CreatedAt = DateTime.Now, // 스트림에서는 불명
                    ModifiedAt = DateTime.Now,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = ReaderType
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            throw new DocumentProcessingException(fileName, $"Failed to read text from stream: {ex.Message}", ex);
        }
    }

    private static async Task<string> ReadTextWithEncodingDetectionAsync(string filePath, CancellationToken cancellationToken)
    {
        const int bufferSize = 8192; // 8KB 버퍼 사용
        var charPool = ArrayPool<char>.Shared;
        var bytePool = ArrayPool<byte>.Shared;
        
        var encoding = Encoding.UTF8;
        var hasBom = false;
        
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        
        // BOM 감지를 위해 처음 몇 바이트 읽기
        var bomBuffer = bytePool.Rent(4);
        try
        {
            var bomBytesRead = await fileStream.ReadAsync(bomBuffer.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            
            if (bomBytesRead >= 3 && bomBuffer[0] == 0xEF && bomBuffer[1] == 0xBB && bomBuffer[2] == 0xBF)
            {
                hasBom = true;
                encoding = Encoding.UTF8;
                // BOM 이후부터 읽기 위해 스트림 위치 조정
                fileStream.Seek(3, SeekOrigin.Begin);
            }
            else
            {
                // BOM이 없으면 처음부터 다시 읽기
                fileStream.Seek(0, SeekOrigin.Begin);
            }
        }
        finally
        {
            bytePool.Return(bomBuffer);
        }
        
        // 스트리밍 방식으로 텍스트 읽기
        using var reader = new StreamReader(fileStream, encoding, !hasBom, bufferSize, leaveOpen: false);
        
        // StringBuilder 풀링을 통한 메모리 효율성
        var stringBuilder = new StringBuilder();
        var charBuffer = charPool.Rent(bufferSize);
        
        try
        {
            int charsRead;
            while ((charsRead = await reader.ReadAsync(charBuffer, 0, charBuffer.Length).ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stringBuilder.Append(charBuffer, 0, charsRead);
            }
            
            return stringBuilder.ToString();
        }
        catch (DecoderFallbackException) when (!hasBom && encoding == Encoding.UTF8)
        {
            // UTF-8 디코딩 실패 시 시스템 기본 인코딩으로 재시도
            fileStream.Seek(0, SeekOrigin.Begin);
            using var fallbackReader = new StreamReader(fileStream, Encoding.Default, false, bufferSize);
            return await fallbackReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            charPool.Return(charBuffer);
        }
    }

    /// <summary>
    /// LLM 없이 감지 가능한 기본적인 구조적 힌트
    /// </summary>
    private static Dictionary<string, object> DetectBasicStructuralHints(string text, string fileExtension)
    {
        var hints = new Dictionary<string, object>();

        // 파일 형식별 기본 힌트
        hints["file_type"] = fileExtension.ToLowerInvariant() switch
        {
            ".md" => "markdown",
            ".txt" => "plain_text",
            ".tmp" => "plain_text",
            _ => "unknown"
        };

        // 마크다운 특화 힌트
        if (fileExtension.Equals(".md", StringComparison.InvariantCultureIgnoreCase))
        {
            var lines = text.Split('\n', StringSplitOptions.None);
            var headerLines = lines.Where(line => line.TrimStart().StartsWith('#')).ToList();

            if (headerLines.Count != 0)
            {
                hints["has_headers"] = true;
                hints["header_count"] = headerLines.Count;
                hints["top_level_headers"] = headerLines.Where(h => h.TrimStart().StartsWith("# ")).Count();
            }

            // 코드 블록 감지
            var codeBlockCount = text.Split("```").Length - 1;
            if (codeBlockCount > 0)
            {
                hints["has_code_blocks"] = true;
                hints["code_block_count"] = codeBlockCount / 2; // 시작/끝 쌍
            }
        }

        // 일반적인 구조적 패턴
        var lineCount = text.Split('\n').Length;
        var paragraphCount = text.Split(separator, StringSplitOptions.RemoveEmptyEntries).Length;

        hints["line_count"] = lineCount;
        hints["paragraph_count"] = paragraphCount;
        hints["character_count"] = text.Length;
        hints["word_count"] = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

        return hints;
    }

    /// <summary>
    /// 진행률을 추적하면서 문서를 읽습니다
    /// </summary>
    public async Task<RawDocumentContent> ExtractWithProgressAsync(
        string filePath,
        Action<ProcessingProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}");

        if (!CanRead(filePath))
            throw new NotSupportedException($"File format not supported: {Path.GetExtension(filePath)}");

        var fileName = Path.GetFileName(filePath);
        var startTime = DateTime.UtcNow;

        try
        {
            // 파일 크기 확인
            var fileInfo = new FileInfo(filePath);
            var totalBytes = fileInfo.Length;

            // 읽기 시작 진행률 보고
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = filePath,
                Stage = ProcessingStage.Reading,
                OverallProgress = 0.0,
                StageProgress = 0.0,
                Message = $"파일 읽기 시작: {fileName}",
                TotalBytes = totalBytes,
                ProcessedBytes = 0,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            // 청크 단위로 파일 읽기 (큰 파일 대응)
            const int bufferSize = 4096;
            var buffer = new byte[bufferSize];
            var allBytes = new List<byte>();

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);

            int totalRead = 0;
            int bytesRead;

            while ((bytesRead = await fileStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                allBytes.AddRange(buffer.Take(bytesRead));
                totalRead += bytesRead;

                // 진행률 업데이트 (읽기 단계)
                var readProgress = totalBytes > 0 ? (double)totalRead / totalBytes : 1.0;
                progressCallback?.Invoke(new ProcessingProgress
                {
                    FilePath = filePath,
                    Stage = ProcessingStage.Reading,
                    OverallProgress = readProgress * 0.5, // 전체의 50%는 읽기 단계
                    StageProgress = readProgress,
                    Message = $"파일 읽는 중: {totalRead:N0}/{totalBytes:N0} bytes",
                    TotalBytes = totalBytes,
                    ProcessedBytes = totalRead,
                    StartTime = startTime,
                    CurrentTime = DateTime.UtcNow
                });

                cancellationToken.ThrowIfCancellationRequested();
            }

            // 인코딩 감지 및 변환 단계
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = filePath,
                Stage = ProcessingStage.Extracting,
                OverallProgress = 0.5,
                StageProgress = 0.0,
                Message = "텍스트 인코딩 감지 및 변환 중",
                TotalBytes = totalBytes,
                ProcessedBytes = totalRead,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            var bytes = allBytes.ToArray();
            var text = await Task.Run(() => DecodeTextWithEncoding(bytes), cancellationToken);

            // 구조적 힌트 감지 단계
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = filePath,
                Stage = ProcessingStage.Extracting,
                OverallProgress = 0.8,
                StageProgress = 0.6,
                Message = "구조적 패턴 분석 중",
                TotalBytes = totalBytes,
                ProcessedBytes = totalRead,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            var structuralHints = DetectBasicStructuralHints(text, Path.GetExtension(filePath));

            // 추출 경고 수집
            var warnings = new List<string>();
            if (text.Length < 30)
            {
                warnings.Add($"Text content is very short ({text.Length} characters). May not be suitable for meaningful processing.");
            }

            // 완료 진행률 보고
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = filePath,
                Stage = ProcessingStage.Extracting,
                OverallProgress = 1.0,
                StageProgress = 1.0,
                Message = $"텍스트 추출 완료: {text.Length:N0}자",
                TotalBytes = totalBytes,
                ProcessedBytes = totalRead,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            return new RawDocumentContent
            {
                Text = text,
                FileInfo = new FileMetadata
                {
                    FileName = fileInfo.Name,
                    FileExtension = Path.GetExtension(filePath).ToLowerInvariant(),
                    FileSize = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    ModifiedAt = fileInfo.LastWriteTimeUtc,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = ReaderType
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            progressCallback?.Invoke(ProcessingProgress.Factory.CreateError(filePath, ex.Message));
            throw new DocumentProcessingException(filePath, $"Failed to read text file: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 진행률을 추적하면서 스트림에서 문서를 읽습니다
    /// </summary>
    public async Task<RawDocumentContent> ExtractWithProgressAsync(
        Stream stream,
        string fileName,
        Action<ProcessingProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be null or empty", nameof(fileName));

        var startTime = DateTime.UtcNow;

        try
        {
            var totalBytes = stream.CanSeek ? stream.Length : -1;

            // 읽기 시작 진행률 보고
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = fileName,
                Stage = ProcessingStage.Reading,
                OverallProgress = 0.0,
                StageProgress = 0.0,
                Message = $"스트림 읽기 시작: {fileName}",
                TotalBytes = totalBytes,
                ProcessedBytes = 0,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            // 스트림에서 텍스트 읽기
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);

            // 구조적 힌트 감지
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = fileName,
                Stage = ProcessingStage.Extracting,
                OverallProgress = 0.8,
                StageProgress = 0.8,
                Message = "구조적 패턴 분석 중",
                TotalBytes = totalBytes,
                ProcessedBytes = stream.Position,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            var structuralHints = DetectBasicStructuralHints(text, Path.GetExtension(fileName));

            var warnings = new List<string>();
            if (text.Length < 30)
            {
                warnings.Add($"Text content is very short ({text.Length} characters). May not be suitable for meaningful processing.");
            }

            // 완료 진행률 보고
            progressCallback?.Invoke(new ProcessingProgress
            {
                FilePath = fileName,
                Stage = ProcessingStage.Extracting,
                OverallProgress = 1.0,
                StageProgress = 1.0,
                Message = $"텍스트 추출 완료: {text.Length:N0}자",
                TotalBytes = totalBytes,
                ProcessedBytes = stream.Position,
                StartTime = startTime,
                CurrentTime = DateTime.UtcNow
            });

            return new RawDocumentContent
            {
                Text = text,
                FileInfo = new FileMetadata
                {
                    FileName = fileName,
                    FileExtension = Path.GetExtension(fileName).ToLowerInvariant(),
                    FileSize = stream.Length,
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = ReaderType
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = warnings
            };
        }
        catch (Exception ex) when (!(ex is FileFluxException))
        {
            progressCallback?.Invoke(ProcessingProgress.Factory.CreateError(fileName, ex.Message));
            throw new DocumentProcessingException(fileName, $"Failed to read text from stream: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 바이트 배열을 적절한 인코딩으로 디코딩합니다
    /// </summary>
    private static string DecodeTextWithEncoding(byte[] bytes)
    {
        // UTF-8 BOM 감지
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // UTF-8로 시도
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // UTF-8 실패 시 시스템 기본 인코딩 사용
            return Encoding.Default.GetString(bytes);
        }
    }
}
