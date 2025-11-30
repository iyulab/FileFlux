using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Strategies;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Strategies;

public class HierarchicalChunkingStrategyTests
{
    private readonly ITestOutputHelper _output;
    private readonly HierarchicalChunkingStrategy _strategy;

    public HierarchicalChunkingStrategyTests(ITestOutputHelper output)
    {
        _output = output;
        _strategy = new HierarchicalChunkingStrategy();
    }

    [Fact]
    public async Task ChunkAsync_WithMarkdownHeaders_CreatesHierarchicalChunks()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = @"# Introduction

This is the introduction section of the document.

## Background

Here is some background information that provides context.

## Methods

This section describes the methods used.

### Data Collection

Data was collected from multiple sources.

### Analysis

The analysis was performed using standard techniques.

# Conclusion

This is the conclusion of the document.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.md",
                FileType = "text/markdown"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Hierarchical",
            MaxChunkSize = 500
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        _output.WriteLine($"Total chunks created: {chunks.Count}");

        foreach (var chunk in chunks)
        {
            _output.WriteLine($"Chunk {chunk.Index}: {chunk.Content.Substring(0, Math.Min(50, chunk.Content.Length))}...");
            if (chunk is HierarchicalDocumentChunk hierarchical)
            {
                _output.WriteLine($"  Level: {hierarchical.Level}, Type: {hierarchical.Type}, HasChildren: {hierarchical.HasChildren}");
            }
        }

        // Verify hierarchy properties are set
        var hierarchicalChunks = chunks.OfType<HierarchicalDocumentChunk>().ToList();
        Assert.NotEmpty(hierarchicalChunks);
        Assert.Contains(hierarchicalChunks, c => c.Level == 1); // Section level
    }

    [Fact]
    public async Task ChunkAsync_SetsParentChildRelationships()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = @"# Main Section

Main section content here.

## Subsection One

Subsection one content that is detailed enough to be meaningful.

## Subsection Two

Subsection two content with additional information.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.md",
                FileType = "text/markdown"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Hierarchical",
            MaxChunkSize = 300
        };
        options.StrategyOptions["MinSectionLength"] = 20;

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();
        var hierarchicalChunks = chunks.OfType<HierarchicalDocumentChunk>().ToList();

        // Assert
        Assert.NotEmpty(hierarchicalChunks);

        // Find parent chunks
        var parentChunks = hierarchicalChunks.Where(c => c.HasChildren).ToList();
        _output.WriteLine($"Parent chunks: {parentChunks.Count}");

        foreach (var parent in parentChunks)
        {
            _output.WriteLine($"Parent: {parent.Content.Substring(0, Math.Min(30, parent.Content.Length))}... has {parent.ChildIds.Count} children");
        }
    }

    [Fact]
    public async Task ChunkAsync_SetsHierarchyPropsKeys()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = @"# Document Title

This is the document content.

## Section One

Section one has detailed content.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.md",
                FileType = "text/markdown"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Hierarchical",
            MaxChunkSize = 500
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        // Assert
        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.HierarchyLevel));
            Assert.True(chunk.Props.ContainsKey(ChunkPropsKeys.HierarchyChunkType));
            _output.WriteLine($"Chunk {chunk.Index}: Level={chunk.Props[ChunkPropsKeys.HierarchyLevel]}, Type={chunk.Props[ChunkPropsKeys.HierarchyChunkType]}");
        }
    }

    [Fact]
    public void StrategyName_ReturnsHierarchical()
    {
        Assert.Equal("Hierarchical", _strategy.StrategyName);
    }

    [Fact]
    public void SupportedOptions_ContainsExpectedOptions()
    {
        var supportedOptions = _strategy.SupportedOptions.ToList();

        Assert.Contains("MaxParentChunkSize", supportedOptions);
        Assert.Contains("MaxChildChunkSize", supportedOptions);
        Assert.Contains("MinSectionLength", supportedOptions);
        Assert.Contains("MaxHierarchyDepth", supportedOptions);
    }
}
