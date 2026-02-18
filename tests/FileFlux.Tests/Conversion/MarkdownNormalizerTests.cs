using FileFlux.Core;
using FileFlux.Infrastructure;

namespace FileFlux.Tests.Conversion;

public class MarkdownNormalizerTests
{
    private readonly MarkdownNormalizer _normalizer = new();

    #region Normalize — General

    [Fact]
    public void Normalize_NullOptions_UsesDefaults()
    {
        var result = _normalizer.Normalize("# Title\n\nContent", null);

        result.Markdown.Should().Contain("# Title");
        result.OriginalMarkdown.Should().Contain("# Title");
    }

    [Fact]
    public void Normalize_EmptyMarkdown_ReturnsEmpty()
    {
        var result = _normalizer.Normalize("");

        result.Markdown.Should().BeEmpty();
        result.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Normalize_NoIssues_NoChanges()
    {
        var md = "# Title\n\n## Section\n\nContent here.";

        var result = _normalizer.Normalize(md);

        result.HasChanges.Should().BeFalse();
        result.Stats.HeadingsFound.Should().Be(2);
    }

    [Fact]
    public void Normalize_PreservesOriginalMarkdown()
    {
        var original = "#### Deep Start\n\nContent";

        var result = _normalizer.Normalize(original);

        result.OriginalMarkdown.Should().Be(original);
    }

    #endregion

    #region Phase 1 — DemoteAnnotationHeadings

    [Fact]
    public void DemoteAnnotation_ParenthesizedContent_Demoted()
    {
        var md = "## (증가율)\n\nSome content";
        var options = OnlyPhase(o => o.DemoteAnnotationHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("(증가율)");
        result.Markdown.Should().NotContain("## (증가율)");
        result.Stats.HeadingsDemoted.Should().Be(1);
    }

    [Fact]
    public void DemoteAnnotation_FullWidthParens_Demoted()
    {
        var md = "### （감소율）\n\nText";
        var options = OnlyPhase(o => o.DemoteAnnotationHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().NotContain("### ");
        result.Stats.HeadingsDemoted.Should().Be(1);
    }

    [Fact]
    public void DemoteAnnotation_NoteSymbol_Demoted()
    {
        var md = "## ※ 주석\n\nContent";
        var options = OnlyPhase(o => o.DemoteAnnotationHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("※ 주석");
        result.Markdown.Should().NotContain("## ※");
        result.Stats.HeadingsDemoted.Should().Be(1);
    }

    [Fact]
    public void DemoteAnnotation_Bullet_Demoted()
    {
        var md = "## • 항목\n\nContent";
        var options = OnlyPhase(o => o.DemoteAnnotationHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.HeadingsDemoted.Should().Be(1);
    }

    [Fact]
    public void DemoteAnnotation_PunctuationOnly_Demoted()
    {
        var md = "## ...\n\nContent";
        var options = OnlyPhase(o => o.DemoteAnnotationHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.HeadingsDemoted.Should().Be(1);
    }

    [Fact]
    public void DemoteAnnotation_RealHeading_NotDemoted()
    {
        var md = "## Real Section Title\n\nContent";
        var options = OnlyPhase(o => o.DemoteAnnotationHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("## Real Section Title");
        result.Stats.HeadingsDemoted.Should().Be(0);
    }

    [Fact]
    public void DemoteAnnotation_Disabled_NoChanges()
    {
        var md = "## (증가율)\n\nContent";
        var options = OnlyPhase(_ => { }); // all disabled

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("## (증가율)");
    }

    #endregion

    #region Phase 2 — RemoveEmptyHeadings

    [Fact]
    public void RemoveEmpty_EmptyHeading_Removed()
    {
        var md = "##\n\nContent after";
        var options = OnlyPhase(o => o.RemoveEmptyHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().NotContain("##");
        result.Stats.HeadingsRemoved.Should().Be(1);
    }

    [Fact]
    public void RemoveEmpty_WhitespaceOnlyHeading_Removed()
    {
        var md = "###    \n\nContent after";
        var options = OnlyPhase(o => o.RemoveEmptyHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.HeadingsRemoved.Should().Be(1);
    }

    [Fact]
    public void RemoveEmpty_HeadingWithContent_Preserved()
    {
        var md = "## Valid Title\n\nContent";
        var options = OnlyPhase(o => o.RemoveEmptyHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("## Valid Title");
        result.Stats.HeadingsRemoved.Should().Be(0);
    }

    [Fact]
    public void RemoveEmpty_MultipleEmptyHeadings_AllRemoved()
    {
        var md = "##\n###\n####\n\nContent";
        var options = OnlyPhase(o => o.RemoveEmptyHeadings = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.HeadingsRemoved.Should().Be(3);
    }

    #endregion

    #region Phase 3 — NormalizeHeadingHierarchy

    [Fact]
    public void HeadingHierarchy_FirstHeadingTooDeep_Promoted()
    {
        var md = "#### Deep Start\n\nContent";
        var options = OnlyPhase(o =>
        {
            o.NormalizeHeadings = true;
            o.MaxFirstHeadingLevel = 2;
        });

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().StartWith("# Deep Start");
        result.Stats.HeadingsAdjusted.Should().Be(1);
        result.Actions.Should().Contain(a => a.Type == NormalizationActionType.FirstHeadingPromoted);
    }

    [Fact]
    public void HeadingHierarchy_FirstHeadingAtAllowedLevel_NotPromoted()
    {
        var md = "## Normal Start\n\nContent";
        var options = OnlyPhase(o =>
        {
            o.NormalizeHeadings = true;
            o.MaxFirstHeadingLevel = 2;
        });

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("## Normal Start");
        result.Stats.HeadingsAdjusted.Should().Be(0);
    }

    [Fact]
    public void HeadingHierarchy_LevelJump_Adjusted()
    {
        var md = "# Title\n\n#### Jumped\n\nContent";
        var options = OnlyPhase(o =>
        {
            o.NormalizeHeadings = true;
            o.MaxHeadingLevelJump = 1;
        });

        var result = _normalizer.Normalize(md, options);

        // H1 → H4 should become H1 → H2 (max jump 1)
        result.Markdown.Should().Contain("## Jumped");
        result.Stats.HeadingsAdjusted.Should().Be(1);
    }

    [Fact]
    public void HeadingHierarchy_AllowedJump_NoChange()
    {
        var md = "# Title\n\n## Next\n\nContent";
        var options = OnlyPhase(o =>
        {
            o.NormalizeHeadings = true;
            o.MaxHeadingLevelJump = 1;
        });

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Contain("## Next");
        result.Stats.HeadingsAdjusted.Should().Be(0);
    }

    [Fact]
    public void HeadingHierarchy_CascadingAdjustment()
    {
        var md = "# Title\n\n##### H5\n\n###### H6\n\nContent";
        var options = OnlyPhase(o =>
        {
            o.NormalizeHeadings = true;
            o.MaxHeadingLevelJump = 1;
        });

        var result = _normalizer.Normalize(md, options);

        // H1 → H5 → H6 becomes H1 → H2 → H3
        result.Markdown.Should().Contain("## H5");
        result.Markdown.Should().Contain("### H6");
        result.Stats.HeadingsAdjusted.Should().Be(2);
    }

    #endregion

    #region Phase 4 — NormalizeListStructure

    [Fact]
    public void ListStructure_ExcessiveIndentJump_Normalized()
    {
        var md = "- Item 1\n          - Deep nested item";
        var options = OnlyPhase(o => o.NormalizeLists = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.ListItemsNormalized.Should().Be(1);
        result.Actions.Should().Contain(a => a.Type == NormalizationActionType.ListIndentNormalized);
    }

    [Fact]
    public void ListStructure_NormalIndent_NoChange()
    {
        var md = "- Item 1\n  - Sub item";
        var options = OnlyPhase(o => o.NormalizeLists = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.ListItemsNormalized.Should().Be(0);
    }

    [Fact]
    public void ListStructure_NonListLine_ResetsList()
    {
        var md = "- List 1\n\nParagraph\n\n- New list\n          - Deep";
        var options = OnlyPhase(o => o.NormalizeLists = true);

        var result = _normalizer.Normalize(md, options);

        // After paragraph break, "New list" becomes new base, then "Deep" is indent-jumped
        result.Stats.ListItemsNormalized.Should().Be(1);
    }

    [Fact]
    public void ListStructure_OrderedList_HandledCorrectly()
    {
        var md = "1. First\n          2. Deep second";
        var options = OnlyPhase(o => o.NormalizeLists = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.ListItemsNormalized.Should().Be(1);
    }

    #endregion

    #region Phase 5 — NormalizeTables

    [Fact]
    public void Tables_ValidTable_Preserved()
    {
        var md = "| Col1 | Col2 |\n|------|------|\n| A    | B    |";
        var options = OnlyPhase(o => o.NormalizeTables = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.TablesFound.Should().Be(1);
        result.Stats.TablesPreserved.Should().Be(1);
        result.Markdown.Should().Contain("| Col1 | Col2 |");
    }

    [Fact]
    public void Tables_MalformedColumnCount_Converted()
    {
        var md = "| A | B | C |\n|---|---|---|\n| X | Y |";
        var options = OnlyPhase(o =>
        {
            o.NormalizeTables = true;
            o.MaxColumnVariance = 0;
        });

        var result = _normalizer.Normalize(md, options);

        result.Stats.TablesConvertedToText.Should().Be(1);
        result.Markdown.Should().Contain("<table>");
        result.Markdown.Should().Contain("</table>");
    }

    [Fact]
    public void Tables_NoSeparator_Complex()
    {
        // 3+ row table without separator line → complex
        var md = "| A | B |\n| C | D |\n| E | F |";
        var options = OnlyPhase(o => o.NormalizeTables = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.TablesConvertedToText.Should().Be(1);
        result.Actions.Should().Contain(a => a.Type == NormalizationActionType.ComplexTableConverted);
    }

    [Fact]
    public void Tables_SingleRowTable_TooFewRows()
    {
        var md = "| Only row |";
        var options = OnlyPhase(o => o.NormalizeTables = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.TablesConvertedToText.Should().Be(1);
    }

    [Fact]
    public void Tables_AllowColumnVariance()
    {
        var md = "| A | B | C |\n|---|---|---|\n| X | Y |";
        var options = OnlyPhase(o =>
        {
            o.NormalizeTables = true;
            o.MaxColumnVariance = 1; // allow 1 column difference
        });

        var result = _normalizer.Normalize(md, options);

        result.Stats.TablesPreserved.Should().Be(1);
    }

    [Fact]
    public void Tables_MultipleTables_ProcessedIndependently()
    {
        var md = "| A | B |\n|---|---|\n| X | Y |\n\nParagraph\n\n| Only |";
        var options = OnlyPhase(o => o.NormalizeTables = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.TablesFound.Should().Be(2);
    }

    #endregion

    #region Phase 6 — NormalizeWhitespace

    [Fact]
    public void Whitespace_TrailingSpaces_Removed()
    {
        var md = "Content   \nMore   ";
        var options = OnlyPhase(o => o.NormalizeWhitespace = true);

        var result = _normalizer.Normalize(md, options);

        result.Markdown.Should().Be("Content\nMore");
    }

    [Fact]
    public void Whitespace_ExcessiveBlankLines_ReducedToTwo()
    {
        var md = "First\n\n\n\n\nSecond";
        var options = OnlyPhase(o => o.NormalizeWhitespace = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.BlankLinesRemoved.Should().BeGreaterThan(0);
        // Should have at most 2 consecutive blank lines
        result.Markdown.Should().NotContain("\n\n\n\n");
    }

    [Fact]
    public void Whitespace_TwoBlankLines_Preserved()
    {
        var md = "First\n\n\nSecond";
        var options = OnlyPhase(o => o.NormalizeWhitespace = true);

        var result = _normalizer.Normalize(md, options);

        result.Stats.BlankLinesRemoved.Should().Be(0);
    }

    [Fact]
    public void Whitespace_BlankLinesLogsAction()
    {
        var md = "First\n\n\n\n\nSecond";
        var options = OnlyPhase(o => o.NormalizeWhitespace = true);

        var result = _normalizer.Normalize(md, options);

        result.Actions.Should().Contain(a => a.Type == NormalizationActionType.ExcessiveBlankLinesRemoved);
    }

    #endregion

    #region Combined phases

    [Fact]
    public void AllPhases_DefaultOptions_WorkTogether()
    {
        var md = "#### Deep Title\n\n##\n\n## (주석)\n\n| A |\n\n- Item\n          - Deep\n\nContent   \n\n\n\n\nEnd";

        var result = _normalizer.Normalize(md);

        result.HasChanges.Should().BeTrue();
        result.Stats.HeadingsAdjusted.Should().BeGreaterThan(0);
        result.Stats.HeadingsRemoved.Should().BeGreaterThan(0);
        result.Stats.HeadingsDemoted.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AllPhases_Disabled_NoChanges()
    {
        var md = "#### Deep\n\n##\n\n## ※ note\n\nContent   \n\n\n\n\nEnd";
        var options = new NormalizationOptions
        {
            NormalizeHeadings = false,
            RemoveEmptyHeadings = false,
            NormalizeLists = false,
            NormalizeWhitespace = false,
            DemoteAnnotationHeadings = false,
            NormalizeTables = false
        };

        var result = _normalizer.Normalize(md, options);

        result.HasChanges.Should().BeFalse();
        result.Markdown.Should().Be(md);
    }

    #endregion

    #region NormalizationResult model

    [Fact]
    public void NormalizationResult_HasChanges_TrueWhenActionsExist()
    {
        var result = new NormalizationResult
        {
            Actions = [new NormalizationAction { Type = NormalizationActionType.EmptyHeadingRemoved }]
        };

        result.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void NormalizationResult_HasChanges_FalseWhenEmpty()
    {
        var result = new NormalizationResult();

        result.HasChanges.Should().BeFalse();
    }

    #endregion

    #region NormalizationOptions defaults

    [Fact]
    public void NormalizationOptions_DefaultValues()
    {
        var options = NormalizationOptions.Default;

        options.NormalizeHeadings.Should().BeTrue();
        options.RemoveEmptyHeadings.Should().BeTrue();
        options.NormalizeLists.Should().BeTrue();
        options.NormalizeWhitespace.Should().BeTrue();
        options.DemoteAnnotationHeadings.Should().BeTrue();
        options.NormalizeTables.Should().BeTrue();
        options.MaxColumnVariance.Should().Be(0);
        options.MaxHeadingLevelJump.Should().Be(1);
        options.MaxFirstHeadingLevel.Should().Be(2);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates NormalizationOptions with only the specified phase enabled.
    /// </summary>
    private static NormalizationOptions OnlyPhase(Action<NormalizationOptions> configure)
    {
        var options = new NormalizationOptions
        {
            NormalizeHeadings = false,
            RemoveEmptyHeadings = false,
            NormalizeLists = false,
            NormalizeWhitespace = false,
            DemoteAnnotationHeadings = false,
            NormalizeTables = false
        };
        configure(options);
        return options;
    }

    #endregion
}
