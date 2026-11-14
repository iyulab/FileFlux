using FileFlux.Infrastructure;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// <see cref="MarkdownFences"/>: the one fenced-code-block scan the text-level heading, list and whitespace rules share.
/// </summary>
public sealed class MarkdownFencesTests
{
    private static string[] Fenced(string text) =>
        [.. MarkdownFences.FindRanges(text).Select(r => text[r.Start..r.End])];

    [Fact]
    public void A_backtick_fence_runs_from_its_opening_line_to_the_end_of_its_closing_line()
    {
        const string text = "before\n```bash\n# comment\n```\nafter\n";

        Assert.Equal(["```bash\n# comment\n```\n"], Fenced(text));
    }

    [Fact]
    public void A_tilde_fence_is_a_fence_and_a_backtick_line_does_not_close_it()
    {
        const string text = "~~~\ncode\n```\nstill code\n~~~\nprose\n";

        Assert.Equal(["~~~\ncode\n```\nstill code\n~~~\n"], Fenced(text));
    }

    [Fact]
    public void A_longer_fence_is_closed_only_by_a_fence_at_least_as_long()
    {
        const string text = "````\n```\ninner\n```\n````\nprose\n";

        Assert.Equal(["````\n```\ninner\n```\n````\n"], Fenced(text));
    }

    [Fact]
    public void A_closing_fence_carries_no_info_string()
    {
        // «```bash» inside an open fence is code, not the end of it.
        const string text = "```\na\n```bash\nb\n```\nprose\n";

        Assert.Equal(["```\na\n```bash\nb\n```\n"], Fenced(text));
    }

    [Fact]
    public void A_fence_that_is_never_closed_runs_to_the_end_of_the_text()
    {
        const string text = "prose\n```\n# not a heading\nmore";

        Assert.Equal(["```\n# not a heading\nmore"], Fenced(text));
    }

    [Fact]
    public void A_backtick_line_whose_info_string_holds_a_backtick_is_not_a_fence()
    {
        // «```code``` here» is inline code at the start of a line.
        const string text = "```code``` here\n# a heading\n";

        Assert.Empty(MarkdownFences.FindRanges(text));
    }

    [Fact]
    public void An_indented_fence_in_a_list_item_is_a_fence()
    {
        const string text = "- item\n\n      ```bash\n      # comment\n      ```\n- next\n";

        Assert.Equal(["      ```bash\n      # comment\n      ```\n"], Fenced(text));
    }

    [Fact]
    public void Windows_line_endings_do_not_change_the_ranges()
    {
        const string text = "a\r\n```\r\n# c\r\n```\r\nb\r\n";

        Assert.Equal(["```\r\n# c\r\n```\r\n"], Fenced(text));
    }

    [Fact]
    public void Two_fenced_blocks_are_two_ranges_and_contains_finds_a_position_in_either()
    {
        const string text = "x\n```\na\n```\ny\n~~~\nb\n~~~\nz\n";
        var ranges = MarkdownFences.FindRanges(text);

        Assert.Equal(2, ranges.Count);
        Assert.True(MarkdownFences.Contains(ranges, text.IndexOf('a', StringComparison.Ordinal)));
        Assert.True(MarkdownFences.Contains(ranges, text.IndexOf('b', StringComparison.Ordinal)));
        Assert.False(MarkdownFences.Contains(ranges, text.IndexOf('x', StringComparison.Ordinal)));
        Assert.False(MarkdownFences.Contains(ranges, text.IndexOf('y', StringComparison.Ordinal)));
        Assert.False(MarkdownFences.Contains(ranges, text.IndexOf('z', StringComparison.Ordinal)));
    }

    [Fact]
    public void The_line_mask_marks_the_fence_lines_and_the_lines_between_them()
    {
        string[] lines = ["prose", "```", "# code", "```", "# heading", "~~~", "x"];

        Assert.Equal([false, true, true, true, false, true, true], MarkdownFences.LineMask(lines));
    }

    [Fact]
    public void MapOutside_transforms_only_the_text_between_fences()
    {
        const string text = "a\n```\nb\n```\nc\n";

        var result = MarkdownFences.MapOutside(text, segment => segment.ToUpperInvariant());

        Assert.Equal("A\n```\nb\n```\nC\n", result);
    }

    [Fact]
    public void MapOutside_keeps_the_whitespace_where_a_segment_meets_a_fence_even_for_a_transform_that_trims()
    {
        const string text = "a\n\n```\nb\n```\n\nc\n";

        var result = MarkdownFences.MapOutside(text, segment => segment.Trim());

        Assert.Equal("a\n\n```\nb\n```\n\nc", result);
    }

    [Fact]
    public void MapOutside_transforms_a_text_without_a_fence_as_a_whole()
    {
        Assert.Equal("[ a ]", MarkdownFences.MapOutside(" a ", segment => "[" + segment + "]"));
    }

    [Fact]
    public void A_heading_chunk_starting_where_the_previous_section_ends_belongs_to_the_next_section_only()
    {
        const string text = "# A\n\nfirst\n\n## B\n\nsecond\n\n## C\n\nthird\n";
        var sections = SectionPathCalculator.BuildSections(text);

        var atC = SectionPathCalculator.CalculateHeadingPath(sections, text.IndexOf("## C", StringComparison.Ordinal), 0);

        Assert.Equal(["A", "C"], atC);
    }
}
