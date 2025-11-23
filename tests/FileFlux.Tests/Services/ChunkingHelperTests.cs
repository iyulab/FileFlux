using FileFlux.Domain;
using FileFlux.Infrastructure.Strategies;
using Xunit;

namespace FileFlux.Tests.Services;

public class ChunkingHelperTests
{
    [Fact]
    public void GetHeadingPathForPosition_ReturnsCorrectPath()
    {
        // Arrange
        var sections = new List<ContentSection>
        {
            new()
            {
                Title = "Chapter 1",
                Level = 1,
                StartPosition = 0,
                EndPosition = 1000,
                Children = new List<ContentSection>
                {
                    new()
                    {
                        Title = "Section 1.1",
                        Level = 2,
                        StartPosition = 100,
                        EndPosition = 500
                    },
                    new()
                    {
                        Title = "Section 1.2",
                        Level = 2,
                        StartPosition = 500,
                        EndPosition = 1000
                    }
                }
            }
        };

        // Act
        var path = ChunkingHelper.GetHeadingPathForPosition(sections, 300);

        // Assert
        Assert.Equal(2, path.Count);
        Assert.Equal("Chapter 1", path[0]);
        Assert.Equal("Section 1.1", path[1]);
    }

    [Fact]
    public void GetHeadingPathForPosition_PositionOutsideSections_ReturnsEmptyPath()
    {
        // Arrange
        var sections = new List<ContentSection>
        {
            new()
            {
                Title = "Chapter 1",
                Level = 1,
                StartPosition = 100,
                EndPosition = 500
            }
        };

        // Act
        var path = ChunkingHelper.GetHeadingPathForPosition(sections, 600);

        // Assert
        Assert.Empty(path);
    }

    [Fact]
    public void GetPageForPosition_ReturnsCorrectPage()
    {
        // Arrange
        var pageRanges = new Dictionary<int, (int Start, int End)>
        {
            { 1, (0, 999) },
            { 2, (1000, 1999) },
            { 3, (2000, 2999) }
        };

        // Act & Assert
        Assert.Equal(1, ChunkingHelper.GetPageForPosition(pageRanges, 500));
        Assert.Equal(2, ChunkingHelper.GetPageForPosition(pageRanges, 1500));
        Assert.Equal(3, ChunkingHelper.GetPageForPosition(pageRanges, 2500));
    }

    [Fact]
    public void GetPageForPosition_PositionNotInRange_ReturnsNull()
    {
        // Arrange
        var pageRanges = new Dictionary<int, (int Start, int End)>
        {
            { 1, (0, 999) }
        };

        // Act
        var page = ChunkingHelper.GetPageForPosition(pageRanges, 5000);

        // Assert
        Assert.Null(page);
    }

    [Fact]
    public void GetPageForPosition_EmptyRanges_ReturnsNull()
    {
        // Arrange
        var pageRanges = new Dictionary<int, (int Start, int End)>();

        // Act
        var page = ChunkingHelper.GetPageForPosition(pageRanges, 100);

        // Assert
        Assert.Null(page);
    }

    [Fact]
    public void CreateSourceInfo_CreatesCorrectInfo()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = "Sample text for testing the source info creation.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.pdf",
                FileType = "pdf",
                Title = "Test Document",
                PageCount = 5,
                CreatedAt = new DateTime(2024, 1, 1)
            }
        };

        // Act
        var sourceInfo = ChunkingHelper.CreateSourceInfo(content);

        // Assert
        Assert.NotNull(sourceInfo);
        Assert.Equal("pdf", sourceInfo.SourceType);
        Assert.Equal("Test Document", sourceInfo.Title);
        Assert.Equal(5, sourceInfo.PageCount);
        Assert.True(sourceInfo.WordCount > 0);
    }

    [Fact]
    public void EnrichChunk_SetsAllMetadata()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = "This is sample content for testing chunk enrichment with various metadata.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.pdf",
                FileType = "pdf",
                PageCount = 1
            },
            Sections = new List<ContentSection>
            {
                new()
                {
                    Title = "Introduction",
                    Level = 1,
                    StartPosition = 0,
                    EndPosition = 100
                }
            },
            PageRanges = new Dictionary<int, (int Start, int End)>
            {
                { 1, (0, 100) }
            }
        };

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = "Sample content",
            Metadata = content.Metadata,
            Location = new SourceLocation { StartChar = 0, EndChar = 50 }
        };

        // Act
        ChunkingHelper.EnrichChunk(chunk, content, 0, 50);

        // Assert
        Assert.Single(chunk.Location.HeadingPath);
        Assert.Equal("Introduction", chunk.Location.HeadingPath[0]);
        Assert.Equal(1, chunk.Location.StartPage);
        Assert.Equal(1, chunk.Location.EndPage);
        Assert.True(chunk.ContextDependency >= 0 && chunk.ContextDependency <= 1);
        Assert.NotNull(chunk.SourceInfo);
    }

    [Fact]
    public void UpdateChunkCount_SetsCorrectCount()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new() { Id = Guid.NewGuid(), SourceInfo = new SourceMetadataInfo() },
            new() { Id = Guid.NewGuid(), SourceInfo = new SourceMetadataInfo() },
            new() { Id = Guid.NewGuid(), SourceInfo = new SourceMetadataInfo() }
        };

        // Act
        ChunkingHelper.UpdateChunkCount(chunks);

        // Assert
        foreach (var chunk in chunks)
        {
            Assert.Equal(3, chunk.SourceInfo.ChunkCount);
        }
    }
}
