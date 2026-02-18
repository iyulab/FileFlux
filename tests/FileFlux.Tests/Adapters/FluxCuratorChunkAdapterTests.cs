using FileFlux.Core;
using FileFlux.Infrastructure.Adapters;
using FluentAssertions;
using FluxCuratorChunk = FluxCurator.Core.Domain.DocumentChunk;
using FluxCuratorLocation = FluxCurator.Core.Domain.ChunkLocation;
using FluxCuratorMetadata = FluxCurator.Core.Domain.ChunkMetadata;
using FluxCuratorStrategy = FluxCurator.Core.Domain.ChunkingStrategy;

namespace FileFlux.Tests.Adapters;

public class FluxCuratorChunkAdapterTests
{
    #region ToFileFluxChunk

    [Fact]
    public void ToFileFluxChunk_BasicConversion_MapsAllFields()
    {
        var source = new FluxCuratorChunk
        {
            Content = "Test chunk content",
            Index = 3,
            TotalChunks = 10,
            Location = new FluxCuratorLocation
            {
                StartPosition = 100,
                EndPosition = 200,
                SectionPath = "Chapter 1 > Section 2"
            },
            Metadata = new FluxCuratorMetadata
            {
                QualityScore = 0.85f,
                DensityScore = 0.72f,
                EstimatedTokenCount = 42,
                Strategy = FluxCuratorStrategy.Paragraph,
                LanguageCode = "en"
            }
        };

        var result = source.ToFileFluxChunk();

        result.Content.Should().Be("Test chunk content");
        result.Index.Should().Be(3);
        result.Quality.Should().BeApproximately(0.85, 0.01);
        result.Density.Should().BeApproximately(0.72, 0.01);
        result.Tokens.Should().Be(42);
        result.Strategy.Should().Be("Paragraph");
        result.SourceInfo.Language.Should().Be("en");
        result.SourceInfo.LanguageConfidence.Should().Be(1.0);
    }

    [Fact]
    public void ToFileFluxChunk_WithParsedIdAndRawId_SetsTraceability()
    {
        var parsedId = Guid.NewGuid();
        var rawId = Guid.NewGuid();
        var source = new FluxCuratorChunk { Content = "content" };

        var result = source.ToFileFluxChunk(parsedId, rawId);

        result.ParsedId.Should().Be(parsedId);
        result.RawId.Should().Be(rawId);
    }

    [Fact]
    public void ToFileFluxChunk_WithoutIds_DefaultsToEmpty()
    {
        var source = new FluxCuratorChunk { Content = "content" };

        var result = source.ToFileFluxChunk();

        result.ParsedId.Should().Be(Guid.Empty);
        result.RawId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void ToFileFluxChunk_ValidGuidId_ParsesCorrectly()
    {
        var expectedId = Guid.NewGuid();
        var source = new FluxCuratorChunk
        {
            Id = expectedId.ToString("N"),
            Content = "content"
        };

        var result = source.ToFileFluxChunk();

        result.Id.Should().Be(expectedId);
    }

    [Fact]
    public void ToFileFluxChunk_InvalidGuidId_GeneratesNewGuid()
    {
        var source = new FluxCuratorChunk
        {
            Id = "not-a-guid",
            Content = "content"
        };

        var result = source.ToFileFluxChunk();

        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ToFileFluxChunk_SectionPath_ParsedIntoHeadingPath()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Location = new FluxCuratorLocation
            {
                SectionPath = "Chapter 1 > Section 2 > Subsection A"
            }
        };

        var result = source.ToFileFluxChunk();

        result.Location.HeadingPath.Should().HaveCount(3);
        result.Location.HeadingPath[0].Should().Be("Chapter 1");
        result.Location.HeadingPath[1].Should().Be("Section 2");
        result.Location.HeadingPath[2].Should().Be("Subsection A");
    }

    [Fact]
    public void ToFileFluxChunk_NullSectionPath_EmptyHeadingPath()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Location = new FluxCuratorLocation { SectionPath = null }
        };

        var result = source.ToFileFluxChunk();

        result.Location.HeadingPath.Should().BeEmpty();
    }

    [Fact]
    public void ToFileFluxChunk_Location_MapsPositions()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Location = new FluxCuratorLocation
            {
                StartPosition = 50,
                EndPosition = 150
            }
        };

        var result = source.ToFileFluxChunk();

        result.Location.StartChar.Should().Be(50);
        result.Location.EndChar.Should().Be(150);
    }

    [Fact]
    public void ToFileFluxChunk_CustomProperties_Copied()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Metadata = new FluxCuratorMetadata
            {
                Custom = new Dictionary<string, object>
                {
                    ["key1"] = "value1",
                    ["key2"] = 42
                }
            }
        };

        var result = source.ToFileFluxChunk();

        result.Props.Should().ContainKey("key1");
        result.Props["key1"].Should().Be("value1");
        result.Props["key2"].Should().Be(42);
    }

    [Fact]
    public void ToFileFluxChunk_NullCustomProperties_EmptyProps()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Metadata = new FluxCuratorMetadata { Custom = null }
        };

        var result = source.ToFileFluxChunk();

        result.Props.Should().BeEmpty();
    }

    [Fact]
    public void ToFileFluxChunk_NullSource_ThrowsArgumentNullException()
    {
        FluxCuratorChunk? source = null;

        var act = () => source!.ToFileFluxChunk();

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ToFluxCuratorChunk

    [Fact]
    public void ToFluxCuratorChunk_BasicConversion_MapsAllFields()
    {
        var id = Guid.NewGuid();
        var source = new DocumentChunk
        {
            Id = id,
            Content = "FileFlux chunk content",
            Index = 5,
            Quality = 0.9,
            Density = 0.8,
            Tokens = 100,
            Strategy = "semantic",
            SourceInfo = new SourceMetadataInfo
            {
                Language = "ko",
                ChunkCount = 20
            },
            Location = new SourceLocation
            {
                StartChar = 200,
                EndChar = 400,
                HeadingPath = new List<string> { "Part 1", "Chapter 2" }
            }
        };

        var result = source.ToFluxCuratorChunk();

        result.Id.Should().Be(id.ToString("N"));
        result.Content.Should().Be("FileFlux chunk content");
        result.Index.Should().Be(5);
        result.TotalChunks.Should().Be(20);
        result.Metadata.LanguageCode.Should().Be("ko");
        result.Metadata.EstimatedTokenCount.Should().Be(100);
        result.Metadata.QualityScore.Should().BeApproximately(0.9f, 0.01f);
        result.Metadata.DensityScore.Should().BeApproximately(0.8f, 0.01f);
        result.Metadata.Strategy.Should().Be(FluxCuratorStrategy.Semantic);
        result.Metadata.ContainsSectionHeader.Should().BeTrue();
    }

    [Fact]
    public void ToFluxCuratorChunk_HeadingPath_JoinedIntoSectionPath()
    {
        var source = new DocumentChunk
        {
            Content = "content",
            Location = new SourceLocation
            {
                HeadingPath = new List<string> { "A", "B", "C" }
            }
        };

        var result = source.ToFluxCuratorChunk();

        result.Location.SectionPath.Should().Be("A > B > C");
    }

    [Fact]
    public void ToFluxCuratorChunk_EmptyHeadingPath_UsesSectionField()
    {
        var source = new DocumentChunk
        {
            Content = "content",
            Location = new SourceLocation
            {
                HeadingPath = new List<string>(),
                Section = "Section X"
            }
        };

        var result = source.ToFluxCuratorChunk();

        result.Location.SectionPath.Should().Be("Section X");
        result.Metadata.ContainsSectionHeader.Should().BeFalse();
    }

    [Fact]
    public void ToFluxCuratorChunk_CustomProps_Copied()
    {
        var source = new DocumentChunk
        {
            Content = "content",
            Props = new Dictionary<string, object>
            {
                ["myProp"] = "myValue"
            }
        };

        var result = source.ToFluxCuratorChunk();

        result.Metadata.Custom.Should().NotBeNull();
        result.Metadata.Custom!["myProp"].Should().Be("myValue");
    }

    [Fact]
    public void ToFluxCuratorChunk_EmptyProps_NullCustom()
    {
        var source = new DocumentChunk
        {
            Content = "content",
            Props = new Dictionary<string, object>()
        };

        var result = source.ToFluxCuratorChunk();

        result.Metadata.Custom.Should().BeNull();
    }

    [Fact]
    public void ToFluxCuratorChunk_NullSource_ThrowsArgumentNullException()
    {
        DocumentChunk? source = null;

        var act = () => source!.ToFluxCuratorChunk();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToFluxCuratorChunk_LocationPositions_Mapped()
    {
        var source = new DocumentChunk
        {
            Content = "content",
            Location = new SourceLocation
            {
                StartChar = 10,
                EndChar = 50,
                StartPage = 3,
                EndPage = 5
            }
        };

        var result = source.ToFluxCuratorChunk();

        result.Location.StartPosition.Should().Be(10);
        result.Location.EndPosition.Should().Be(50);
        result.Location.StartLine.Should().Be(3);
        result.Location.EndLine.Should().Be(5);
    }

    #endregion

    #region Strategy Parsing

    [Theory]
    [InlineData("sentence", FluxCuratorStrategy.Sentence)]
    [InlineData("paragraph", FluxCuratorStrategy.Paragraph)]
    [InlineData("token", FluxCuratorStrategy.Token)]
    [InlineData("semantic", FluxCuratorStrategy.Semantic)]
    [InlineData("hierarchical", FluxCuratorStrategy.Hierarchical)]
    [InlineData("SENTENCE", FluxCuratorStrategy.Sentence)]
    [InlineData("Paragraph", FluxCuratorStrategy.Paragraph)]
    [InlineData("", FluxCuratorStrategy.Auto)]
    [InlineData("unknown_strategy", FluxCuratorStrategy.Auto)]
    public void ToFluxCuratorChunk_StrategyParsing(string strategy, FluxCuratorStrategy expected)
    {
        var source = new DocumentChunk
        {
            Content = "content",
            Strategy = strategy
        };

        var result = source.ToFluxCuratorChunk();

        result.Metadata.Strategy.Should().Be(expected);
    }

    #endregion

    #region Importance Calculation

    [Fact]
    public void ToFileFluxChunk_WithHierarchyLevel0_HighImportance()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Metadata = new FluxCuratorMetadata
            {
                Custom = new Dictionary<string, object> { ["HierarchyLevel"] = 0 }
            }
        };

        var result = source.ToFileFluxChunk();

        result.Importance.Should().Be(1.0);
    }

    [Fact]
    public void ToFileFluxChunk_WithHierarchyLevel5_LowerImportance()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Metadata = new FluxCuratorMetadata
            {
                Custom = new Dictionary<string, object> { ["HierarchyLevel"] = 5 }
            }
        };

        var result = source.ToFileFluxChunk();

        result.Importance.Should().Be(0.5);
    }

    [Fact]
    public void ToFileFluxChunk_WithHierarchyLevel10_ClampsToMinimum()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Metadata = new FluxCuratorMetadata
            {
                Custom = new Dictionary<string, object> { ["HierarchyLevel"] = 10 }
            }
        };

        var result = source.ToFileFluxChunk();

        result.Importance.Should().Be(0.5); // Max(0.5, 1.0 - 10*0.1) = 0.5
    }

    [Fact]
    public void ToFileFluxChunk_NoHierarchyLevel_DefaultsToQualityScore()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content",
            Metadata = new FluxCuratorMetadata { QualityScore = 0.77f }
        };

        var result = source.ToFileFluxChunk();

        result.Importance.Should().BeApproximately(0.77, 0.01);
    }

    #endregion

    #region Context Dependency Calculation

    [Fact]
    public void ToFileFluxChunk_WithOverlap_HigherContextDependency()
    {
        var source = new FluxCuratorChunk
        {
            Content = "This is the full chunk content for testing overlap behavior.",
            Metadata = new FluxCuratorMetadata
            {
                OverlapFromPrevious = "This is the full chunk"
            }
        };

        var result = source.ToFileFluxChunk();

        result.ContextDependency.Should().BeGreaterThan(0.3);
    }

    [Fact]
    public void ToFileFluxChunk_SentenceBoundaries_LowContextDependency()
    {
        var source = new FluxCuratorChunk
        {
            Content = "Complete sentence chunk.",
            Metadata = new FluxCuratorMetadata
            {
                StartsAtSentenceBoundary = true,
                EndsAtSentenceBoundary = true
            }
        };

        var result = source.ToFileFluxChunk();

        result.ContextDependency.Should().Be(0.1);
    }

    [Fact]
    public void ToFileFluxChunk_NoOverlapNoSentenceBoundary_DefaultContextDependency()
    {
        var source = new FluxCuratorChunk
        {
            Content = "content chunk",
            Metadata = new FluxCuratorMetadata
            {
                StartsAtSentenceBoundary = false,
                EndsAtSentenceBoundary = false
            }
        };

        var result = source.ToFileFluxChunk();

        result.ContextDependency.Should().Be(0.3);
    }

    #endregion

    #region Batch Conversions

    [Fact]
    public void ToFileFluxChunks_ConvertsAll()
    {
        var sources = new[]
        {
            new FluxCuratorChunk { Content = "chunk1", Index = 0 },
            new FluxCuratorChunk { Content = "chunk2", Index = 1 },
            new FluxCuratorChunk { Content = "chunk3", Index = 2 }
        };

        var results = sources.ToFileFluxChunks();

        results.Should().HaveCount(3);
        results[0].Content.Should().Be("chunk1");
        results[1].Content.Should().Be("chunk2");
        results[2].Content.Should().Be("chunk3");
    }

    [Fact]
    public void ToFileFluxChunks_WithIds_PropagatesIds()
    {
        var parsedId = Guid.NewGuid();
        var rawId = Guid.NewGuid();
        var sources = new[]
        {
            new FluxCuratorChunk { Content = "chunk1" },
            new FluxCuratorChunk { Content = "chunk2" }
        };

        var results = sources.ToFileFluxChunks(parsedId, rawId);

        results.Should().AllSatisfy(c =>
        {
            c.ParsedId.Should().Be(parsedId);
            c.RawId.Should().Be(rawId);
        });
    }

    [Fact]
    public void ToFluxCuratorChunks_ConvertsAll()
    {
        var sources = new[]
        {
            new DocumentChunk { Content = "ff1", Index = 0 },
            new DocumentChunk { Content = "ff2", Index = 1 }
        };

        var results = sources.ToFluxCuratorChunks();

        results.Should().HaveCount(2);
        results[0].Content.Should().Be("ff1");
        results[1].Content.Should().Be("ff2");
    }

    #endregion
}
