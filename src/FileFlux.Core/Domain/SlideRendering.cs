namespace FileFlux.Core;

/// <summary>
/// Which presentation slides to render as images, and how (<see cref="ExtractOptions.SlideRendering"/>). A slide drawn with
/// shapes, connectors, charts or SmartArt keeps its text through the reader but loses its picture — a flow or architecture
/// diagram becomes a list of labels. A rendered slide is added to <see cref="RawContent.Images"/> with its slide number, so
/// the image-to-text service that describes embedded pictures describes the slide as drawn.
/// </summary>
/// <remarks>
/// <c>.pptx</c> only — a legacy <c>.ppt</c> has no drawing to render and is reported in <see cref="RawContent.Warnings"/>.
/// Fonts are not bundled: text is drawn with fonts from <see cref="FontDirectories"/>, then the system's. A host without
/// fonts for a slide's script (a Linux container without CJK fonts) draws no text for it, and says so in
/// <see cref="RenderedPage.UnpaintedTextRuns"/>.
/// </remarks>
public sealed class SlideRenderingOptions
{
    /// <summary>Which slides to render, from each slide's composition. Null: none.</summary>
    public Func<SlideComposition, bool>? SelectSlides { get; set; }

    /// <summary>Render resolution in dots per inch. Default 150.</summary>
    public int Dpi { get; set; } = 150;

    /// <summary>The most slides one document renders; selected slides past it are named in the warnings. Null: no limit.</summary>
    public int? MaxSlides { get; set; }

    /// <summary>Directories searched for fonts before the system's. Null: none.</summary>
    public IReadOnlyList<string>? FontDirectories { get; set; }

    /// <summary>Whether the system's fonts are used after <see cref="FontDirectories"/>. Default true.</summary>
    public bool SystemFonts { get; set; } = true;

    /// <summary>Every slide.</summary>
    public static Func<SlideComposition, bool> AllSlides { get; } = _ => true;

    /// <summary>
    /// Slides that carry a drawing the text cannot: any connector, chart, table, SmartArt or other graphic frame, or at
    /// least three drawn shapes (shapes that are neither placeholders nor text boxes). A heuristic — pass your own
    /// predicate over <see cref="SlideComposition"/> when it does not fit your decks.
    /// </summary>
    public static Func<SlideComposition, bool> DrawnSlides { get; } =
        slide => slide.Connectors > 0 || slide.GraphicFrames > 0 || slide.Shapes >= 3;
}

/// <summary>
/// What a slide is drawn with, counted from the slide's own markup (elements inside groups included; the layout and
/// master are not counted).
/// </summary>
/// <param name="Slide">The slide number, 1-based in presentation order — the numbering of <see cref="ImageInfo.PageNumber"/>.</param>
/// <param name="Placeholders">Shapes that fill a layout placeholder (title, body, footer).</param>
/// <param name="TextBoxes">Shapes marked as text boxes.</param>
/// <param name="Shapes">Every other shape — rectangles, arrows, callouts: the parts of a drawing.</param>
/// <param name="Connectors">Connector lines between shapes.</param>
/// <param name="Pictures">Pictures.</param>
/// <param name="GraphicFrames">Charts, tables, SmartArt and embedded objects.</param>
public sealed record SlideComposition(
    int Slide, int Placeholders, int TextBoxes, int Shapes, int Connectors, int Pictures, int GraphicFrames);

/// <summary>
/// How an image that is a rendered page or slide was drawn (<see cref="ImageInfo.RenderedPage"/>): its size and what the
/// renderer could not paint. Every count is zero when the image shows everything the page asks for.
/// </summary>
public sealed record RenderedPage
{
    /// <summary>The resolution it was rendered at.</summary>
    public required int Dpi { get; init; }

    /// <summary>Width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Shapes not painted (custom geometry the renderer does not draw).</summary>
    public int UnpaintedShapes { get; init; }

    /// <summary>Pictures not painted (an image codec the renderer does not decode).</summary>
    public int UnpaintedImages { get; init; }

    /// <summary>Text runs not painted — no font on the host for their script.</summary>
    public int UnpaintedTextRuns { get; init; }

    /// <summary>Charts not painted.</summary>
    public int UnpaintedCharts { get; init; }

    /// <summary>Tables, SmartArt and other graphic frames not painted.</summary>
    public int UnpaintedGraphicFrames { get; init; }

    /// <summary>Gradient and pattern fills painted in one of their colours.</summary>
    public int ApproximatedFills { get; init; }

    /// <summary>Text runs painted in a substitute font (their own font was not found).</summary>
    public int SubstitutedTextRuns { get; init; }

    /// <summary>Nothing was left out (approximations and substitute fonts aside).</summary>
    public bool IsComplete =>
        UnpaintedShapes == 0 && UnpaintedImages == 0 && UnpaintedTextRuns == 0 && UnpaintedCharts == 0 && UnpaintedGraphicFrames == 0;
}
