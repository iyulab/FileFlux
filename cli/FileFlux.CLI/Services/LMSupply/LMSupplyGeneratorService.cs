using FileFlux.Core;
using FileFlux.Domain;
using LMSupply;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;

namespace FileFlux.CLI.Services.LMSupply;

/// <summary>
/// IDocumentAnalysisService implementation using LMSupply.Generator.
/// </summary>
public sealed class LMSupplyGeneratorService : IDocumentAnalysisService, IAsyncDisposable
{
    private readonly IGeneratorModel _model;
    private readonly LMSupplyOptions _options;
    private bool _disposed;

    /// <summary>
    /// Creates a new instance of LMSupplyGeneratorService with the specified model.
    /// </summary>
    /// <param name="model">The loaded generator model.</param>
    /// <param name="options">Configuration options.</param>
    public LMSupplyGeneratorService(IGeneratorModel model, LMSupplyOptions? options = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _options = options ?? new LMSupplyOptions();
    }

    /// <summary>
    /// Creates a new LMSupplyGeneratorService with the default model.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="progress">Optional progress reporting for downloads.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new LMSupplyGeneratorService instance.</returns>
    public static async Task<LMSupplyGeneratorService> CreateAsync(
        LMSupplyOptions? options = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LMSupplyOptions();

        var generatorOptions = new GeneratorOptions
        {
            CacheDirectory = options.CacheDirectory,
            Provider = options.UseGpuAcceleration
                ? ExecutionProvider.DirectML
                : ExecutionProvider.Cpu
        };

        var model = await LocalGenerator.LoadAsync(
            options.GeneratorModel,
            generatorOptions,
            progress,
            cancellationToken).ConfigureAwait(false);

        if (options.WarmupOnInit)
        {
            await model.WarmupAsync(cancellationToken).ConfigureAwait(false);
        }

        return new LMSupplyGeneratorService(model, options);
    }

    /// <inheritdoc />
    public DocumentAnalysisServiceInfo ProviderInfo => new()
    {
        Name = "LMSupply Generator",
        Type = DocumentAnalysisProviderType.Local,
        SupportedModels = [_model.ModelId],
        MaxContextLength = _model.MaxContextLength,
        InputTokenCost = 0,
        OutputTokenCost = 0,
        ApiVersion = "1.0"
    };

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await Task.FromResult(true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var options = new GenerationOptions
        {
            MaxTokens = _options.MaxGenerationTokens,
            Temperature = 0.7f
        };

        return await _model.GenerateCompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var options = new GenerationOptions
        {
            MaxTokens = _options.MaxGenerationTokens,
            Temperature = 0.3f
        };

        var response = await _model.GenerateCompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);

        return new StructureAnalysisResult
        {
            DocumentType = documentType,
            Sections = [],
            Structure = new CoreDocumentStructure(),
            Confidence = 0.7,
            RawResponse = response,
            TokensUsed = EstimateTokens(prompt) + EstimateTokens(response)
        };
    }

    /// <inheritdoc />
    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var options = new GenerationOptions
        {
            MaxTokens = Math.Min(maxLength * 2, _options.MaxGenerationTokens),
            Temperature = 0.5f
        };

        var response = await _model.GenerateCompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);

        return new ContentSummary
        {
            Summary = response.Trim(),
            Keywords = ExtractKeywordsFromText(response),
            Confidence = 0.7,
            OriginalLength = prompt.Length,
            TokensUsed = EstimateTokens(prompt) + EstimateTokens(response)
        };
    }

    /// <inheritdoc />
    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var options = new GenerationOptions
        {
            MaxTokens = _options.MaxGenerationTokens,
            Temperature = 0.3f
        };

        var response = await _model.GenerateCompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);

        return new MetadataExtractionResult
        {
            Keywords = ExtractKeywordsFromText(response),
            Language = "en",
            Categories = [],
            Entities = new Dictionary<string, string[]>(),
            TechnicalMetadata = new Dictionary<string, string>
            {
                ["documentType"] = documentType.ToString()
            },
            Confidence = 0.7,
            TokensUsed = EstimateTokens(prompt) + EstimateTokens(response)
        };
    }

    /// <inheritdoc />
    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var options = new GenerationOptions
        {
            MaxTokens = _options.MaxGenerationTokens,
            Temperature = 0.3f
        };

        var response = await _model.GenerateCompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);

        return new QualityAssessment
        {
            ConfidenceScore = 0.7,
            CompletenessScore = 0.7,
            ConsistencyScore = 0.7,
            Recommendations = [],
            Explanation = response.Trim(),
            TokensUsed = EstimateTokens(prompt) + EstimateTokens(response)
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _model.DisposeAsync().ConfigureAwait(false);
    }

    private static int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token for English
        return text.Length / 4;
    }

    private static string[] ExtractKeywordsFromText(string text)
    {
        // Simple keyword extraction based on word frequency
        var words = text.Split([' ', '\n', '\r', '\t', ',', '.', ';', ':', '!', '?'],
            StringSplitOptions.RemoveEmptyEntries);

        return words
            .Where(w => w.Length > 3)
            .GroupBy(w => w.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToArray();
    }
}
