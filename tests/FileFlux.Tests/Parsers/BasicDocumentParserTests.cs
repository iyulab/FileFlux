using FileFlux.Core;
using FileFlux.Infrastructure.Parsers;

namespace FileFlux.Tests.Parsers;

/// <summary>
/// Tests for BasicDocumentParser, including section extraction and hierarchy building.
/// </summary>
public class BasicDocumentParserTests
{
    private readonly BasicDocumentParser _parser;

    public BasicDocumentParserTests()
    {
        _parser = new BasicDocumentParser();
    }

    [Fact]
    public async Task ParseAsync_MarkdownWithHeaders_ExtractsSectionsWithStartAndEnd()
    {
        // Arrange
        var markdown = """
            # Introduction
            This is the introduction.

            ## Background
            Some background info.

            ## Methods
            The methods section.
            """;

        var rawContent = new RawContent
        {
            Text = markdown,
            File = new SourceFileInfo { Name = "test.md", Extension = ".md", Size = markdown.Length },
            Hints = new Dictionary<string, object> { { "has_headers", true } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        Assert.NotEmpty(result.Sections);

        foreach (var section in result.Sections)
        {
            Assert.True(section.Start >= 0, $"Section '{section.Title}' should have Start >= 0");
            Assert.True(section.End > section.Start, $"Section '{section.Title}' should have End > Start");
            Assert.True(section.End < markdown.Length, $"Section '{section.Title}' End should be within text bounds");
        }
    }

    [Fact]
    public async Task ParseAsync_MarkdownWithNestedHeaders_BuildsHierarchy()
    {
        // Arrange
        var markdown = """
            # Chapter 1
            Intro to chapter 1.

            ## Section 1.1
            Content of section 1.1.

            ### Subsection 1.1.1
            Deep content here.

            ## Section 1.2
            Content of section 1.2.

            # Chapter 2
            Intro to chapter 2.
            """;

        var rawContent = new RawContent
        {
            Text = markdown,
            File = new SourceFileInfo { Name = "test.md", Extension = ".md", Size = markdown.Length },
            Hints = new Dictionary<string, object> { { "has_headers", true } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        // Should have 2 top-level sections (Chapter 1 and Chapter 2)
        Assert.Equal(2, result.Sections.Count);

        var chapter1 = result.Sections[0];
        Assert.Equal("Chapter 1", chapter1.Title);
        Assert.Equal(1, chapter1.Level);
        Assert.Equal(2, chapter1.Children.Count); // Section 1.1 and Section 1.2

        var section11 = chapter1.Children[0];
        Assert.Equal("Section 1.1", section11.Title);
        Assert.Equal(2, section11.Level);
        Assert.Single(section11.Children); // Subsection 1.1.1

        var subsection111 = section11.Children[0];
        Assert.Equal("Subsection 1.1.1", subsection111.Title);
        Assert.Equal(3, subsection111.Level);
        Assert.Empty(subsection111.Children);

        var section12 = chapter1.Children[1];
        Assert.Equal("Section 1.2", section12.Title);
        Assert.Empty(section12.Children);

        var chapter2 = result.Sections[1];
        Assert.Equal("Chapter 2", chapter2.Title);
        Assert.Equal(1, chapter2.Level);
    }

    [Fact]
    public async Task ParseAsync_MarkdownWithSkippedLevels_HandlesGracefully()
    {
        // Arrange - jumping from h1 to h3 (skipping h2)
        var markdown = """
            # Main Title
            Introduction.

            ### Deep Section
            This skips h2 but should still work.
            """;

        var rawContent = new RawContent
        {
            Text = markdown,
            File = new SourceFileInfo { Name = "test.md", Extension = ".md", Size = markdown.Length },
            Hints = new Dictionary<string, object> { { "has_headers", true } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        Assert.Single(result.Sections); // Only Main Title at root

        var mainTitle = result.Sections[0];
        Assert.Equal("Main Title", mainTitle.Title);
        Assert.Single(mainTitle.Children); // Deep Section as child

        var deepSection = mainTitle.Children[0];
        Assert.Equal("Deep Section", deepSection.Title);
        Assert.Equal(3, deepSection.Level);
    }

    [Fact]
    public async Task ParseAsync_ParagraphsWithoutHeaders_ExtractsFlatSections()
    {
        // Arrange
        var text = """
            First paragraph with some content.

            Second paragraph with more content.

            Third paragraph to conclude.
            """;

        var rawContent = new RawContent
        {
            Text = text,
            File = new SourceFileInfo { Name = "test.txt", Extension = ".txt", Size = text.Length },
            Hints = new Dictionary<string, object> { { "has_headers", false } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        Assert.Equal(3, result.Sections.Count);

        foreach (var section in result.Sections)
        {
            Assert.Equal(1, section.Level);
            Assert.Empty(section.Children); // Paragraphs don't have children
            Assert.True(section.Start >= 0);
            Assert.True(section.End > section.Start);
        }
    }

    [Fact]
    public async Task ParseAsync_SectionOffsets_AreAccurate()
    {
        // Arrange
        var markdown = "# Title\nContent here.";

        var rawContent = new RawContent
        {
            Text = markdown,
            File = new SourceFileInfo { Name = "test.md", Extension = ".md", Size = markdown.Length },
            Hints = new Dictionary<string, object> { { "has_headers", true } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        Assert.Single(result.Sections);

        var section = result.Sections[0];
        Assert.Equal(0, section.Start); // Starts at beginning
        Assert.Equal(markdown.Length - 1, section.End); // Ends at last character

        // Verify the content can be extracted using offsets
        var extractedContent = markdown.Substring(section.Start, section.End - section.Start + 1);
        Assert.Equal(markdown, extractedContent);
    }

    [Fact]
    public async Task ParseAsync_EmptyDocument_ReturnsEmptySections()
    {
        // Arrange
        var rawContent = new RawContent
        {
            Text = "",
            File = new SourceFileInfo { Name = "empty.md", Extension = ".md", Size = 0 },
            Hints = new Dictionary<string, object> { { "has_headers", true } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        Assert.Empty(result.Sections);
    }

    [Fact]
    public async Task ParseAsync_MultipleH1Headers_CreatesMultipleRoots()
    {
        // Arrange
        var markdown = """
            # Document 1
            Content for doc 1.

            # Document 2
            Content for doc 2.

            # Document 3
            Content for doc 3.
            """;

        var rawContent = new RawContent
        {
            Text = markdown,
            File = new SourceFileInfo { Name = "multi.md", Extension = ".md", Size = markdown.Length },
            Hints = new Dictionary<string, object> { { "has_headers", true } }
        };

        var options = new DocumentParsingOptions { UseLlmParsing = false };

        // Act
        var result = await _parser.ParseAsync(rawContent, options);

        // Assert
        Assert.Equal(3, result.Sections.Count);
        Assert.All(result.Sections, s => Assert.Equal(1, s.Level));
    }
}
