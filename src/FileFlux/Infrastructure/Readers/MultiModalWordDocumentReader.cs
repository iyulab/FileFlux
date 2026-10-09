using FileFlux.Core;
using FileFlux.Core.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 기능이 통합된 멀티모달 Word 문서 리더
/// IImageToTextService가 제공된 경우 이미지에서 텍스트를 추출하여 enrichment
/// IImageRelevanceEvaluator가 제공된 경우 관련성 평가 후 선택적 포함
/// </summary>
public class MultiModalWordDocumentReader : IDocumentReader
{
    private static readonly ImageDescriptionFormat s_format = new()
    {
        DocumentType = "Word",
        ImageTypeHint = "document",
        SectionMarker = "DOCUMENT_IMAGES",
        Label = (_, number) => string.Create(CultureInfo.InvariantCulture, $"Document Image {number}:"),
        ResultSubject = (_, imageType) => $"Document: {imageType} image",
    };

    private readonly IImageToTextService? _imageToTextService;
    private readonly IImageRelevanceEvaluator? _relevanceEvaluator;
    private readonly WordDocumentReader _baseWordReader;

    public string ReaderType => "MultiModalWordReader";

    public IEnumerable<string> SupportedExtensions => _baseWordReader.SupportedExtensions;

    public MultiModalWordDocumentReader(IServiceProvider serviceProvider)
    {
        // IImageToTextService는 선택적 의존성
        _imageToTextService = serviceProvider.GetService<IImageToTextService>();
        // IImageRelevanceEvaluator는 선택적 의존성
        _relevanceEvaluator = serviceProvider.GetService<IImageRelevanceEvaluator>();
        _baseWordReader = new WordDocumentReader();
    }

    public bool CanRead(string fileName)
    {
        return _baseWordReader.CanRead(fileName);
    }

    // ========================================
    // Stage 0: Read (Document Structure)
    // ========================================

    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return _baseWordReader.ReadAsync(filePath, cancellationToken);
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        return _baseWordReader.ReadAsync(stream, fileName, cancellationToken);
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
        var baseContent = await _baseWordReader.ExtractAsync(filePath, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(filePath), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ExtractAsync(string, ExtractOptions?, CancellationToken)"/>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var baseContent = await _baseWordReader.ExtractAsync(stream, fileName, options, cancellationToken).ConfigureAwait(false);
        return await DescribeImagesAsync(baseContent, Path.GetFileNameWithoutExtension(fileName), cancellationToken).ConfigureAwait(false);
    }

    private Task<RawContent> DescribeImagesAsync(RawContent baseContent, string title, CancellationToken cancellationToken) =>
        ImageDescriptions.AppendAsync(baseContent, title, ReaderType, s_format, _imageToTextService, _relevanceEvaluator, cancellationToken);
}
