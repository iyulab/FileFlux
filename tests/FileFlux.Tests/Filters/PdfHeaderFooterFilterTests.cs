using FileFlux.Infrastructure.Filters;

namespace FileFlux.Tests.Filters;

public class PdfHeaderFooterFilterTests
{
    #region Constructor

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new PdfHeaderFooterFilter(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_DefaultOptions_CreatesInstance()
    {
        var filter = new PdfHeaderFooterFilter();

        filter.Should().NotBeNull();
    }

    #endregion

    #region Filter — Bypass paths

    [Fact]
    public void Filter_NotEnabled_ReturnsOriginal()
    {
        var filter = new PdfHeaderFooterFilter(new PdfHeaderFooterFilter.Options { Enabled = false });
        var content = "Header\nBody\nHeader";

        var result = filter.Filter(content, 10);

        result.Should().Be(content);
    }

    [Fact]
    public void Filter_EmptyContent_ReturnsOriginal()
    {
        var filter = CreateEnabledFilter();

        filter.Filter("", 10).Should().Be("");
    }

    [Fact]
    public void Filter_WhitespaceContent_ReturnsOriginal()
    {
        var filter = CreateEnabledFilter();

        filter.Filter("   \n  ", 10).Should().Be("   \n  ");
    }

    [Fact]
    public void Filter_BelowMinPageCount_ReturnsOriginal()
    {
        var filter = CreateEnabledFilter(minPages: 5);
        var content = "Header\nBody\nHeader\nBody2\nHeader";

        filter.Filter(content, 4).Should().Be(content);
    }

    [Fact]
    public void Filter_ExactMinPageCount_Processes()
    {
        var filter = CreateEnabledFilter(minPages: 3, threshold: 0.5);
        // "Confidential" appears 3 times in 3-page doc → thresholdCount=Max(2, 1)=2 → removed
        var content = "Confidential\nFirst section\nConfidential\nSecond section\nConfidential\nThird section";

        var result = filter.Filter(content, 3);

        result.Should().NotContain("Confidential");
        result.Should().Contain("First section");
    }

    #endregion

    #region Filter — Core repetition detection

    [Fact]
    public void Filter_RemovesRepetitiveLines()
    {
        var filter = CreateEnabledFilter(threshold: 0.5);
        // Header appears 5 times in 10-page doc → ratio 0.5 >= threshold → removed
        var lines = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            lines.Add("Company Confidential");
            lines.Add($"Actual content paragraph {i + 1}");
        }
        var content = string.Join("\n", lines);

        var result = filter.Filter(content, 10);

        result.Should().NotContain("Company Confidential");
        result.Should().Contain("Actual content paragraph 1");
        result.Should().Contain("Actual content paragraph 5");
    }

    [Fact]
    public void Filter_KeepsLinesBelowThreshold()
    {
        var filter = CreateEnabledFilter(threshold: 0.5);
        // "Rare line" appears only once in 10-page doc → ratio 0.1 < 0.5 → kept
        var content = "Rare line\nHeader\nHeader\nHeader\nHeader\nHeader\nBody";

        var result = filter.Filter(content, 10);

        result.Should().Contain("Rare line");
        result.Should().Contain("Body");
    }

    [Fact]
    public void Filter_ThresholdMinimumIsTwo()
    {
        // Even with very low threshold, minimum count is 2
        var filter = CreateEnabledFilter(threshold: 0.01);
        // 1 occurrence never gets removed since Math.Max(2, ...) enforces minimum
        var content = "SingleLine\nBody text here";

        var result = filter.Filter(content, 100);

        result.Should().Contain("SingleLine");
    }

    [Fact]
    public void Filter_MaxLineLength_IgnoresLongLines()
    {
        var filter = CreateEnabledFilter(threshold: 0.3, maxLineLength: 50);
        var longLine = new string('X', 201); // exceeds default 200 char limit
        var lines = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            lines.Add(longLine);
            lines.Add($"Content {i}");
        }
        var content = string.Join("\n", lines);

        var result = filter.Filter(content, 10);

        // Long lines should be preserved (not detected as header/footer)
        result.Should().Contain(longLine);
    }

    [Fact]
    public void Filter_PreservesEmptyLines()
    {
        var filter = CreateEnabledFilter(threshold: 0.5);
        var content = "Header\n\nBody\n\nHeader\nMore content\n\nHeader";

        var result = filter.Filter(content, 5);

        // Empty lines should be preserved for formatting
        result.Should().Contain("\n\n");
    }

    #endregion

    #region Filter — Page number normalization

    [Theory]
    [InlineData("Page 1", "Page 2", "Page 3")]
    [InlineData("page 42", "page 99", "page 7")]
    [InlineData("Page  5", "Page  10", "Page  15")]
    public void Filter_EnglishPageNumbers_Normalized(string p1, string p2, string p3)
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        // These should all normalize to the same pattern and be detected
        var content = $"{p1}\nContent A\n{p2}\nContent B\n{p3}\nContent C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("Page");
        result.Should().Contain("Content A");
    }

    [Fact]
    public void Filter_KoreanPageNumbers_Normalized()
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        var content = "1페이지\nContent A\n2페이지\nContent B\n3페이지\nContent C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("페이지");
        result.Should().Contain("Content A");
    }

    [Fact]
    public void Filter_ChinesePageNumbers_Normalized()
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        var content = "第1页\nContent A\n第2页\nContent B\n第3页\nContent C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("第");
        result.Should().Contain("Content A");
    }

    [Fact]
    public void Filter_GermanPageNumbers_Normalized()
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        var content = "Seite1\nContent A\nSeite2\nContent B\nSeite3\nContent C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("Seite");
        result.Should().Contain("Content A");
    }

    [Fact]
    public void Filter_FractionalPageNumbers_Normalized()
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        var content = "1/10\nContent A\n2/10\nContent B\n3/10\nContent C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("/10");
        result.Should().Contain("Content A");
    }

    [Fact]
    public void Filter_DateNormalization_DetectsVariants()
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        // Dates with different values should normalize to the same pattern
        var content = "Report 2024-01-15\nContent A\nReport 2024-02-20\nContent B\nReport 2024-03-25\nContent C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("Report");
        result.Should().Contain("Content A");
    }

    [Fact]
    public void Filter_DateFormats_BothDirections()
    {
        var filter = CreateEnabledFilter(threshold: 0.3);
        // US-style dates (MM/DD/YYYY)
        var content = "Issued 01/15/2024\nBody A\nIssued 02/20/2024\nBody B\nIssued 03/25/2024\nBody C";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("Issued");
        result.Should().Contain("Body A");
    }

    #endregion

    #region Filter — PreservePatterns / RemovePatterns

    [Fact]
    public void Filter_PreservePatterns_KeepsMatchedLines()
    {
        var options = new PdfHeaderFooterFilter.Options
        {
            Enabled = true,
            RepetitionThreshold = 0.3,
            MinPageCount = 3,
            PreservePatterns = ["DISCLAIMER"]
        };
        var filter = new PdfHeaderFooterFilter(options);

        // "DISCLAIMER" appears repeatedly but is preserved
        var content = "DISCLAIMER\nContent\nDISCLAIMER\nMore content\nDISCLAIMER";

        var result = filter.Filter(content, 5);

        result.Should().Contain("DISCLAIMER");
    }

    [Fact]
    public void Filter_RemovePatterns_RemovesMatchedLines()
    {
        var options = new PdfHeaderFooterFilter.Options
        {
            Enabled = true,
            RepetitionThreshold = 0.99, // very high — won't trigger frequency-based removal
            MinPageCount = 3,
            RemovePatterns = ["^Draft.*$"]
        };
        var filter = new PdfHeaderFooterFilter(options);
        var content = "Draft v1.0\nContent body\nRegular line";

        var result = filter.Filter(content, 5);

        result.Should().NotContain("Draft");
        result.Should().Contain("Content body");
    }

    [Fact]
    public void Filter_InvalidRegexInRemovePatterns_SkipsGracefully()
    {
        var options = new PdfHeaderFooterFilter.Options
        {
            Enabled = true,
            MinPageCount = 1,
            RemovePatterns = ["[invalid", "^Valid$"]
        };
        var filter = new PdfHeaderFooterFilter(options);
        var content = "Valid\nOther content";

        // Should not throw — invalid regex is silently skipped
        var result = filter.Filter(content, 5);

        result.Should().NotContain("Valid");
        result.Should().Contain("Other content");
    }

    [Fact]
    public void Filter_InvalidRegexInPreservePatterns_SkipsGracefully()
    {
        var options = new PdfHeaderFooterFilter.Options
        {
            Enabled = true,
            RepetitionThreshold = 0.3,
            MinPageCount = 3,
            PreservePatterns = ["[broken"]
        };
        var filter = new PdfHeaderFooterFilter(options);
        var content = "Repeated\nBody\nRepeated\nBody2\nRepeated";

        // Should process normally — invalid regex skipped, "Repeated" gets removed
        var result = filter.Filter(content, 5);

        result.Should().NotContain("Repeated");
    }

    #endregion

    #region AnalyzePatterns

    [Fact]
    public void AnalyzePatterns_EmptyContent_ReturnsEmpty()
    {
        var filter = new PdfHeaderFooterFilter();

        var patterns = filter.AnalyzePatterns("", 10);

        patterns.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzePatterns_SinglePage_ReturnsEmpty()
    {
        var filter = new PdfHeaderFooterFilter();

        var patterns = filter.AnalyzePatterns("Header\nBody\nHeader", 1);

        patterns.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzePatterns_DetectsRepetitivePatterns()
    {
        var filter = new PdfHeaderFooterFilter();
        var content = "Header Line\nContent A\nHeader Line\nContent B\nHeader Line\nContent C";

        var patterns = filter.AnalyzePatterns(content, 10);

        patterns.Should().ContainSingle();
        patterns[0].NormalizedText.Should().Be("Header Line");
        patterns[0].Occurrences.Should().Be(3);
        patterns[0].Ratio.Should().BeApproximately(0.3, 0.01);
    }

    [Fact]
    public void AnalyzePatterns_WouldBeFiltered_RespectsThreshold()
    {
        var options = new PdfHeaderFooterFilter.Options { RepetitionThreshold = 0.5 };
        var filter = new PdfHeaderFooterFilter(options);
        var content = "Low\nLow\nContent\nHigh\nHigh\nHigh\nHigh\nHigh";

        var patterns = filter.AnalyzePatterns(content, 5);

        var high = patterns.Single(p => p.NormalizedText == "High");
        var low = patterns.Single(p => p.NormalizedText == "Low");

        high.WouldBeFiltered.Should().BeTrue();  // 5/5 = 1.0 >= 0.5
        low.WouldBeFiltered.Should().BeFalse();   // 2/5 = 0.4 < 0.5
    }

    [Fact]
    public void AnalyzePatterns_OrderedByOccurrenceDescending()
    {
        var filter = new PdfHeaderFooterFilter();
        var content = "A\nA\nB\nB\nB\nC\nC\nC\nC";

        var patterns = filter.AnalyzePatterns(content, 10);

        patterns.Should().HaveCount(3);
        patterns[0].Occurrences.Should().Be(4); // C
        patterns[1].Occurrences.Should().Be(3); // B
        patterns[2].Occurrences.Should().Be(2); // A
    }

    [Fact]
    public void AnalyzePatterns_NormalizesPageNumbers()
    {
        var filter = new PdfHeaderFooterFilter();
        var content = "Page 1\nContent\nPage 2\nContent\nPage 3";

        var patterns = filter.AnalyzePatterns(content, 5);

        // "Page 1/2/3" should normalize to the same "PAGE_NUM" pattern
        var pagePattern = patterns.SingleOrDefault(p => p.NormalizedText.Contains("PAGE_NUM"));
        pagePattern.Should().NotBeNull();
        pagePattern!.Occurrences.Should().Be(3);
    }

    [Fact]
    public void AnalyzePatterns_IgnoresLongLines()
    {
        var filter = new PdfHeaderFooterFilter(new PdfHeaderFooterFilter.Options { MaxLineLength = 50 });
        var longLine = new string('X', 51);
        var content = $"{longLine}\n{longLine}\n{longLine}\nShort\nShort";

        var patterns = filter.AnalyzePatterns(content, 5);

        // Long lines should be ignored
        patterns.Should().ContainSingle();
        patterns[0].NormalizedText.Should().Be("Short");
    }

    [Fact]
    public void AnalyzePatterns_PreservePatterns_MarksWouldBeFilteredFalse()
    {
        var options = new PdfHeaderFooterFilter.Options
        {
            RepetitionThreshold = 0.3,
            PreservePatterns = ["Important"]
        };
        var filter = new PdfHeaderFooterFilter(options);
        var content = "Important Notice\nBody\nImportant Notice\nBody\nImportant Notice";

        var patterns = filter.AnalyzePatterns(content, 5);

        var important = patterns.Single(p => p.NormalizedText.Contains("Important"));
        important.WouldBeFiltered.Should().BeFalse();
    }

    #endregion

    #region DetectedPattern model

    [Fact]
    public void DetectedPattern_DefaultValues()
    {
        var pattern = new PdfHeaderFooterFilter.DetectedPattern();

        pattern.OriginalText.Should().BeEmpty();
        pattern.NormalizedText.Should().BeEmpty();
        pattern.Occurrences.Should().Be(0);
        pattern.Ratio.Should().Be(0);
        pattern.WouldBeFiltered.Should().BeFalse();
    }

    #endregion

    #region Options model

    [Fact]
    public void Options_DefaultValues()
    {
        var options = new PdfHeaderFooterFilter.Options();

        options.Enabled.Should().BeFalse();
        options.RepetitionThreshold.Should().Be(0.5);
        options.MinPageCount.Should().Be(3);
        options.MaxLineLength.Should().Be(200);
        options.PreservePatterns.Should().BeEmpty();
        options.RemovePatterns.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static PdfHeaderFooterFilter CreateEnabledFilter(
        double threshold = 0.5,
        int minPages = 3,
        int maxLineLength = 200)
    {
        return new PdfHeaderFooterFilter(new PdfHeaderFooterFilter.Options
        {
            Enabled = true,
            RepetitionThreshold = threshold,
            MinPageCount = minPages,
            MaxLineLength = maxLineLength
        });
    }

    #endregion
}
