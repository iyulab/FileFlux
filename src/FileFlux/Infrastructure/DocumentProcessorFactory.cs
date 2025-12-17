using FileFlux.Core;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Core;
using FluxCurator.Core.Core;
using FluxCurator.Infrastructure.Chunking;
using FluxImprover;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure;

/// <summary>
/// Factory for creating stateful document processors.
/// </summary>
public class DocumentProcessorFactory : IDocumentProcessorFactory
{
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IChunkerFactory _chunkerFactory;
    private readonly IDocumentRefiner? _documentRefiner;
    private readonly FluxImproverServices? _improverServices;
    private readonly IMarkdownConverter? _markdownConverter;
    private readonly IImageToTextService? _imageToTextService;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Create factory with required dependencies.
    /// </summary>
    public DocumentProcessorFactory(
        IDocumentReaderFactory readerFactory,
        IChunkerFactory chunkerFactory,
        FluxImproverServices? improverServices = null,
        IMarkdownConverter? markdownConverter = null,
        IImageToTextService? imageToTextService = null,
        ILoggerFactory? loggerFactory = null)
        : this(readerFactory, chunkerFactory, null, improverServices, markdownConverter, imageToTextService, loggerFactory)
    {
    }

    /// <summary>
    /// Create factory with all dependencies including document refiner.
    /// </summary>
    public DocumentProcessorFactory(
        IDocumentReaderFactory readerFactory,
        IChunkerFactory chunkerFactory,
        IDocumentRefiner? documentRefiner,
        FluxImproverServices? improverServices = null,
        IMarkdownConverter? markdownConverter = null,
        IImageToTextService? imageToTextService = null,
        ILoggerFactory? loggerFactory = null)
    {
        _readerFactory = readerFactory ?? throw new ArgumentNullException(nameof(readerFactory));
        _chunkerFactory = chunkerFactory ?? throw new ArgumentNullException(nameof(chunkerFactory));
        _documentRefiner = documentRefiner;
        _improverServices = improverServices;
        _markdownConverter = markdownConverter;
        _imageToTextService = imageToTextService;
        _loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
    }

    /// <inheritdoc/>
    public IDocumentProcessor Create(string filePath)
    {
        return new StatefulDocumentProcessor(
            filePath,
            _readerFactory,
            _chunkerFactory,
            _documentRefiner,
            _improverServices,
            _markdownConverter,
            _imageToTextService,
            _loggerFactory.CreateLogger<StatefulDocumentProcessor>());
    }

    /// <inheritdoc/>
    public IDocumentProcessor Create(Stream stream, string extension)
    {
        return new StatefulDocumentProcessor(
            stream,
            extension,
            _readerFactory,
            _chunkerFactory,
            _documentRefiner,
            _improverServices,
            _markdownConverter,
            _imageToTextService,
            _loggerFactory.CreateLogger<StatefulDocumentProcessor>());
    }

    /// <inheritdoc/>
    public IDocumentProcessor Create(byte[] content, string extension, string? fileName = null)
    {
        return new StatefulDocumentProcessor(
            content,
            extension,
            fileName,
            _readerFactory,
            _chunkerFactory,
            _documentRefiner,
            _improverServices,
            _markdownConverter,
            _imageToTextService,
            _loggerFactory.CreateLogger<StatefulDocumentProcessor>());
    }
}

/// <summary>
/// Builder for creating DocumentProcessorFactory with fluent API.
/// </summary>
public class DocumentProcessorFactoryBuilder
{
    private IDocumentReaderFactory? _readerFactory;
    private IChunkerFactory? _chunkerFactory;
    private IDocumentRefiner? _documentRefiner;
    private FluxImproverServices? _improverServices;
    private IMarkdownConverter? _markdownConverter;
    private IImageToTextService? _imageToTextService;
    private ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Set the document reader factory.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithReaderFactory(IDocumentReaderFactory factory)
    {
        _readerFactory = factory;
        return this;
    }

    /// <summary>
    /// Set the chunker factory.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithChunkerFactory(IChunkerFactory factory)
    {
        _chunkerFactory = factory;
        return this;
    }

    /// <summary>
    /// Set the document refiner.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithDocumentRefiner(IDocumentRefiner refiner)
    {
        _documentRefiner = refiner;
        return this;
    }

    /// <summary>
    /// Set FluxImprover services for LLM enrichment.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithImproverServices(FluxImproverServices services)
    {
        _improverServices = services;
        return this;
    }

    /// <summary>
    /// Set markdown converter.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithMarkdownConverter(IMarkdownConverter converter)
    {
        _markdownConverter = converter;
        return this;
    }

    /// <summary>
    /// Set image to text service.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithImageToTextService(IImageToTextService service)
    {
        _imageToTextService = service;
        return this;
    }

    /// <summary>
    /// Set logger factory.
    /// </summary>
    public DocumentProcessorFactoryBuilder WithLoggerFactory(ILoggerFactory factory)
    {
        _loggerFactory = factory;
        return this;
    }

    /// <summary>
    /// Build the factory.
    /// </summary>
    public DocumentProcessorFactory Build()
    {
        if (_readerFactory == null)
            throw new InvalidOperationException("ReaderFactory is required");
        if (_chunkerFactory == null)
            throw new InvalidOperationException("ChunkerFactory is required");

        return new DocumentProcessorFactory(
            _readerFactory,
            _chunkerFactory,
            _documentRefiner,
            _improverServices,
            _markdownConverter,
            _imageToTextService,
            _loggerFactory);
    }

    /// <summary>
    /// Create a builder with default FileFlux factories.
    /// </summary>
    public static DocumentProcessorFactoryBuilder CreateDefault()
    {
        return new DocumentProcessorFactoryBuilder()
            .WithReaderFactory(new DocumentReaderFactory())
            .WithChunkerFactory(new ChunkerFactory());
    }
}
