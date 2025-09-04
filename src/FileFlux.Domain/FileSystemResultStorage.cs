using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FileFlux.Domain;

/// <summary>
/// 파일 시스템 기반 처리 결과 저장소
/// 파일 해시를 기반으로 디렉토리 구조를 생성하여 결과를 저장합니다
/// </summary>
public class FileSystemResultStorage : IDisposable
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// FileSystemResultStorage 인스턴스를 초기화합니다
    /// </summary>
    /// <param name="baseDirectory">기본 저장 디렉토리 (기본값: ./fileflux-results)</param>
    public FileSystemResultStorage(string baseDirectory = "./fileflux-results")
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // 기본 디렉토리 생성
        Directory.CreateDirectory(_baseDirectory);
    }

    /// <summary>
    /// 파일의 SHA256 해시를 계산합니다
    /// </summary>
    /// <param name="filePath">파일 경로</param>
    /// <returns>16진수 문자열로 된 해시</returns>
    public async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);

        var hashBytes = await sha256.ComputeHashAsync(fileStream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 문자열 내용의 SHA256 해시를 계산합니다
    /// </summary>
    /// <param name="content">내용</param>
    /// <returns>16진수 문자열로 된 해시</returns>
    public static string ComputeContentHash(string content)
    {
        using var sha256 = SHA256.Create();
        var contentBytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha256.ComputeHash(contentBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 파일 해시를 기반으로 결과 디렉토리 경로를 생성합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <returns>결과 디렉토리 경로</returns>
    public string GetResultDirectory(string fileHash)
    {
        // 해시를 2자리씩 나누어 중첩 디렉토리 생성 (성능 최적화)
        var subdir1 = fileHash.Substring(0, 2);
        var subdir2 = fileHash.Substring(2, 2);

        return Path.Combine(_baseDirectory, subdir1, subdir2, fileHash);
    }

    /// <summary>
    /// RawDocumentContent를 저장합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <param name="content">원본 문서 내용</param>
    /// <returns>저장된 파일 경로</returns>
    public async Task<string> SaveRawContentAsync(string fileHash, RawDocumentContent content)
    {
        var resultDir = GetResultDirectory(fileHash);
        Directory.CreateDirectory(resultDir);

        var extractionDir = Path.Combine(resultDir, "extraction");
        Directory.CreateDirectory(extractionDir);

        // 메타데이터와 함께 JSON으로 저장
        var extractionResult = new
        {
            FileHash = fileHash,
            ExtractedAt = DateTime.UtcNow,
            FileInfo = content.FileInfo,
            Text = content.Text,
            StructuralHints = content.StructuralHints,
            ExtractionWarnings = content.ExtractionWarnings,
            TextLength = content.Text.Length,
            WordCount = content.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
        };

        var jsonPath = Path.Combine(extractionDir, "raw-content.json");
        var jsonContent = JsonSerializer.Serialize(extractionResult, _jsonOptions);
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        // 순수 텍스트도 별도 저장
        var textPath = Path.Combine(extractionDir, "extracted-text.txt");
        await File.WriteAllTextAsync(textPath, content.Text);

        return jsonPath;
    }

    /// <summary>
    /// ParsedDocumentContent를 저장합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <param name="content">파싱된 문서 내용</param>
    /// <returns>저장된 파일 경로</returns>
    public async Task<string> SaveParsedContentAsync(string fileHash, ParsedDocumentContent content)
    {
        var resultDir = GetResultDirectory(fileHash);
        Directory.CreateDirectory(resultDir);

        var parsingDir = Path.Combine(resultDir, "parsing");
        Directory.CreateDirectory(parsingDir);

        // 파싱 결과를 JSON으로 저장
        var parsingResult = new
        {
            FileHash = fileHash,
            ParsedAt = DateTime.UtcNow,
            StructuredText = content.StructuredText,
            OriginalText = content.OriginalText,
            Metadata = content.Metadata,
            Structure = content.Structure,
            Quality = content.Quality,
            ParsingInfo = content.ParsingInfo
        };

        var jsonPath = Path.Combine(parsingDir, "parsed-content.json");
        var jsonContent = JsonSerializer.Serialize(parsingResult, _jsonOptions);
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        // 구조화된 텍스트도 별도 저장
        var structuredTextPath = Path.Combine(parsingDir, "structured-text.txt");
        await File.WriteAllTextAsync(structuredTextPath, content.StructuredText);

        return jsonPath;
    }

    /// <summary>
    /// DocumentChunk 배열을 저장합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <param name="chunks">문서 청크 배열</param>
    /// <param name="chunkingOptions">청킹 옵션</param>
    /// <returns>저장된 파일 경로</returns>
    public async Task<string> SaveChunksAsync(string fileHash, DocumentChunk[] chunks, ChunkingOptions chunkingOptions)
    {
        var resultDir = GetResultDirectory(fileHash);
        Directory.CreateDirectory(resultDir);

        var chunkingDir = Path.Combine(resultDir, "chunking");
        Directory.CreateDirectory(chunkingDir);

        // 청킹 결과를 JSON으로 저장
        var chunkingResult = new
        {
            FileHash = fileHash,
            ChunkedAt = DateTime.UtcNow,
            ChunkingOptions = chunkingOptions,
            ChunkCount = chunks.Length,
            TotalCharacters = chunks.Sum(c => c.Content.Length),
            AverageChunkSize = chunks.Length > 0 ? chunks.Average(c => c.Content.Length) : 0,
            Chunks = chunks.Select(c => new
            {
                c.Id,
                c.Content,
                c.StartPosition,
                c.EndPosition,
                c.ChunkIndex,
                c.Metadata,
                c.Properties,
                ContentLength = c.Content.Length,
                WordCount = c.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
            })
        };

        var jsonPath = Path.Combine(chunkingDir, "chunks.json");
        var jsonContent = JsonSerializer.Serialize(chunkingResult, _jsonOptions);
        await File.WriteAllTextAsync(jsonPath, jsonContent);

        // 개별 청크 파일도 생성
        var chunksDir = Path.Combine(chunkingDir, "individual-chunks");
        Directory.CreateDirectory(chunksDir);

        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            var chunkFileName = $"chunk-{i:D4}-{chunk.Id}.txt";
            var chunkFilePath = Path.Combine(chunksDir, chunkFileName);

            var chunkContent = $"=== Chunk {i + 1}/{chunks.Length} ===\n" +
                              $"ID: {chunk.Id}\n" +
                              $"Position: {chunk.StartPosition}-{chunk.EndPosition}\n" +
                              $"Length: {chunk.Content.Length} chars\n" +
                              $"Metadata: {JsonSerializer.Serialize(chunk.Metadata, _jsonOptions)}\n" +
                              $"{new string('=', 50)}\n\n" +
                              chunk.Content;

            await File.WriteAllTextAsync(chunkFilePath, chunkContent);
        }

        return jsonPath;
    }

    /// <summary>
    /// 처리 진행률을 저장합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <param name="progress">진행률 정보</param>
    /// <returns>저장된 파일 경로</returns>
    public async Task<string> SaveProgressAsync(string fileHash, ProcessingProgress progress)
    {
        var resultDir = GetResultDirectory(fileHash);
        Directory.CreateDirectory(resultDir);

        var progressPath = Path.Combine(resultDir, "progress.json");
        var jsonContent = JsonSerializer.Serialize(progress, _jsonOptions);
        await File.WriteAllTextAsync(progressPath, jsonContent);

        return progressPath;
    }

    /// <summary>
    /// 저장된 결과가 존재하는지 확인합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <param name="resultType">결과 타입 ("extraction", "parsing", "chunking")</param>
    /// <returns>존재 여부</returns>
    public bool HasResult(string fileHash, string resultType)
    {
        var resultDir = GetResultDirectory(fileHash);
        var typeDir = Path.Combine(resultDir, resultType);

        return Directory.Exists(typeDir) && Directory.GetFiles(typeDir, "*.json").Length > 0;
    }

    /// <summary>
    /// 저장된 RawDocumentContent를 로드합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <returns>RawDocumentContent 또는 null</returns>
    public async Task<RawDocumentContent?> LoadRawContentAsync(string fileHash)
    {
        var resultDir = GetResultDirectory(fileHash);
        var jsonPath = Path.Combine(resultDir, "extraction", "raw-content.json");

        if (!File.Exists(jsonPath))
            return null;

        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        var result = JsonSerializer.Deserialize<RawDocumentContent>(jsonContent, _jsonOptions);
        return result;
    }

    /// <summary>
    /// 저장된 DocumentChunk 배열을 로드합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <returns>DocumentChunk 배열 또는 null</returns>
    public async Task<DocumentChunk[]?> LoadChunksAsync(string fileHash)
    {
        var resultDir = GetResultDirectory(fileHash);
        var jsonPath = Path.Combine(resultDir, "chunking", "chunks.json");

        if (!File.Exists(jsonPath))
            return null;

        var jsonContent = await File.ReadAllTextAsync(jsonPath);
        using var document = JsonDocument.Parse(jsonContent);

        if (document.RootElement.TryGetProperty("chunks", out var chunksElement))
        {
            return JsonSerializer.Deserialize<DocumentChunk[]>(chunksElement.GetRawText(), _jsonOptions);
        }

        return null;
    }

    /// <summary>
    /// 처리 요약 정보를 생성합니다
    /// </summary>
    /// <param name="fileHash">파일 해시</param>
    /// <returns>처리 요약 정보</returns>
    public async Task<ProcessingSummary?> GetProcessingSummaryAsync(string fileHash)
    {
        var resultDir = GetResultDirectory(fileHash);

        if (!Directory.Exists(resultDir))
            return null;

        var summary = new ProcessingSummary
        {
            FileHash = fileHash,
            ResultDirectory = resultDir,
            HasExtraction = HasResult(fileHash, "extraction"),
            HasParsing = HasResult(fileHash, "parsing"),
            HasChunking = HasResult(fileHash, "chunking")
        };

        // 청킹 결과가 있으면 추가 정보 수집
        if (summary.HasChunking)
        {
            var chunksJsonPath = Path.Combine(resultDir, "chunking", "chunks.json");
            if (File.Exists(chunksJsonPath))
            {
                var jsonContent = await File.ReadAllTextAsync(chunksJsonPath);
                using var document = JsonDocument.Parse(jsonContent);

                if (document.RootElement.TryGetProperty("chunkCount", out var chunkCountElement))
                {
                    summary.ChunkCount = chunkCountElement.GetInt32();
                }

                if (document.RootElement.TryGetProperty("totalCharacters", out var totalCharsElement))
                {
                    summary.TotalCharacters = totalCharsElement.GetInt64();
                }

                if (document.RootElement.TryGetProperty("chunkedAt", out var chunkedAtElement))
                {
                    if (DateTime.TryParse(chunkedAtElement.GetString(), out var chunkedAt))
                    {
                        summary.LastProcessed = chunkedAt;
                    }
                }
            }
        }

        return summary;
    }

    /// <summary>
    /// 리소스를 해제합니다
    /// </summary>
    public void Dispose()
    {
        // 현재는 특별히 해제할 리소스가 없음
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 처리 결과 요약 정보
/// </summary>
public class ProcessingSummary
{
    /// <summary>
    /// 파일 해시
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// 결과 디렉토리 경로
    /// </summary>
    public string ResultDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 텍스트 추출 결과 존재 여부
    /// </summary>
    public bool HasExtraction { get; set; }

    /// <summary>
    /// 파싱 결과 존재 여부
    /// </summary>
    public bool HasParsing { get; set; }

    /// <summary>
    /// 청킹 결과 존재 여부
    /// </summary>
    public bool HasChunking { get; set; }

    /// <summary>
    /// 청크 수
    /// </summary>
    public int ChunkCount { get; set; }

    /// <summary>
    /// 총 문자 수
    /// </summary>
    public long TotalCharacters { get; set; }

    /// <summary>
    /// 마지막 처리 시간
    /// </summary>
    public DateTime? LastProcessed { get; set; }

    /// <summary>
    /// 모든 단계가 완료되었는지 여부
    /// </summary>
    public bool IsComplete => HasExtraction && HasParsing && HasChunking;
}