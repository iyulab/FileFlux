using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 Excel 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// IImageRelevanceEvaluator가 제공된 경우 관련성 평가 후 선택적 포함
/// </summary>
public class MultiModalExcelDocumentReader : IDocumentReader
{
    private static readonly ImageDescriptionFormat s_format = new()
    {
        DocumentType = "Excel",
        ImageTypeHint = "chart",
        SectionMarker = "SPREADSHEET_IMAGES",
        Label = (_, number) => string.Create(CultureInfo.InvariantCulture, $"Spreadsheet Image {number}:"),
        ResultSubject = (_, imageType) => $"Spreadsheet: {imageType} image",
    };

    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly ExcelDocumentReader _baseExcelReader;

    public string ReaderType => "MultiModalExcelReader";

    public IEnumerable<string> SupportedExtensions => _baseExcelReader.SupportedExtensions;

    public MultiModalExcelDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService는 선택적 의존성
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        // IImageRelevanceEvaluator는 선택적 의존성
        _relevanceEvaluator = serviceProvider.GetService<IImageRelevanceEvaluator>();
        _baseExcelReader = new ExcelDocumentReader();
    }

    public bool CanRead(string fileName)
    {
        return _baseExcelReader.CanRead(fileName);
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _baseExcelReader.ReadAsync(filePath, cancellationToken);
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        return _baseExcelReader.ReadAsync(stream, fileName, cancellationToken);
    }

    // ========================================
    // Stage 1: Extract (Raw Content)
    // ========================================

    /// <summary>
    /// The document's text, with a description of each image the base reader extracted appended, when an
    /// <see cref="IImageToTextService"/> is registered. The extraction options (<see cref="ExtractOptions.ExtractImages"/>,
    /// <see cref="ExtractOptions.MaxImageSize"/>) decide which images there are; the stream path describes the same ones.
    /// </summary>
    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _baseExcelReader.ExtractAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(filePath), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ExtractAsync(string, ExtractOptions?, CancellationToken)"/>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _baseExcelReader.ExtractAsync(stream, fileName, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(fileName), cancellationToken).ConfigureAwait(false);
    }

    private Task<RawContent> DescribeImagesAsync(RawContent baseContent, string title, CancellationToken cancellationToken) =>
        ImageDescriptions.AppendAsync(baseContent, title, ReaderType, s_format, _imageToTextService, _relevanceEvaluator, cancellationToken);
}
