using FileFlux.Infrastructure.Readers;
using FileFlux.Infrastructure.Factories;
using FileFlux.Domain;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// ZipArchiveReader 단위 테스트
/// </summary>
public class ZipArchiveReaderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly DocumentReaderFactory _readerFactory;
    private readonly ZipArchiveReader _reader;

    public ZipArchiveReaderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"FileFlux_ZipTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        _readerFactory = new DocumentReaderFactory();
        _reader = new ZipArchiveReader(_readerFactory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // Cleanup failure is acceptable in tests
            }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ReaderType_ShouldReturnZipArchiveReader()
    {
        // Act
        var readerType = _reader.ReaderType;

        // Assert
        Assert.Equal("ZipArchiveReader", readerType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludeZip()
    {
        // Act
        var supportedExtensions = _reader.SupportedExtensions;

        // Assert
        Assert.Contains(".zip", supportedExtensions);
    }

    [Theory]
    [InlineData("archive.zip", true)]
    [InlineData("TEST.ZIP", true)]
    [InlineData("document.pdf", false)]
    [InlineData("test.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanRead_ShouldReturnCorrectResult(string? fileName, bool expected)
    {
        // Act
        var canRead = _reader.CanRead(fileName!);

        // Assert
        Assert.Equal(expected, canRead);
    }

    [Fact]
    public async Task ExtractAsync_WithNullFilePath_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _reader.ExtractAsync(null!, CancellationToken.None));

        Assert.Contains("File path cannot be null or empty", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.zip");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _reader.ExtractAsync(nonExistentPath, CancellationToken.None));
    }

    [Fact]
    public async Task ExtractAsync_WithValidZipContainingTextFiles_ShouldExtractSuccessfully()
    {
        // Arrange
        var zipPath = CreateTestZipWithTextFiles();

        // Act
        var result = await _reader.ExtractAsync(zipPath, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ProcessingStatus.Completed, result.Status);
        Assert.NotEmpty(result.Text);
        Assert.Contains("file1.txt", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file2.txt", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(".zip", result.File.Extension);
        Assert.Equal("ZipArchiveReader", result.ReaderType);

        // Verify metadata
        Assert.True(result.Hints.ContainsKey("SupportedFiles"));
        Assert.True(result.Hints.ContainsKey("ProcessedFileList"));
    }

    [Fact]
    public async Task ExtractAsync_WithMixedFileTypes_ShouldProcessOnlySupportedFormats()
    {
        // Arrange
        var zipPath = CreateTestZipWithMixedFiles();

        // Act
        var result = await _reader.ExtractAsync(zipPath, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ProcessingStatus.Completed, result.Status);

        // Should contain text from .txt file
        Assert.Contains("Sample text content", result.Text, StringComparison.OrdinalIgnoreCase);

        // Should skip unsupported .bin file
        Assert.True(result.Hints.ContainsKey("SupportedFiles"));
        var supportedCount = (int)result.Hints["SupportedFiles"];
        Assert.Equal(2, supportedCount); // Only .txt and .md files
    }

    [Fact]
    public async Task ExtractAsync_WithPathTraversalAttempt_ShouldSkipMaliciousEntries()
    {
        // Arrange
        var zipPath = CreateTestZipWithPathTraversal();

        // Act
        var result = await _reader.ExtractAsync(zipPath, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("unsafe entry", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task ExtractAsync_WithEmptyZip_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var zipPath = CreateEmptyZip();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _reader.ExtractAsync(zipPath, CancellationToken.None));

        Assert.Contains("No supported file formats found", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_WithCancellationToken_ShouldCancelOperation()
    {
        // Arrange
        var zipPath = CreateLargeTestZip();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(10)); // Cancel quickly

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _reader.ExtractAsync(zipPath, cts.Token));
    }

    [Fact]
    public async Task ExtractAsync_WithZipBomb_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var zipPath = CreateZipBombSimulation();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _reader.ExtractAsync(zipPath, CancellationToken.None));

        Assert.Contains("compression ratio", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExtractAsync_WithCustomOptions_ShouldRespectLimits()
    {
        // Arrange
        var options = new ZipProcessingOptions
        {
            MaxZipFileSize = 50 * 1024, // 50KB limit
            MaxFileCount = 5 // Allow enough files for test
        };

        var readerWithOptions = new ZipArchiveReader(_readerFactory, options);
        var zipPath = CreateTestZipWithTextFiles();

        // Act
        var result = await readerWithOptions.ExtractAsync(zipPath, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ProcessingStatus.Completed, result.Status);
    }

    #region Helper Methods

    private string CreateTestZipWithTextFiles()
    {
        var zipPath = Path.Combine(_testDirectory, "test_text_files.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Add file1.txt
            var entry1 = archive.CreateEntry("file1.txt");
            using (var writer = new StreamWriter(entry1.Open()))
            {
                writer.WriteLine("This is the content of file1.txt");
                writer.WriteLine("It contains multiple lines.");
            }

            // Add file2.txt
            var entry2 = archive.CreateEntry("file2.txt");
            using (var writer = new StreamWriter(entry2.Open()))
            {
                writer.WriteLine("This is file2.txt content");
            }

            // Add subdirectory with file
            var entry3 = archive.CreateEntry("subdir/file3.txt");
            using (var writer = new StreamWriter(entry3.Open()))
            {
                writer.WriteLine("Nested file content");
            }
        }

        return zipPath;
    }

    private string CreateTestZipWithMixedFiles()
    {
        var zipPath = Path.Combine(_testDirectory, "test_mixed_files.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Supported: .txt
            var entry1 = archive.CreateEntry("document.txt");
            using (var writer = new StreamWriter(entry1.Open()))
            {
                writer.WriteLine("Sample text content");
            }

            // Supported: .md
            var entry2 = archive.CreateEntry("readme.md");
            using (var writer = new StreamWriter(entry2.Open()))
            {
                writer.WriteLine("# Markdown Content");
            }

            // Unsupported: .bin
            var entry3 = archive.CreateEntry("data.bin");
            using (var writer = new BinaryWriter(entry3.Open()))
            {
                writer.Write(new byte[] { 0x00, 0x01, 0x02 });
            }
        }

        return zipPath;
    }

    private string CreateTestZipWithPathTraversal()
    {
        var zipPath = Path.Combine(_testDirectory, "test_path_traversal.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Malicious entry with path traversal
            var entry1 = archive.CreateEntry("../../etc/passwd.txt");
            using (var writer = new StreamWriter(entry1.Open()))
            {
                writer.WriteLine("Malicious content");
            }

            // Normal entry
            var entry2 = archive.CreateEntry("normal.txt");
            using (var writer = new StreamWriter(entry2.Open()))
            {
                writer.WriteLine("Normal content");
            }
        }

        return zipPath;
    }

    private string CreateEmptyZip()
    {
        var zipPath = Path.Combine(_testDirectory, "empty.zip");
        using (ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Create empty archive
        }
        return zipPath;
    }

    private string CreateLargeTestZip()
    {
        var zipPath = Path.Combine(_testDirectory, "large_test.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < 100; i++)
            {
                var entry = archive.CreateEntry($"file{i}.txt");
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.WriteLine($"Content of file {i}");
                    writer.WriteLine(new string('x', 10000)); // Large content
                }
            }
        }

        return zipPath;
    }

    private string CreateZipBombSimulation()
    {
        var zipPath = Path.Combine(_testDirectory, "zip_bomb.zip");

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Create a highly compressible file (all zeros)
            var entry = archive.CreateEntry("bomb.txt");
            using (var writer = new StreamWriter(entry.Open()))
            {
                // Write 10MB of zeros (will compress to very small size)
                for (int i = 0; i < 10000; i++)
                {
                    writer.WriteLine(new string('0', 1000));
                }
            }
        }

        return zipPath;
    }

    #endregion
}
