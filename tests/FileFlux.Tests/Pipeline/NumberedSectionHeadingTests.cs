using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// With <see cref="RefineOptions.BuildSections"/>, numbered lines that are section titles become headings. A numbered
/// reference list, footnotes, inline numbered points, a table of contents — lists — must stay lists: turned into
/// headings, every reference becomes a "section" and a section list of an encyclopedia export runs to tens of thousands
/// of characters.
/// </summary>
public class NumberedSectionHeadingTests
{
    [Fact]
    public async Task A_Numbered_Reference_List_Stays_A_List_Under_Its_Heading()
    {
        var text = string.Join('\n',
            "# References",
            "",
            "1. Bryant, Donald A.; Frigaard, Niels-Ulrik (Nov 2006). \"Prokaryotic photosynthesis and phototrophy illuminated\". Trends in Microbiology. 14 (11): 488-496.",
            "2. Reece J, Urry L, Cain M, Wasserman S, Minorsky P, Jackson R (2011). Biology (International ed.). Upper Saddle River, NJ: Pearson Education.",
            "3. Olson JM (May 2006). \"Photosynthesis in the Archean era\". Photosynthesis Research. 88 (2): 109-117.");
        var refiner = new DocumentRefiner(markdownConverter: null, logger: NullLogger<DocumentRefiner>.Instance);

        var refined = await refiner.RefineAsync(Raw(text), new RefineOptions { BuildSections = true }, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("## 1.", refined.Text, StringComparison.Ordinal);
        Assert.Contains("\n1. Bryant", refined.Text, StringComparison.Ordinal);
        Assert.Equal(["References"], refined.Sections.Select(s => s.Title));
    }

    [Fact]
    public void Numbered_Sections_Separated_By_Body_Text_Become_Headings()
    {
        // The shape the promotion exists for: plain text whose sections are numbered lines.
        var text = "1. 개요\n이 문서는 시스템을 설명한다.\n\n2. 범위\n적용 범위는 다음과 같다.\n\n3-1. 세부 범위\n세부 내용.\n\n3-1-1. 예외\n예외 내용.";

        var result = DocumentRefiner.ConvertNumberedSectionsToHeadings(text);

        Assert.Contains("## 1. 개요", result, StringComparison.Ordinal);
        Assert.Contains("## 2. 범위", result, StringComparison.Ordinal);
        Assert.Contains("### 3-1. 세부 범위", result, StringComparison.Ordinal);
        Assert.Contains("#### 3-1-1. 예외", result, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Run_Of_Short_Numbered_Lines_Is_A_List_Not_Sections()
    {
        // A table of contents or a list of steps: short items, each next to the next.
        var text = "Contents\n\n1. Overview\n2. Light-dependent reactions\n3. Evolution\n\nBody.";

        var result = DocumentRefiner.ConvertNumberedSectionsToHeadings(text);

        Assert.Equal(text, result);
    }

    [Fact]
    public void A_Long_Numbered_Sentence_In_The_Body_Is_Not_A_Title()
    {
        var text = "The reaction has two products.\n1. One product of oxygenase activity is phosphoglycolate, which is recycled at a cost.\nThe other is glycerate.";

        Assert.Equal(text, DocumentRefiner.ConvertNumberedSectionsToHeadings(text));
    }

    [Fact]
    public void A_Figure_Legend_Label_Starting_Lowercase_Is_Not_A_Title()
    {
        // A diagram's numbered labels, interleaved with the body text they sit beside in the PDF layout.
        var text = "Chloroplast ultrastructure:\n1. outer membrane\nIn plants and algae, photosynthesis takes place in chloroplasts.\n2. intermembrane space\nThe chloroplast is enclosed by a membrane.";

        Assert.Equal(text, DocumentRefiner.ConvertNumberedSectionsToHeadings(text));
    }

    [Fact]
    public void A_Short_Numbered_Line_Ending_Like_A_Sentence_Is_Not_A_Title()
    {
        var text = "Intro text.\n1. Mix the samples.\nMore text.";

        Assert.Equal(text, DocumentRefiner.ConvertNumberedSectionsToHeadings(text));
    }

    [Fact]
    public void A_One_Item_List_With_A_Heading_After_It_Is_Not_A_Title()
    {
        // A notes list with a single entry, then the next section: nothing below it is its body.
        var text = "# Notes\n\n1. /ˌfoʊtoʊˈsɪnθəsɪs/ FOH-toh-SINTH-ə-sis\n\n# References";

        Assert.Equal(text, DocumentRefiner.ConvertNumberedSectionsToHeadings(text));
    }

    [Fact]
    public void Numbered_Lines_Inside_A_Code_Fence_Are_Untouched()
    {
        var text = "Setup:\n```\n1. step\n```\nDone";

        Assert.Equal(text, DocumentRefiner.ConvertNumberedSectionsToHeadings(text));
    }

    [Fact]
    public void Parenthesized_Items_Under_A_Numbered_Section_Stay_A_List_While_The_Section_Becomes_A_Heading()
    {
        var text = "1. 준비물\n(1) 노트북\n(2) 전원 어댑터\n\n본문.";

        var result = DocumentRefiner.ConvertNumberedSectionsToHeadings(text);

        Assert.StartsWith("## 1. 준비물\n(1) 노트북\n(2) 전원 어댑터", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_Endings_Are_Preserved()
    {
        Assert.Equal("## 1. Scope\r\nBody.\r\n", DocumentRefiner.ConvertNumberedSectionsToHeadings("1. Scope\r\nBody.\r\n"));
    }

    private static RawContent Raw(string text) => new()
    {
        Id = Guid.NewGuid(),
        Text = text,
        File = new SourceFileInfo
        {
            Name = "doc.md",
            Extension = ".md",
            Size = text.Length,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
        },
    };
}
