using FileFlux.Core;
using FileFlux.Infrastructure.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// A chunk is a slice of the document: a code block larger than a chunk is cut across two of them, and then neither part is
/// a closed fence the cleaning could hide. FileFlux's own structural markers are removed from the chunk text, but the code
/// of such a part is still code: its HTML comments and its blank lines stay.
/// </summary>
public sealed class ChunkCutFenceMarkerTests
{
    private static string Strip(string content) => FluxCuratorChunkAdapter.StripInternalMarkers(content);

    [Fact]
    public void The_head_of_a_cut_code_block_keeps_its_comments_and_blank_lines()
    {
        const string content = "Intro\n\n<!-- CODE_START -->\n```xml\n<Project>\n  <!-- keep me -->\n\n\n  <A />";

        var clean = Strip(content);

        Assert.Equal("Intro\n\n```xml\n<Project>\n  <!-- keep me -->\n\n\n  <A />", clean);
    }

    [Fact]
    public void The_tail_of_a_cut_code_block_keeps_its_comments_and_blank_lines()
    {
        const string content = "  <B />\n  <!-- keep me too -->\n\n\n  <C />\n</Project>\n```\n<!-- CODE_END -->\n\nOutro";

        var clean = Strip(content);

        Assert.Equal("  <B />\n  <!-- keep me too -->\n\n\n  <C />\n</Project>\n```\n\nOutro", clean);
    }

    [Fact]
    public void The_tail_of_a_cut_code_block_followed_by_a_closed_block_with_an_info_string_still_keeps_its_comments()
    {
        // The orphan closing fence pairs with the closing fence of the next block: three fence lines, nothing unclosed.
        const string content = "<!-- keep -->\n\n\ncode\n```\n<!-- CODE_END -->\n\n<!-- CODE_START -->\n```js\nlet x;\n```\n<!-- CODE_END -->";

        var clean = Strip(content);

        Assert.Contains("<!-- keep -->\n\n\ncode\n```", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("CODE_START", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("CODE_END", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marker_glued_to_its_fence_by_a_chunker_is_removed_and_the_code_after_it_is_kept()
    {
        // The shape FluxCurator 0.19.0 produced at an overlap seam: the fence line is not a fence, so nothing hides the block.
        const string content = "Prose\n\n<!-- CODE_START -->```xml\n<!-- keep -->\n\n\n<A />\n```\n<!-- CODE_END -->";

        var clean = Strip(content);

        Assert.Equal("Prose\n\n```xml\n<!-- keep -->\n\n\n<A />\n```", clean);
    }

    [Theory]
    [InlineData("<!-- HEADING_START:H2 -->")]
    [InlineData("<!-- HEADING_END:H2 -->")]
    [InlineData("<!-- LIST_START:UNORDERED -->")]
    [InlineData("<!-- LIST_END:ORDERED -->")]
    [InlineData("<!-- TABLE_START -->")]
    [InlineData("<!-- IMAGE_START:IMG_3 -->")]
    [InlineData("<!-- DOCUMENT_IMAGES_END -->")]
    [InlineData("<!-- table: No data rows in table -->")]
    [InlineData("<!-- table: Column count varies from 2 to 5 (variance: 3, max allowed: 2) -->")]
    [InlineData("<!-- table: Complex table structure (missing separator) -->")]
    [InlineData("<!-- PRESENTATION_IMAGES_START -->")]
    [InlineData("<!-- SPREADSHEET_IMAGES_END -->")]
    [InlineData("<!--ffspan:7-->")]
    public void Every_structural_marker_is_removed_from_a_part_of_a_cut_block_too(string marker)
    {
        var clean = Strip("```text\nline\n" + marker + "\nline two");

        Assert.Equal("```text\nline\nline two", clean);
    }

    [Theory]
    [InlineData("<!-- MAIN_CONTENT_START -->")]
    [InlineData("<!-- FOOTER_END -->")]
    [InlineData("<!-- table: users -->")]
    [InlineData("<!-- SECTION_END -->")]
    public void A_comment_of_the_codes_own_with_the_shape_of_a_marker_is_kept_in_a_cut_block(string comment)
    {
        var clean = Strip("```html\n<body>\n" + comment + "\n<div/>");

        Assert.Equal("```html\n<body>\n" + comment + "\n<div/>", clean);
    }

    [Fact]
    public async Task A_long_code_block_keeps_every_comment_and_blank_line_through_the_default_path_in_every_piece()
    {
        // A block of ~120 lines is cut into several chunks at the small chunk size below: head, middle pieces and tail.
        var code = string.Join("\n", Enumerable.Range(0, 40).Select(i =>
            "  <!-- note " + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + " -->\n  <Item Id=\"" + i + "\" />\n"));
        var text = "# Sample\n\nBefore the code.\n\n```xml\n<Root>\n" + code + "</Root>\n```\n\nAfter the code.\n";
        var path = Path.Combine(Path.GetTempPath(), "fileflux-cutblock-" + Guid.NewGuid().ToString("N") + ".md");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        try
        {
            var services = new ServiceCollection();
            services.AddFileFlux();
            await using var provider = services.BuildServiceProvider();
            await using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);

            await processor.ExtractAsync(cancellationToken: TestContext.Current.CancellationToken);
            await processor.RefineAsync(cancellationToken: TestContext.Current.CancellationToken);
            await processor.ChunkAsync(new ChunkingOptions { MaxChunkSize = 120, MinChunkSize = 20, OverlapSize = 20 }, TestContext.Current.CancellationToken);

            var chunks = processor.Result.Chunks!;
            var stream = string.Join('\n', chunks.Select(chunk => chunk.Content));

            Assert.True(chunks.Count >= 4, "the block must be cut into head, middle and tail pieces (" + chunks.Count + " chunks)");
            for (var i = 0; i < 40; i++)
                Assert.Contains("<!-- note " + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + " -->", stream, StringComparison.Ordinal);
            Assert.DoesNotContain("CODE_START", stream, StringComparison.Ordinal);
            Assert.DoesNotContain("CODE_END", stream, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_piece_from_the_middle_of_a_block_is_kept_when_the_chunk_position_says_it_is_inside_a_fence()
    {
        // A block cut into three chunks: the middle one has no fence line, so only its position can tell.
        const string whole = "Intro\n\n```xml\n<A />\n  <!-- keep me -->\n\n\n  <B />\n  <C />\n```\n\nOutro\n";
        const string middle = "  <!-- keep me -->\n\n\n  <B />";
        var start = whole.IndexOf(middle, StringComparison.Ordinal);
        var ranges = FluxCuratorChunkAdapter.FenceRanges(whole);
        var chunk = new FluxCurator.Core.Domain.DocumentChunk
        {
            Content = middle,
            Location = new FluxCurator.Core.Domain.ChunkLocation { StartPosition = start, EndPosition = start + middle.Length },
        };

        Assert.True(FluxCuratorChunkAdapter.IsPartOfCutFence(ranges, chunk));
        Assert.Equal(middle, FluxCuratorChunkAdapter.StripInternalMarkers(middle, cutFence: true));
        // Without the position the text alone cannot tell, and the middle piece is cleaned like prose (the old behaviour).
        Assert.NotEqual(middle, FluxCuratorChunkAdapter.StripInternalMarkers(middle));
    }

    [Fact]
    public void A_chunk_that_holds_a_block_whole_or_lies_outside_every_block_is_not_a_cut_piece()
    {
        const string whole = "Intro\n\n```xml\n<A />\n```\n\nOutro\n";
        var ranges = FluxCuratorChunkAdapter.FenceRanges(whole);
        var holdsWhole = new FluxCurator.Core.Domain.DocumentChunk
        {
            Content = whole,
            Location = new FluxCurator.Core.Domain.ChunkLocation { StartPosition = 0, EndPosition = whole.Length },
        };
        var outside = new FluxCurator.Core.Domain.DocumentChunk
        {
            Content = "Outro",
            Location = new FluxCurator.Core.Domain.ChunkLocation { StartPosition = whole.IndexOf("Outro", StringComparison.Ordinal), EndPosition = whole.Length },
        };

        Assert.False(FluxCuratorChunkAdapter.IsPartOfCutFence(ranges, holdsWhole));
        Assert.False(FluxCuratorChunkAdapter.IsPartOfCutFence(ranges, outside));
    }

    [Fact]
    public void A_chunk_without_a_cut_block_still_drops_every_html_comment_outside_code_and_collapses_blank_lines()
    {
        const string content = "Prose <!-- a note --> more.\n\n<!-- HEADING_START:H2 -->\n\n\n\n## T\n\n```text\n<!-- in code -->\n\n\nx\n```";

        var clean = Strip(content);

        Assert.DoesNotContain("a note", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("HEADING_START", clean, StringComparison.Ordinal);
        Assert.Contains("```text\n<!-- in code -->\n\n\nx\n```", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n\n", clean, StringComparison.Ordinal);
    }
}
