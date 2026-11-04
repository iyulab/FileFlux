using FileFlux.Core.Infrastructure.Readers;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Readers;

/// <summary>
/// A reader that opened a non-empty file and found nothing used to hand back an empty string and no explanation — only the PDF
/// reader classified it (<c>extraction_failure_reason</c>). <see cref="EmptyExtraction.Note"/> is the general fallback applied
/// where every extraction passes, so a pipeline caller (FluxFeed reads the hint) can tell "read but empty" from "short".
/// </summary>
public sealed class EmptyExtractionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fileflux-empty-" + Guid.NewGuid().ToString("N"));

    public EmptyExtractionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Note_MarksANonEmptyFileThatHeldNothing()
    {
        var raw = new RawContent { Text = "  \n", File = new SourceFileInfo { Size = 1616 } };

        EmptyExtraction.Note(raw);

        raw.Hints[EmptyExtraction.HintKey].Should().Be(EmptyExtraction.Reason);
        raw.Warnings.Should().ContainSingle(w => w.Contains("1616 bytes") && w.Contains("[empty_extraction]"));
    }

    [Fact]
    public void Note_UsesTheCallersLength_WhenTheReaderRecordedNoSize()
    {
        var raw = new RawContent { Text = "" };

        EmptyExtraction.Note(raw, sourceLength: 40);

        raw.Hints[EmptyExtraction.HintKey].Should().Be("empty_extraction");
    }

    [Fact]
    public void Note_LeavesAReadersOwnAnswerAlone()
    {
        var raw = new RawContent { Text = "", File = new SourceFileInfo { Size = 9000 } };
        raw.Hints[EmptyExtraction.HintKey] = "no_text_layer";

        EmptyExtraction.Note(raw);

        raw.Hints[EmptyExtraction.HintKey].Should().Be("no_text_layer");
        raw.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Note_MarksNothing_ForAnEmptySource_ForAnUnknownSize_AndWhenThereIsContent()
    {
        var empty = new RawContent { Text = "", File = new SourceFileInfo { Size = 0 } };
        EmptyExtraction.Note(empty, sourceLength: 0);
        empty.Hints.Should().BeEmpty();

        var unknown = new RawContent { Text = "" };
        EmptyExtraction.Note(unknown);
        unknown.Hints.Should().BeEmpty();

        var withText = new RawContent { Text = "Name  Qty", File = new SourceFileInfo { Size = 72 } };
        EmptyExtraction.Note(withText);
        withText.Hints.Should().BeEmpty();

        var withImage = new RawContent { Text = "", File = new SourceFileInfo { Size = 5000 }, Images = [new ImageInfo()] };
        EmptyExtraction.Note(withImage);
        withImage.Hints.Should().BeEmpty("an image-only document is content for an image describer, not an empty read");
    }

    [Fact]
    public async Task ExtractAsync_MarksAFileThatHasContentButNoText_AndNotOneThatReadsNormally()
    {
        var processor = new ServiceCollection().AddFileFlux().BuildServiceProvider().GetRequiredService<FluxDocumentProcessor>();

        var blank = Path.Combine(_root, "blank.txt");
        await File.WriteAllTextAsync(blank, "   \n\t \n   ", Ct);
        var clean = Path.Combine(_root, "clean.md");
        await File.WriteAllTextAsync(clean, "# Title\n\nFirst paragraph about wind turbines.\n", Ct);

        var emptyRaw = await processor.ExtractAsync(blank, Ct);
        var cleanRaw = await processor.ExtractAsync(clean, Ct);

        emptyRaw.Hints[EmptyExtraction.HintKey].Should().Be(EmptyExtraction.Reason);
        emptyRaw.Warnings.Should().Contain(w => w.Contains("[empty_extraction]"));
        cleanRaw.Hints.ContainsKey(EmptyExtraction.HintKey).Should().BeFalse();
    }
}
