using AwesomeAssertions;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Parsers;
using FileFlux.Infrastructure.Services;
using Xunit;

namespace FileFlux.Tests.Options;

/// <summary>
/// Six options that a hard-coded literal used to stand in for (roster verdict D, run 58 cycle-920). Each fact
/// runs both values: the option must change the outcome, and its default must reproduce what the literal did.
/// </summary>
public sealed class WiredOptionsTests
{
    private static RawContent WithImages(params int[] sizes)
    {
        var raw = new RawContent { Text = "text" };
        for (var i = 0; i < sizes.Length; i++)
        {
            raw.Images.Add(new ImageInfo { Id = $"img{i}", Data = new byte[sizes[i]], OriginalSize = sizes[i], MimeType = "image/png" });
        }
        raw.Hints["has_images"] = true;
        raw.Hints["image_count"] = sizes.Length;
        return raw;
    }

    [Fact]
    public void ExtractImages_off_drops_every_image_and_the_image_hints()
    {
        var kept = ImageExtractionPolicy.Apply(WithImages(10, 20), new ExtractOptions { ExtractImages = true });
        var dropped = ImageExtractionPolicy.Apply(WithImages(10, 20), new ExtractOptions { ExtractImages = false });

        kept.Images.Should().HaveCount(2);
        dropped.Images.Should().BeEmpty();
        dropped.Hints.Should().NotContainKey("has_images").And.NotContainKey("image_count");
        ImageExtractionPolicy.Apply(WithImages(10), null).Images.Should().HaveCount(1, "no options means the reader's result as collected");
    }

    [Fact]
    public void MaxImageSize_drops_only_the_images_above_it_and_says_which()
    {
        var raw = ImageExtractionPolicy.Apply(WithImages(100, 5000, 200), new ExtractOptions { MaxImageSize = 1000 });

        raw.Images.Select(i => i.Id).Should().Equal("img0", "img2");
        raw.Hints["image_count"].Should().Be(2);
        raw.Warnings.Should().ContainSingle(w => w.Contains("img1") && w.Contains("5000") && w.Contains("MaxImageSize"));
        ImageExtractionPolicy.Apply(WithImages(100, 5000), new ExtractOptions { MaxImageSize = null }).Images.Should().HaveCount(2, "null is no limit");
    }

    [Fact]
    public async Task DocumentParsingOptions_ExtractMetadata_off_yields_empty_metadata()
    {
        var parser = new BasicDocumentParser();
        var raw = new RawContent { Text = string.Join(" ", Enumerable.Repeat("word", 120)) + "\n\n# Heading\n\nmore words here" };
        raw.Hints["page_count"] = 3;

        var with = await parser.ParseAsync(raw, new DocumentParsingOptions { ExtractMetadata = true, UseLlmParsing = false }, TestContext.Current.CancellationToken);
        var without = await parser.ParseAsync(raw, new DocumentParsingOptions { ExtractMetadata = false, UseLlmParsing = false }, TestContext.Current.CancellationToken);

        with.Metadata.WordCount.Should().BeGreaterThan(0);
        with.Metadata.PageCount.Should().Be(3);
        without.Metadata.WordCount.Should().Be(0);
        without.Metadata.PageCount.Should().Be(0);
        without.Text.Should().Be(with.Text, "only the metadata is affected");
    }

    [Theory]
    [InlineData(100, 400)]
    [InlineData(10, 40)]
    public void MetadataEnrichmentOptions_MaxTokens_bounds_the_sampled_content(int maxTokens, int expectedChars)
    {
        var content = new string('x', 10_000);

        var bounded = AIMetadataEnricher.TruncateContent(content, MetadataExtractionStrategy.Deep, maxTokens);
        var byStrategy = AIMetadataEnricher.TruncateContent(content, MetadataExtractionStrategy.Deep, null);

        bounded.Should().StartWith(new string('x', expectedChars));
        bounded.Substring(expectedChars).Should().NotStartWith("x", "the budget is tokens times CharsPerToken");
        byStrategy.Should().StartWith(new string('x', 8000), "null keeps the strategy's default (Deep = 8000 chars)");
    }

    [Fact]
    public void LlmRefineOptions_PreserveFormatting_changes_the_prompt_rule()
    {
        var keep = LlmRefiner.FormattingRule(preserveFormatting: true);
        var relax = LlmRefiner.FormattingRule(preserveFormatting: false);

        keep.Should().Contain("Preserve the original formatting");
        relax.Should().Contain("may be normalized");
        keep.Should().NotBe(relax);
    }
}
