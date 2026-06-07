using FileFlux.Core.Infrastructure.Readers;
using Xunit;
using System.Text;

namespace FileFlux.Tests.Readers;

/// <summary>
/// MarkdownDocumentReader unit tests
/// </summary>
public class MarkdownDocumentReaderTests
{
    private readonly MarkdownDocumentReader _reader;

    public MarkdownDocumentReaderTests()
    {
        _reader = new MarkdownDocumentReader();
    }

    [Fact]
    public void ReaderType_ShouldReturnMarkdownReader()
    {
        // Act
        var readerType = _reader.ReaderType;

        // Assert
        Assert.Equal("MarkdownReader", readerType);
    }

    [Fact]
    public void SupportedExtensions_ShouldIncludeMd()
    {
        // Act
        var supportedExtensions = _reader.SupportedExtensions;

        // Assert
        Assert.Contains(".md", supportedExtensions);
    }

    [Theory]
    [InlineData("test.md", true)]
    [InlineData("README.MD", true)]
    [InlineData("document.txt", false)]
    [InlineData("file.docx", false)]
    public void CanRead_ShouldReturnCorrectResult(string filePath, bool expected)
    {
        // Act
        var canRead = _reader.CanRead(filePath);

        // Assert
        Assert.Equal(expected, canRead);
    }

    [Fact]
    public async Task ExtractAsync_OrderedList_ShouldHaveSequentialNumbering()
    {
        // Arrange
        var markdown = @"## Summary

This document covers:
1. First item
2. Second item
3. Third item
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "test.md");

        // Assert
        var content = result.Text;

        // Verify sequential numbering (1. 2. 3.) instead of (1. 1. 1.)
        Assert.Contains("1. First item", content);
        Assert.Contains("2. Second item", content);
        Assert.Contains("3. Third item", content);

        // Should NOT have repeated "1." for all items
        var oneCount = content.Split("1.").Length - 1;
        Assert.Equal(1, oneCount); // Only one "1." expected
    }

    [Fact]
    public async Task ExtractAsync_OrderedList_ShouldPreserveListMarkers()
    {
        // Arrange
        var markdown = @"## Steps

1. Step one
2. Step two
3. Step three
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "test.md");

        // Assert
        var content = result.Text;

        Assert.Contains("<!-- LIST_START:ORDERED -->", content);
        Assert.Contains("<!-- LIST_END:ORDERED -->", content);
    }

    [Fact]
    public async Task ExtractAsync_UnorderedList_ShouldUseBulletPoints()
    {
        // Arrange
        var markdown = @"## Features

- Feature A
- Feature B
- Feature C
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "test.md");

        // Assert
        var content = result.Text;

        Assert.Contains("<!-- LIST_START:UNORDERED -->", content);
        Assert.Contains("• Feature A", content);
        Assert.Contains("• Feature B", content);
        Assert.Contains("• Feature C", content);
    }

    [Fact]
    public async Task ExtractAsync_LongOrderedList_ShouldHaveCorrectNumbering()
    {
        // Arrange
        var markdown = @"## Ten Items

1. Item one
2. Item two
3. Item three
4. Item four
5. Item five
6. Item six
7. Item seven
8. Item eight
9. Item nine
10. Item ten
";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "test.md");

        // Assert
        var content = result.Text;

        // Verify all numbers are present
        Assert.Contains("1. Item one", content);
        Assert.Contains("5. Item five", content);
        Assert.Contains("10. Item ten", content);
    }

    // Link reference definitions (`[label]: url`) are Markdig metadata, not renderable
    // content. Markdig parses them into a LinkReferenceDefinitionGroup block that has no
    // matching ExtractBlock case and used to fall through to the NormalizeRenderer default
    // arm, leaking the raw definition plus a trailing `[]:` into chunk text (Filer saw the
    // trailing `[]:` after tables/lists in osi-model.md / tcp-vs-udp.md). The reader now
    // skips these blocks entirely.

    [Fact]
    public async Task ExtractAsync_LabeledReferenceDefinition_ShouldStripDefinitionAndKeepInlineLink()
    {
        // Arrange — a labeled reference definition must be stripped from content, while the
        // referencing link's display text and resolved target stay intact (no content loss).
        var markdown = "# Doc\n\nSee [link][1] here.\n\n[1]: https://example.com\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "doc.md");

        // Assert — definition line and trailing []: gone, inline link text/target preserved
        Assert.DoesNotContain("[]:", result.Text);
        Assert.DoesNotContain("[1]: https://example.com", result.Text);
        Assert.Contains("link", result.Text);
        Assert.Contains("https://example.com", result.Text); // resolved inline link target retained
    }

    [Fact]
    public async Task ExtractAsync_TableThenReferenceDefinition_ShouldNotLeakTrailingBracketColon()
    {
        // Arrange — reference definition at end of document, right after a table. This is the
        // exact Filer shape: the group renders a trailing `[]:` glued onto the table's tail.
        var markdown = "# TCP vs UDP\n\n| Protocol | Use |\n|---|---|\n| TCP | Streaming, DNS, games |\n\n[ref]: https://example.com/spec\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "tcp.md");

        // Assert — no leaked definition or placeholder, table content intact
        Assert.DoesNotContain("[]:", result.Text);
        Assert.DoesNotContain("[ref]:", result.Text);
        Assert.Contains("Streaming, DNS, games", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_MultipleReferenceDefinitions_ShouldStripAll()
    {
        // Arrange — several labeled definitions; none may survive into chunk content.
        var markdown = "# Refs\n\nText with [a][1] and [b][2].\n\n[1]: https://a.example\n[2]: https://b.example\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(markdown));

        // Act
        var result = await _reader.ExtractAsync(stream, "refs.md");

        // Assert
        Assert.DoesNotContain("[]:", result.Text);
        Assert.DoesNotContain("[1]:", result.Text);
        Assert.DoesNotContain("[2]:", result.Text);
        Assert.Contains("Text with", result.Text); // body preserved
    }
}
