using System.Text.RegularExpressions;
using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Xunit;

namespace FileFlux.Tests.Readers;

/// <summary>
/// The Markdown an Office reader returns refers to a picture by the media part's file name (<c>![](image3.png)</c>)
/// while <see cref="ImageInfo.Id"/> is the resource id (<c>rId11</c>) a consumer stores the picture under. Nothing
/// connected the two names, so a viewer could not find the file a reference names. The image now carries the name the
/// text uses, and every reference in the text resolves to exactly one image.
/// </summary>
public partial class ImageReferenceNameTests
{
    private static readonly string Docx = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "test-docx", "demo.docx");

    [GeneratedRegex(@"!\[[^\]]*\]\(([^)\s]+)\)")]
    private static partial Regex ImageReference();

    [Fact]
    public async Task Word_EveryImageReferenceInTheText_NamesExactlyOneImage()
    {
        var content = await new WordDocumentReader().ExtractAsync(Docx, cancellationToken: TestContext.Current.CancellationToken);

        AssertEveryReferenceResolves(content);
    }

    [Fact]
    public async Task Word_TheStreamPath_ResolvesTheSameWay()
    {
        await using var stream = File.OpenRead(Docx);

        var content = await new WordDocumentReader().ExtractAsync(stream, "demo.docx", cancellationToken: TestContext.Current.CancellationToken);

        AssertEveryReferenceResolves(content);
    }

    [Fact]
    public async Task TheReferenceName_IsNotTheImageId()
    {
        var content = await new WordDocumentReader().ExtractAsync(Docx, cancellationToken: TestContext.Current.CancellationToken);

        // The two names are different namespaces; the value is what lets a consumer map one to the other.
        var image = Assert.Single(content.Images, i => i.Id == "rId10");
        Assert.Equal("image2.png", image.Properties[UndocImageResources.ReferenceNameProperty]);
    }

    private static void AssertEveryReferenceResolves(RawContent content)
    {
        var references = ImageReference().Matches(content.Text).Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(references);
        Assert.NotEmpty(content.Images);

        foreach (var reference in references)
        {
            var owners = content.Images
                .Where(i => i.Properties.TryGetValue(UndocImageResources.ReferenceNameProperty, out var name) && Equals(name, reference))
                .ToList();
            Assert.True(owners.Count == 1, $"the reference '{reference}' names {owners.Count} images; images: {string.Join(", ", content.Images.Select(i => i.Id))}");
        }
    }
}
