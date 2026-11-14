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

    private static string TidyLines(string text) =>
        string.Join("\n", text.Split('\n').Select(line => line.Trim()));

    [Fact]
    public void A_shielded_step_never_sees_the_code_of_a_block_and_the_block_comes_back_as_written()
    {
        // The step trims every line - prose is tidied, the indentation, blank lines and trailing spaces of code are code.
        const string text = "  prose one  \n```python\n    deep\n\n\nEND  \nEND\n```\n  prose two  \n";

        var result = MarkdownFences.Shielded(text, TidyLines);

        Assert.Equal("prose one\n```python\n    deep\n\n\nEND  \nEND\n```\nprose two\n", result);
    }

    [Fact]
    public void A_shielded_step_still_sees_one_continuous_text_around_the_blocks()
    {
        // A line that repeats across the blocks is counted over the whole text, not once per stretch of prose.
        const string text = "page header\n```\nx\n```\npage header\n```\ny\n```\npage header\n";

        var result = MarkdownFences.Shielded(text, source =>
        {
            var lines = source.Split('\n');
            var repeated = lines.GroupBy(l => l).Where(g => g.Key.Length > 0 && g.Count() >= 3).Select(g => g.Key).ToHashSet();
            return string.Join("\n", lines.Where(l => !repeated.Contains(l)));
        });

        Assert.Equal("```\nx\n```\n```\ny\n```\n", result);
    }

    [Fact]
    public void A_shielded_step_that_removes_a_placeholder_leaves_the_text_unchanged()
    {
        const string text = "prose\n```\ncode\n```\nprose\n";

        Assert.Equal(text, MarkdownFences.Shielded(text, _ => "everything gone"));
    }

    [Fact]
    public void A_text_without_a_fence_goes_to_the_shielded_step_as_it_is()
    {
        Assert.Equal("[ a ]", MarkdownFences.Shielded(" a ", text => "[" + text + "]"));
    }

    [Fact]
    public void An_unclosed_fence_is_left_to_a_text_step_but_still_counts_as_a_block_for_the_headings()
    {
        // A text step is a heuristic over text of any origin: a line of tildes is as often a separator as a fence, so an
        // unclosed fence does not hide the rest of the document from it. The heading scan keeps the CommonMark reading.
        const string text = "  prose  \n```\n  code without a closing fence";

        Assert.Equal("prose\n```\ncode without a closing fence", MarkdownFences.Shielded(text, TidyLines));
        Assert.Single(MarkdownFences.FindRanges(text));
        Assert.Empty(MarkdownFences.FindRanges(text, includeUnclosed: false));
    }

    [Fact]
    public void Windows_line_endings_around_a_shielded_block_survive()
    {
        const string text = "a\r\n```\r\n  b\r\n```\r\nc\r\n";

        Assert.Equal(text, MarkdownFences.Shielded(text, source => source));
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
