using FileFlux.CLI.Output;
using FileFlux.Domain;
using Xunit;

namespace FileFlux.Tests.CLI;

public class OutputWriterTests : IDisposable
{
    private readonly string _testDir;

    public OutputWriterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"FileFlux_Tests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    // NOTE: ExtractOutputWriter tests removed - API changed to use ParsedContent
    // These tests need to be rewritten with ParsedContent instead of DocumentChunk list

    #region ChunkedOutputWriter Tests

    [Fact]
    public async Task ChunkedOutputWriter_CreatesDirectory()
    {
        // Arrange
        var writer = new ChunkedOutputWriter("md");
        var outputDir = Path.Combine(_testDir, "chunks");
        var chunks = CreateTestChunks(3);

        // Act
        await writer.WriteAsync(chunks, outputDir);

        // Assert
        Assert.True(Directory.Exists(outputDir));
    }

    [Fact]
    public async Task ChunkedOutputWriter_CreatesIndividualChunkFiles()
    {
        // Arrange
        var writer = new ChunkedOutputWriter("md");
        var outputDir = Path.Combine(_testDir, "chunks");
        var chunks = CreateTestChunks(3);

        // Act
        await writer.WriteAsync(chunks, outputDir);

        // Assert
        Assert.True(File.Exists(Path.Combine(outputDir, "chunk_1.md")));
        Assert.True(File.Exists(Path.Combine(outputDir, "chunk_2.md")));
        Assert.True(File.Exists(Path.Combine(outputDir, "chunk_3.md")));
    }

    [Fact]
    public async Task ChunkedOutputWriter_IncludesYamlFrontmatter()
    {
        // Arrange
        var writer = new ChunkedOutputWriter("md");
        var outputDir = Path.Combine(_testDir, "chunks");
        var chunks = CreateTestChunks(3);

        // Act
        await writer.WriteAsync(chunks, outputDir);

        // Assert
        var chunk1 = await File.ReadAllTextAsync(Path.Combine(outputDir, "chunk_1.md"));
        Assert.Contains("---", chunk1);
        Assert.Contains("chunk: 1", chunk1);
        Assert.Contains("total: 3", chunk1);
    }

    [Fact]
    public async Task ChunkedOutputWriter_IncludesNavigation()
    {
        // Arrange
        var writer = new ChunkedOutputWriter("md");
        var outputDir = Path.Combine(_testDir, "chunks");
        var chunks = CreateTestChunks(3);

        // Act
        await writer.WriteAsync(chunks, outputDir);

        // Assert
        var chunk1 = await File.ReadAllTextAsync(Path.Combine(outputDir, "chunk_1.md"));
        var chunk2 = await File.ReadAllTextAsync(Path.Combine(outputDir, "chunk_2.md"));
        var chunk3 = await File.ReadAllTextAsync(Path.Combine(outputDir, "chunk_3.md"));

        // First chunk has no prev
        Assert.DoesNotContain("prev:", chunk1);
        Assert.Contains("next: chunk_2.md", chunk1);

        // Middle chunk has both
        Assert.Contains("prev: chunk_1.md", chunk2);
        Assert.Contains("next: chunk_3.md", chunk2);

        // Last chunk has no next
        Assert.Contains("prev: chunk_2.md", chunk3);
        Assert.DoesNotContain("next:", chunk3);
    }

    [Fact]
    public async Task ChunkedOutputWriter_IncludesNavigationFooter()
    {
        // Arrange
        var writer = new ChunkedOutputWriter("md");
        var outputDir = Path.Combine(_testDir, "chunks");
        var chunks = CreateTestChunks(3);

        // Act
        await writer.WriteAsync(chunks, outputDir);

        // Assert
        var chunk2 = await File.ReadAllTextAsync(Path.Combine(outputDir, "chunk_2.md"));
        Assert.Contains("[← Previous](chunk_1.md)", chunk2);
        Assert.Contains("[Next →](chunk_3.md)", chunk2);
        Assert.Contains("[Info](info.json)", chunk2);
    }

    [Fact]
    public async Task ChunkedOutputWriter_JsonFormat_IncludesNavigation()
    {
        // Arrange
        var writer = new ChunkedOutputWriter("json");
        var outputDir = Path.Combine(_testDir, "chunks");
        var chunks = CreateTestChunks(3);

        // Act
        await writer.WriteAsync(chunks, outputDir);

        // Assert
        var chunk2 = await File.ReadAllTextAsync(Path.Combine(outputDir, "chunk_2.json"));
        Assert.Contains("\"navigation\"", chunk2);
        Assert.Contains("chunk_1.json", chunk2);
        Assert.Contains("chunk_3.json", chunk2);
    }

    #endregion

    #region ProcessingInfoWriter Tests

    [Fact]
    public async Task ProcessingInfoWriter_WriteChunkedInfoAsync_CreatesInfoJson()
    {
        // Arrange
        var outputDir = Path.Combine(_testDir, "chunks");
        Directory.CreateDirectory(outputDir);
        var chunks = CreateTestChunks(3);
        var info = new FileFlux.CLI.Output.ProcessingInfo
        {
            Command = "chunk",
            Format = "md",
            Strategy = "Auto",
            MaxChunkSize = 512,
            OverlapSize = 64,
            EnrichmentEnabled = false
        };

        // Create a test input file
        var testInputPath = Path.Combine(_testDir, "test.docx");
        await File.WriteAllTextAsync(testInputPath, "test content");

        // Act
        await ProcessingInfoWriter.WriteChunkedInfoAsync(outputDir, testInputPath, chunks, info);

        // Assert
        var infoPath = Path.Combine(outputDir, "info.json");
        Assert.True(File.Exists(infoPath));

        var json = await File.ReadAllTextAsync(infoPath);
        Assert.Contains("\"command\": \"chunk\"", json);
        Assert.Contains("\"totalChunks\": 3", json);
        Assert.Contains("chunk_1.md", json);
        Assert.Contains("chunk_2.md", json);
        Assert.Contains("chunk_3.md", json);
    }

    [Fact]
    public void ProcessingInfoWriter_GetInfoPath_ReturnsCorrectPathForDirectory()
    {
        // Arrange
        var dir = Path.Combine(_testDir, "testdir");
        Directory.CreateDirectory(dir);

        // Act
        var infoPath = ProcessingInfoWriter.GetInfoPath(dir);

        // Assert
        Assert.Equal(Path.Combine(dir, "info.json"), infoPath);
    }

    [Fact]
    public void ProcessingInfoWriter_GetInfoPath_ReturnsCorrectPathForFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "output.md");

        // Act
        var infoPath = ProcessingInfoWriter.GetInfoPath(filePath);

        // Assert
        Assert.Equal(Path.Combine(_testDir, "output.info.json"), infoPath);
    }

    #endregion

    #region Helper Methods

    private static List<DocumentChunk> CreateTestChunks(int count)
    {
        var chunks = new List<DocumentChunk>();
        for (int i = 0; i < count; i++)
        {
            chunks.Add(new DocumentChunk
            {
                Index = i,
                Content = $"Test content for chunk {i + 1}",
                Tokens = 100 + i * 10,
                Quality = 0.8 + i * 0.05,
                Metadata = new DocumentMetadata
                {
                    FileName = "test.docx",
                    FileType = "docx",
                    ProcessedAt = DateTime.UtcNow
                },
                Location = new SourceLocation
                {
                    StartPage = i + 1
                },
                SourceInfo = new SourceMetadataInfo
                {
                    SourceId = "test-source",
                    SourceType = "docx",
                    Title = "Test Document"
                }
            });
        }
        return chunks;
    }

    #endregion
}