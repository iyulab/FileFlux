using FileFlux.Infrastructure.Adapters;

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
    [InlineData("<!-- table: too wide to keep as a table -->")]
    [InlineData("<!--ffspan:7-->")]
    public void Every_structural_marker_is_removed_from_a_part_of_a_cut_block_too(string marker)
    {
        var clean = Strip("```text\nline\n" + marker + "\nline two");

        Assert.Equal("```text\nline\nline two", clean);
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
