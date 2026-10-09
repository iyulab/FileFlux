using System.Globalization;
using FileFlux.Core;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Undoc;

namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Renders the slides <see cref="SlideRenderingOptions.SelectSlides"/> picks and adds each to the extracted images as a
/// PNG with its slide number (<see cref="ExtractOptions.SlideRendering"/>). Selection reads each slide's composition from
/// the package (<see cref="ReadCompositions"/>); rendering is Undoc's <c>RenderSection</c>.
/// </summary>
internal static class SlideRenderer
{
    private const string PresentationNs = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private const string RelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>
    /// Renders the selected slides of <paramref name="doc"/> into <paramref name="images"/>. <paramref name="openPackage"/>
    /// opens the document's bytes for the composition read; it is not called when nothing is asked for.
    /// </summary>
    public static void Render(
        UndocDocument doc,
        Func<Stream> openPackage,
        bool isLegacyPpt,
        ExtractOptions? options,
        List<ImageInfo> images,
        List<string> warnings,
        Dictionary<string, object> hints,
        CancellationToken cancellationToken)
    {
        var rendering = options?.SlideRendering;
        if (rendering?.SelectSlides is not { } select)
            return;

        if (options!.ExtractImages is false)
        {
            warnings.Add("SlideRendering was set but ExtractImages is false; no slide was rendered.");
            return;
        }

        if (isLegacyPpt)
        {
            warnings.Add("SlideRendering applies to .pptx; a legacy .ppt presentation was not rendered.");
            return;
        }

        IReadOnlyList<SlideComposition> slides;
        using (var package = openPackage())
            slides = ReadCompositions(package);

        if (slides.Count != doc.SectionCount)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"SlideRendering: the package lists {slides.Count} slide(s) but the presentation reads as {doc.SectionCount}; no slide was rendered."));
            return;
        }

        var renderOptions = new RenderSectionOptions
        {
            Dpi = rendering.Dpi,
            FontDirectories = rendering.FontDirectories ?? [],
            SystemFonts = rendering.SystemFonts,
        };

        var rendered = 0;
        foreach (var slide in slides.Where(select))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rendering.MaxSlides is { } max && rendered >= max)
            {
                warnings.Add(string.Create(CultureInfo.InvariantCulture,
                    $"SlideRendering: slide {slide.Slide} was selected but not rendered (MaxSlides {max})."));
                continue;
            }

            RenderedSection section;
            try
            {
                section = doc.RenderSection(slide.Slide - 1, renderOptions);
            }
            catch (UndocException ex)
            {
                warnings.Add(string.Create(CultureInfo.InvariantCulture, $"SlideRendering: slide {slide.Slide} could not be rendered: {ex.Message}"));
                continue;
            }

            images.Add(new ImageInfo
            {
                Id = string.Create(CultureInfo.InvariantCulture, $"slide-render-{slide.Slide}"),
                PageNumber = slide.Slide,
                MimeType = "image/png",
                Data = section.Png,
                OriginalSize = section.Png.Length,
                SourceUrl = string.Create(CultureInfo.InvariantCulture, $"rendered:slide/{slide.Slide}"),
                RenderedPage = new RenderedPage
                {
                    Dpi = rendering.Dpi,
                    Width = section.Width,
                    Height = section.Height,
                    UnpaintedShapes = (int)section.Gaps.Shapes,
                    UnpaintedImages = (int)section.Gaps.Images,
                    UnpaintedTextRuns = (int)section.Gaps.TextRuns,
                    UnpaintedCharts = (int)section.Gaps.Charts,
                    UnpaintedGraphicFrames = (int)section.Gaps.GraphicFrames,
                    ApproximatedFills = (int)section.Gaps.ApproximatedFills,
                    SubstitutedTextRuns = (int)section.SubstitutedTextRuns,
                },
            });
            rendered++;
        }

        hints["rendered_slides"] = rendered;
    }

    /// <summary>
    /// Each slide's composition in presentation order (the order of <c>p:sldIdLst</c>, which is the order Undoc numbers
    /// sections in — the slide part names are not).
    /// </summary>
    internal static IReadOnlyList<SlideComposition> ReadCompositions(Stream package)
    {
        using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var targets = ReadRelationshipTargets(zip, "ppt/_rels/presentation.xml.rels");

        var order = new List<string>();
        using (var reader = OpenXml(zip, "ppt/presentation.xml"))
        {
            while (reader?.Read() == true)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sldId" && reader.NamespaceURI == PresentationNs
                    && reader.GetAttribute("id", RelationshipsNs) is { } relId && targets.TryGetValue(relId, out var target))
                {
                    order.Add("ppt/" + target.TrimStart('/').Replace("ppt/", "", StringComparison.Ordinal));
                }
            }
        }

        var result = new List<SlideComposition>(order.Count);
        for (var i = 0; i < order.Count; i++)
            result.Add(Count(zip, order[i], i + 1));
        return result;
    }

    private static readonly XNamespace P = PresentationNs;

    private static SlideComposition Count(ZipArchive zip, string part, int slide)
    {
        using var reader = OpenXml(zip, part);
        if (reader is null)
            return new SlideComposition(slide, 0, 0, 0, 0, 0, 0);

        var root = XDocument.Load(reader).Root!;
        int placeholders = 0, textBoxes = 0, shapes = 0;
        foreach (var shape in root.Descendants(P + "sp"))
        {
            // The shape's own non-visual properties: p:nvSpPr/p:nvPr/p:ph marks a placeholder,
            // p:nvSpPr/p:cNvSpPr[@txBox="1"] a text box.
            var nonVisual = shape.Element(P + "nvSpPr");
            if (nonVisual?.Element(P + "nvPr")?.Element(P + "ph") is not null)
                placeholders++;
            else if ((string?)nonVisual?.Element(P + "cNvSpPr")?.Attribute("txBox") is "1" or "true")
                textBoxes++;
            else
                shapes++;
        }

        return new SlideComposition(
            slide, placeholders, textBoxes, shapes,
            Connectors: root.Descendants(P + "cxnSp").Count(),
            Pictures: root.Descendants(P + "pic").Count(),
            GraphicFrames: root.Descendants(P + "graphicFrame").Count());
    }

    private static Dictionary<string, string> ReadRelationshipTargets(ZipArchive zip, string part)
    {
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = OpenXml(zip, part);
        while (reader?.Read() == true)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship" && reader.NamespaceURI == PackageRelationshipsNs
                && reader.GetAttribute("Id") is { } id && reader.GetAttribute("Target") is { } target)
            {
                targets[id] = target;
            }
        }
        return targets;
    }

    private static XmlReader? OpenXml(ZipArchive zip, string part)
    {
        var entry = zip.GetEntry(part);
        return entry is null
            ? null
            : XmlReader.Create(entry.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, CloseInput = true });
    }
}
