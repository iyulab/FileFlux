using FileFlux.CLI.Services.LMSupply;
using LMSupply;
using LMSupply.Generator;
using LMSupply.Generator.Abstractions;
using LMSupply.Generator.Models;
using System.Runtime.CompilerServices;
using FI = FluxImprover.Services;

namespace FileFlux.CLI.Services.Providers.FluxImprover;

/// <summary>
/// LMSupply implementation of FluxImprover's ITextCompletionService
/// Uses local AI models via LMSupply.Generator
/// </summary>
public class LMSupplyCompletionService : FI.ITextCompletionService, IAsyncDisposable
{
    private readonly LMSupplyOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IGeneratorModel? _model;
    private bool _disposed;

    public LMSupplyCompletionService(LMSupplyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    private async Task<IGeneratorModel> GetModelAsync(CancellationToken cancellationToken = default)
    {
        if (_model != null) return _model;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_model != null) return _model;

            var generatorOptions = new GeneratorOptions
            {
                CacheDirectory = _options.CacheDirectory,
                Provider = _options.UseGpuAcceleration
                    ? ExecutionProvider.DirectML
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

            return _model;
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

        var model = await GetModelAsync(cancellationToken).ConfigureAwait(false);

        // Build full prompt with system message if provided
        var fullPrompt = BuildPrompt(prompt, options);

        var generationOptions = new GenerationOptions
        {
            MaxTokens = options?.MaxTokens ?? _options.MaxGenerationTokens,
            Temperature = options?.Temperature ?? 0.7f
        };

        return await model.GenerateCompleteAsync(fullPrompt, generationOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        FI.CompletionOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(prompt);

        var model = await GetModelAsync(cancellationToken).ConfigureAwait(false);

        var fullPrompt = BuildPrompt(prompt, options);

        var generationOptions = new GenerationOptions
        {
            MaxTokens = options?.MaxTokens ?? _options.MaxGenerationTokens,
            Temperature = options?.Temperature ?? 0.7f
        };

        await foreach (var token in model.GenerateAsync(fullPrompt, generationOptions, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return token;
        }
    }

    private static string BuildPrompt(string prompt, FI.CompletionOptions? options)
    {
        var parts = new List<string>();

        // Add system prompt if provided
        if (!string.IsNullOrWhiteSpace(options?.SystemPrompt))
        {
            parts.Add($"System: {options.SystemPrompt}\n");
        }

        // Add conversation history if provided
        if (options?.Messages is { Count: > 0 })
        {
            foreach (var msg in options.Messages)
            {
                var role = msg.Role?.ToLowerInvariant() switch
                {
                    "system" => "System",
                    "assistant" => "Assistant",
                    _ => "User"
                };
                parts.Add($"{role}: {msg.Content}");
            }
        }

        parts.Add($"User: {prompt}\nAssistant:");

        return string.Join("\n", parts);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);

        if (_model != null)
        {
            await _model.DisposeAsync().ConfigureAwait(false);
        }

        _initLock.Dispose();
    }
}
