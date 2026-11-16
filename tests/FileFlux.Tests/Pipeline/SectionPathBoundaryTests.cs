using FileFlux.Infrastructure;
using Xunit;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// <see cref="SectionPathCalculator"/>: which section a position belongs to. (The fenced-block scan it relies on is
/// FluxCurator's <c>MarkdownFences</c>; its own facts live in that package.)
/// </summary>
public sealed class SectionPathBoundaryTests
{
    [Fact]
    public void A_heading_chunk_starting_where_the_previous_section_ends_belongs_to_the_next_section_only()
    {
        const string text = "# A\n\nfirst\n\n## B\n\nsecond\n\n## C\n\nthird\n";
        var sections = SectionPathCalculator.BuildSections(text);

        var atC = SectionPathCalculator.CalculateHeadingPath(sections, text.IndexOf("## C", StringComparison.Ordinal), 0);

        Assert.Equal(["A", "C"], atC);
    }
}
