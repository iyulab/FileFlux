using FileFlux.Core;
using System.Text.Json;
using System.Text;
using System.Globalization;

namespace FileFlux.Infrastructure.Storage;

/// <summary>
/// 테스트 결과를 파일로 저장하는 헬퍼 클래스
/// </summary>
public class TestResultsStorage
{
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    public TestResultsStorage(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        var directories = new[]
        {
            Path.Combine(_baseDirectory, "logs"),
            Path.Combine(_baseDirectory, "extraction-results"),
            Path.Combine(_baseDirectory, "parsing-results"),
            Path.Combine(_baseDirectory, "chunking-results")
        };

        foreach (var directory in directories)
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 추출 결과 저장
    /// </summary>
    public async Task SaveExtractionResultAsync(string fileName, RawContent rawContent)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // JSON 메타데이터 저장
        var metadata = new
        {
            FileName = rawContent.File.Name,
            FileSize = rawContent.File.Size,
            FileExtension = rawContent.File.Extension,
            ExtractedAt = rawContent.File.ModifiedAt,
            ReaderType = rawContent.ReaderType,
            TextLength = rawContent.Text.Length,
            WarningCount = rawContent.Warnings.Count,
            Warnings = rawContent.Warnings,
            Hints = rawContent.Hints
        };

        var metadataPath = Path.Combine(_baseDirectory, "extraction-results", $"{sanitizedName}_{timestamp}_metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, _jsonOptions), Encoding.UTF8);

        // 추출된 텍스트 저장
        var textPath = Path.Combine(_baseDirectory, "extraction-results", $"{sanitizedName}_{timestamp}_content.txt");
        await File.WriteAllTextAsync(textPath, rawContent.Text, Encoding.UTF8);
    }

    /// <summary>
    /// 파싱 결과 저장
    /// </summary>
    public async Task SaveParsingResultAsync(string fileName, RefinedContent parsedContent)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // JSON 메타데이터 저장
        var metadata = new
        {
            OriginalFileName = parsedContent.Metadata?.FileName,
            DocumentType = parsedContent.Metadata?.FileType,
            Topic = parsedContent.Topic,
            Keywords = parsedContent.Keywords,
            Summary = parsedContent.Summary,
            SectionCount = parsedContent.Sections.Count,
            QualityMetrics = new
            {
                parsedContent.Quality.ConfidenceScore,
                parsedContent.Quality.CleanupScore,
                parsedContent.Quality.RetentionScore,
                parsedContent.Quality.OverallScore,
                parsedContent.Quality.StructureScore
            },
            RefinementInfo = new
            {
                parsedContent.Info.UsedLlm,
                parsedContent.Info.RefinerType,
                Duration = parsedContent.Info.Duration.TotalMilliseconds,
                WarningCount = parsedContent.Info.Warnings.Count,
                Warnings = parsedContent.Info.Warnings
            }
        };

        var metadataPath = Path.Combine(_baseDirectory, "parsing-results", $"{sanitizedName}_{timestamp}_metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, _jsonOptions), Encoding.UTF8);

        // 구조화된 텍스트 저장
        var structuredTextPath = Path.Combine(_baseDirectory, "parsing-results", $"{sanitizedName}_{timestamp}_structured.txt");
        await File.WriteAllTextAsync(structuredTextPath, parsedContent.Text ?? parsedContent.Text, Encoding.UTF8);

        // 원본 텍스트 저장 (다른 경우)
        if (!string.IsNullOrEmpty(parsedContent.Text) && parsedContent.Text != parsedContent.Text)
        {
            var originalTextPath = Path.Combine(_baseDirectory, "parsing-results", $"{sanitizedName}_{timestamp}_original.txt");
            await File.WriteAllTextAsync(originalTextPath, parsedContent.Text, Encoding.UTF8);
        }

        // 섹션 구조 저장
        if (parsedContent.Sections.Count != 0)
        {
            var sectionsPath = Path.Combine(_baseDirectory, "parsing-results", $"{sanitizedName}_{timestamp}_sections.json");
            await File.WriteAllTextAsync(sectionsPath, JsonSerializer.Serialize(parsedContent.Sections, _jsonOptions), Encoding.UTF8);
        }
    }

    /// <summary>
    /// 청킹 결과 저장
    /// </summary>
    public async Task SaveChunkingResultsAsync(string fileName, DocumentChunk[] chunks, ChunkingOptions options)
    {
        var sanitizedName = SanitizeFileName(fileName);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        // 청킹 통계
        var statistics = new
        {
            FileName = fileName,
            ProcessedAt = DateTime.Now,
            ChunkingStrategy = options.Strategy,
            ConfiguredMaxChunkSize = options.MaxChunkSize,
            OverlapSize = options.OverlapSize,
            TotalChunks = chunks.Length,
            AverageChunkSize = chunks.Length > 0 ? chunks.Average(c => c.Content.Length) : 0,
            MinChunkSize = chunks.Length > 0 ? chunks.Min(c => c.Content.Length) : 0,
            MaxChunkSize = chunks.Length > 0 ? chunks.Max(c => c.Content.Length) : 0,
            TotalCharacters = chunks.Sum(c => c.Content.Length),
            ChunkSizeDistribution = GetChunkSizeDistribution(chunks)
        };

        var statsPath = Path.Combine(_baseDirectory, "chunking-results", $"{sanitizedName}_{timestamp}_statistics.json");
        await File.WriteAllTextAsync(statsPath, JsonSerializer.Serialize(statistics, _jsonOptions), Encoding.UTF8);

        // 모든 청크를 하나의 텍스트 파일로 저장
        var allChunksPath = Path.Combine(_baseDirectory, "chunking-results", $"{sanitizedName}_{timestamp}_all_chunks.txt");
        var allChunksContent = new StringBuilder();

        allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"FileFlux 청킹 결과: {fileName}");
        allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"총 {chunks.Length}개 청크 생성");
        allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"처리 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        allChunksContent.AppendLine("=".PadRight(80, '='));
        allChunksContent.AppendLine();

        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"=== 청크 {i + 1}/{chunks.Length} ===");
            allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"ID: {chunk.Id}");
            allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"크기: {chunk.Content.Length}자");
            allChunksContent.AppendLine(CultureInfo.InvariantCulture, $"위치: {chunk.Location.StartChar} - {chunk.Location.EndChar}");
            allChunksContent.AppendLine();
            allChunksContent.AppendLine(chunk.Content);
            allChunksContent.AppendLine();
            allChunksContent.AppendLine("-".PadRight(60, '-'));
            allChunksContent.AppendLine();
        }

        await File.WriteAllTextAsync(allChunksPath, allChunksContent.ToString(), Encoding.UTF8);

        // 개별 청크 파일들 저장
        var individualChunksDir = Path.Combine(_baseDirectory, "chunking-results", $"{sanitizedName}_{timestamp}_individual");
        Directory.CreateDirectory(individualChunksDir);

        var tasks = chunks.Select(async (chunk, index) =>
        {
            var chunkPath = Path.Combine(individualChunksDir, $"chunk_{index + 1:D3}.txt");
            var chunkHeader = $"청크 {index + 1}/{chunks.Length}\n" +
                             $"ID: {chunk.Id}\n" +
                             $"크기: {chunk.Content.Length}자\n" +
                             $"위치: {chunk.Location.StartChar} - {chunk.Location.EndChar}\n" +
                             $"인덱스: {chunk.Index}\n" +
                             new string('=', 50) + "\n\n";

            await File.WriteAllTextAsync(chunkPath, chunkHeader + chunk.Content, Encoding.UTF8);
        });

        await Task.WhenAll(tasks);
    }

    private static Dictionary<string, int> GetChunkSizeDistribution(DocumentChunk[] chunks)
    {
        var distribution = new Dictionary<string, int>();

        foreach (var chunk in chunks)
        {
            var size = chunk.Content.Length;
            var range = size switch
            {
                < 100 => "0-99",
                < 300 => "100-299",
                < 500 => "300-499",
                < 1000 => "500-999",
                < 2000 => "1000-1999",
                _ => "2000+"
            };

            distribution[range] = distribution.GetValueOrDefault(range, 0) + 1;
        }

        return distribution;
    }

    private static string SanitizeFileName(string fileName)
    {
        return FileNameHelper.NormalizeFileName(fileName)
            .Replace(" ", "_")
            .Replace(".", "_");
    }
}
