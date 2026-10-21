using FileFlux.Providers.LMSupply;
using LMSupply;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using FI = FluxImprover.Services;
using FluxImproverLMSupply = FluxImprover.LMSupply.LMSupplyCompletionService;

namespace FileFlux.CLI.Services.Providers.FluxImprover;

/// <summary>
/// FluxImprover's ITextGenerationService over a local LMSupply model, loaded on first use.
/// Generation is delegated to the FluxImprover.LMSupply adapter (chat template, thinking mode, truncation);
/// this wrapper only adds lazy loading with console progress, which a CLI needs and a library does not.
/// </summary>
public sealed class LMSupplyCompletionService : FI.ITextGenerationService, IAsyncDisposable
{
    private readonly LMSupplyOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IGeneratorModel? _model;
    private FluxImproverLMSupply? _inner;
    private bool _disposed;

    public LMSupplyCompletionService(LMSupplyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private async Task<FluxImproverLMSupply> GetInnerAsync(CancellationToken cancellationToken)
    {
        if (_inner != null) return _inner;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inner != null) return _inner;

            var generatorOptions = new GeneratorOptions
            {
                CacheDirectory = _options.CacheDirectory,
                Provider = _options.UseGpuAcceleration
                    // Auto lets LMSupply pick the provider for this host (CUDA / CoreML / CPU for ONNX sessions,
                    // Vulkan for llama-server on AMD/Intel GPUs). This used to pin DirectML, which ONNX Runtime
                    // 1.25+ no longer ships -- LMSupply 0.67.0 refuses it.
                    ? ExecutionProvider.Auto
                    : ExecutionProvider.Cpu
            };

            Console.WriteLine($"[LMSupply] Loading model: {_options.GeneratorModel}...");

            _model = await LocalGenerator.LoadAsync(
                _options.GeneratorModel,
                generatorOptions,
                new Progress<DownloadProgress>(p =>
                {
                    if (p.TotalBytes > 0)
                    {
                        var percent = (double)p.BytesDownloaded / p.TotalBytes * 100;
                        Console.Write($"\r[LMSupply] Downloading: {percent:F1}%    ");
                    }
                }),
                cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"\r[LMSupply] Model loaded: {_options.GeneratorModel}          ");

            // This wrapper owns the model and disposes it; the adapter is only handed it.
            _inner = new FluxImproverLMSupply(
                _model,
                NullLogger<FluxImproverLMSupply>.Instance,
                defaultTemperature: 0.7f,
                defaultMaxTokens: _options.MaxGenerationTokens);
            return _inner;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string> CompleteAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var inner = await GetInnerAsync(cancellationToken).ConfigureAwait(false);
        return await inner.CompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var inner = await GetInnerAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var token in inner.CompleteStreamingAsync(prompt, options, cancellationToken).ConfigureAwait(false))
        {
            yield return token;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_inner != null)
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
        }

        if (_model != null)
        {
            await _model.DisposeAsync().ConfigureAwait(false);
        }

        _initLock.Dispose();
    }
}
