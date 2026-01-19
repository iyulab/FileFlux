using FileFlux.CLI.Services.LMSupply;
using FileFlux.CLI.Services.Providers;
using FileFlux.Core;
using FluxImprover;
using LMSupply;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using FluxImproverService = FluxImprover.Services.ITextCompletionService;

namespace FileFlux.CLI.Services;

/// <summary>
/// Model information for display purposes
/// </summary>
public sealed record ModelInfo
{
    public required string Provider { get; init; }
    public required string TextModel { get; init; }
    public string? VisionModel { get; init; }
    public string? Endpoint { get; init; }
    public bool IsLocal { get; init; }
    public bool VisionEnabled { get; init; }
    public Dictionary<string, string> AdditionalInfo { get; init; } = new();
}

/// <summary>
/// Result of creating FluxImprover services, including the disposable completion service
/// </summary>
public sealed class FluxImproverResult : IAsyncDisposable
{
    public FluxImproverServices Services { get; }

    /// <summary>
    /// Maximum tokens the model can handle for enrichment.
    /// Use this to adjust chunk size during chunking stage when model limit is smaller than configured chunk size.
    /// For local models (LMSupply), this is the actual model context length.
    /// For cloud models, this is a conservative default (typically unlimited in practice).
    /// </summary>
    public int MaxEnrichmentTokens { get; }

    /// <summary>
    /// Whether this is a local model with known context limitations.
    /// When true, MaxEnrichmentTokens should be respected during chunking.
    /// </summary>
    public bool IsLocalModel { get; }

    private readonly IAsyncDisposable? _disposable;

    public FluxImproverResult(FluxImproverServices services, IAsyncDisposable? disposable, int maxEnrichmentTokens = 4096, bool isLocalModel = false)
    {
        Services = services;
        _disposable = disposable;
        MaxEnrichmentTokens = maxEnrichmentTokens;
        IsLocalModel = isLocalModel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposable != null)
        {
            await _disposable.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Factory for creating AI provider services based on environment configuration
/// </summary>
public class AIProviderFactory
{
    private readonly CliEnvironmentConfig _config;
    private readonly bool _enableVision;
    private readonly bool _verbose;

    public AIProviderFactory(CliEnvironmentConfig config, bool enableVision = false, bool verbose = false)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _enableVision = enableVision;
        _verbose = verbose;
    }

    /// <summary>
    /// Configure services with detected AI provider
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        var provider = _config.DetectProvider();

        switch (provider)
        {
            case "openai":
                ConfigureOpenAI(services);
                break;

            case "anthropic":
                ConfigureAnthropic(services);
                break;

            case "gpustack":
                ConfigureGpuStack(services);
                break;

            case "google":
                ConfigureGoogle(services);
                break;

            case "local":
                ConfigureLMSupply(services);
                break;

            case "none":
                // No AI provider configured - FileFlux will work without AI features
                break;

            case "ambiguous":
                var providers = _config.GetConfiguredProviders();
                throw new InvalidOperationException(
                    $"Multiple API keys configured ({string.Join(", ", providers)}). " +
                    "Set MODEL_PROVIDER environment variable to select one.");

            default:
                throw new InvalidOperationException($"Provider '{provider}' is not supported. Supported: openai, anthropic, gpustack, google.");
        }
    }

    /// <summary>
    /// Get provider status for display
    /// </summary>
    public string GetProviderStatus()
    {
        var provider = _config.DetectProvider();
        var visionStatus = _enableVision ? " + Vision" : "";

        return provider switch
        {
            "openai" => $"OpenAI ({_config.OpenAIModel}){visionStatus}",
            "anthropic" => $"Anthropic ({_config.AnthropicModel}){visionStatus}",
            "gpustack" => $"GPU-Stack ({_config.GpuStackModel ?? "default"}){visionStatus}",
            "google" => $"Google Gemini ({_config.GoogleModel}){visionStatus}",
            "local" => $"LMSupply Local ({_config.LMSupplyModel}){visionStatus}",
            "none" => "No AI provider (basic processing only)",
            "ambiguous" => $"Ambiguous (set MODEL_PROVIDER: {string.Join(", ", _config.GetConfiguredProviders())})",
            _ => $"Unsupported: {provider}"
        };
    }

    /// <summary>
    /// Get detailed model information for display
    /// </summary>
    public ModelInfo? GetDetailedModelInfo()
    {
        var provider = _config.DetectProvider();

        return provider switch
        {
            "openai" => new ModelInfo
            {
                Provider = "OpenAI",
                TextModel = _config.OpenAIModel ?? "gpt-5-nano",
                VisionModel = _enableVision ? _config.OpenAIModel ?? "gpt-5-nano" : null,
                VisionEnabled = _enableVision,
                IsLocal = false,
                AdditionalInfo = new()
                {
                    ["API"] = "https://api.openai.com/v1"
                }
            },
            "anthropic" => new ModelInfo
            {
                Provider = "Anthropic",
                TextModel = _config.AnthropicModel ?? "claude-3-haiku-20240307",
                VisionModel = _enableVision ? _config.AnthropicModel ?? "claude-3-haiku-20240307" : null,
                VisionEnabled = _enableVision,
                IsLocal = false,
                AdditionalInfo = new()
                {
                    ["API"] = "https://api.anthropic.com/v1"
                }
            },
            "gpustack" => new ModelInfo
            {
                Provider = "GPU-Stack",
                TextModel = _config.GpuStackModel ?? "default",
                VisionModel = _enableVision ? _config.GpuStackModel : null,
                Endpoint = _config.GpuStackEndpoint ?? "http://localhost:8080",
                VisionEnabled = _enableVision,
                IsLocal = false,
                AdditionalInfo = new()
                {
                    ["API"] = _config.GpuStackEndpoint ?? "http://localhost:8080"
                }
            },
            "google" => new ModelInfo
            {
                Provider = "Google Gemini",
                TextModel = _config.GoogleModel ?? "gemini-2.0-flash",
                VisionModel = _enableVision ? _config.GoogleModel ?? "gemini-2.0-flash" : null,
                VisionEnabled = _enableVision,
                IsLocal = false,
                AdditionalInfo = new()
                {
                    ["API"] = "https://generativelanguage.googleapis.com/v1"
                }
            },
            "local" => new ModelInfo
            {
                Provider = "LMSupply (Local)",
                TextModel = _config.LMSupplyModel ?? "microsoft/Phi-4-mini-instruct-onnx",
                VisionModel = _enableVision ? "Xenova/vit-gpt2-image-captioning" : null,
                VisionEnabled = _enableVision,
                IsLocal = true,
                AdditionalInfo = new()
                {
                    ["Acceleration"] = _config.LMSupplyUseGpu ? "GPU" : "CPU",
                    ["Cache"] = GetLMSupplyCacheDirectory()
                }
            },
            _ => null
        };
    }

    /// <summary>
    /// Display model information to console
    /// </summary>
    public void DisplayModelInfo(bool quiet = false)
    {
        if (quiet) return;

        var modelInfo = GetDetailedModelInfo();
        if (modelInfo is null)
        {
            AnsiConsole.MarkupLine("[yellow]AI Provider:[/] Not configured");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn(new TableColumn("[bold]Property[/]").Width(20))
            .AddColumn(new TableColumn("[bold]Value[/]"));

        table.Title = new TableTitle($"[blue]AI Model Configuration[/]");

        // Provider info
        var providerIcon = modelInfo.IsLocal ? "🖥️" : "☁️";
        table.AddRow("Provider", $"{providerIcon} {modelInfo.Provider}");
        table.AddRow("Text Model", Markup.Escape(modelInfo.TextModel));

        if (modelInfo.VisionEnabled && modelInfo.VisionModel is not null)
        {
            table.AddRow("Vision Model", Markup.Escape(modelInfo.VisionModel));
        }

        if (modelInfo.Endpoint is not null)
        {
            table.AddRow("Endpoint", Markup.Escape(modelInfo.Endpoint));
        }

        // Additional info
        foreach (var (key, value) in modelInfo.AdditionalInfo)
        {
            table.AddRow(key, Markup.Escape(value));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string GetLMSupplyCacheDirectory()
    {
        var cacheDir = Environment.GetEnvironmentVariable("LMSUPPLY_CACHE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LMSupply");
        return cacheDir;
    }

    /// <summary>
    /// Check if AI features are available
    /// </summary>
    public bool HasAIProvider()
    {
        var provider = _config.DetectProvider();
        return provider != "none" && provider != "ambiguous";
    }

    /// <summary>
    /// Create FluxImprover services for chunk enrichment and QA generation.
    /// The returned FluxImproverResult implements IAsyncDisposable to properly clean up
    /// underlying AI resources (e.g., ONNX GenAI models).
    /// </summary>
    public FluxImproverResult? CreateFluxImproverServices()
    {
        var provider = _config.DetectProvider();
        FluxImproverService? completionService = provider switch
        {
            "openai" => CreateOpenAIFluxImproverService(),
            "anthropic" => CreateAnthropicFluxImproverService(),
            "gpustack" => CreateGpuStackFluxImproverService(),
            "google" => CreateGoogleFluxImproverService(),
            "local" => CreateLMSupplyFluxImproverService(),
            _ => null
        };

        if (completionService is null)
        {
            return null;
        }

        var services = new FluxImproverBuilder()
            .WithCompletionService(completionService)
            .Build();

        // Determine model's max enrichment tokens based on provider
        // Local models (LMSupply) have limited context; cloud models have large context
        var (maxEnrichmentTokens, isLocal) = provider switch
        {
            // Local models - small context (Phi-4-mini: 512 tokens typical)
            // Use conservative estimate for enrichment prompt overhead
            "local" => (GetLMSupplyMaxTokens(), true),

            // Cloud models - large context windows
            "openai" => (8192, false),      // GPT-4/5: 8K-128K
            "anthropic" => (8192, false),   // Claude: 100K+
            "google" => (8192, false),      // Gemini: 32K+
            "gpustack" => (4096, false),    // Variable, use safe default

            _ => (4096, false)
        };

        // Return wrapper that can dispose the completion service
        var disposable = completionService as IAsyncDisposable;
        return new FluxImproverResult(services, disposable, maxEnrichmentTokens, isLocal);
    }

    /// <summary>
    /// Get the effective max tokens for LMSupply models.
    /// Phi-4-mini has 512 token context, but we need room for the enrichment prompt.
    /// </summary>
    private int GetLMSupplyMaxTokens()
    {
        // Check for custom configuration via environment variable
        var customLimit = Environment.GetEnvironmentVariable("LMSUPPLY_MAX_TOKENS");
        if (!string.IsNullOrEmpty(customLimit) && int.TryParse(customLimit, out var custom))
        {
            return custom;
        }

        // Default: Phi-4-mini has ~512 context, leave room for prompt
        // Enrichment prompt is ~100-150 tokens, so effective content limit is ~350-400
        return 400;
    }

    private FluxImproverService CreateOpenAIFluxImproverService()
    {
        var apiKey = _config.OpenAIApiKey ?? throw new InvalidOperationException("OpenAI API key not configured");
        var model = _config.OpenAIModel ?? "gpt-5-nano";
        return new Providers.FluxImprover.OpenAICompletionService(apiKey, model);
    }

    private FluxImproverService CreateAnthropicFluxImproverService()
    {
        var apiKey = _config.AnthropicApiKey ?? throw new InvalidOperationException("Anthropic API key not configured");
        var model = _config.AnthropicModel ?? "claude-3-5-sonnet-20241022";
        return new Providers.FluxImprover.AnthropicCompletionService(apiKey, model);
    }

    private FluxImproverService CreateGpuStackFluxImproverService()
    {
        var apiKey = _config.GpuStackApiKey ?? throw new InvalidOperationException("GPU-Stack API key not configured");
        var model = _config.GpuStackModel ?? throw new InvalidOperationException("GPU-Stack model not configured");
        var endpoint = _config.GpuStackEndpoint ?? "http://localhost:8080";
        return new Providers.FluxImprover.OpenAICompletionService(apiKey, model, endpoint);
    }

    private FluxImproverService CreateGoogleFluxImproverService()
    {
        var apiKey = _config.GoogleApiKey ?? throw new InvalidOperationException("Google API key not configured");
        var model = _config.GoogleModel ?? "gemini-2.0-flash";
        return new Providers.FluxImprover.GoogleCompletionService(apiKey, model);
    }

    private FluxImproverService CreateLMSupplyFluxImproverService()
    {
        var options = new LMSupplyOptions
        {
            GeneratorModel = _config.LMSupplyModel,
            UseGpuAcceleration = _config.LMSupplyUseGpu,
            MaxGenerationTokens = 2048
        };
        return new Providers.FluxImprover.LMSupplyCompletionService(options);
    }

    private void ConfigureOpenAI(IServiceCollection services)
    {
        var apiKey = _config.OpenAIApiKey;
        var model = _config.OpenAIModel ?? "gpt-5-nano";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OpenAI API key not found. Set OPENAI_API_KEY or FILEFLUX_OPENAI_API_KEY environment variable.");
        }

        // Register text completion service
        services.AddScoped<ITextCompletionService>(sp =>
            new OpenAITextCompletionService(apiKey, model));

        // Register image-to-text service if vision is enabled
        if (_enableVision)
        {
            services.AddScoped<IImageToTextService>(sp =>
                new OpenAIImageToTextService(apiKey, model, null, _verbose));
        }
    }

    private void ConfigureAnthropic(IServiceCollection services)
    {
        var apiKey = _config.AnthropicApiKey;
        var model = _config.AnthropicModel ?? "claude-3-5-sonnet-20241022";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Anthropic API key not found. Set ANTHROPIC_API_KEY environment variable.");
        }

        // Register text completion service
        services.AddScoped<ITextCompletionService>(sp =>
            new AnthropicTextCompletionService(apiKey, model));

        // Register image-to-text service if vision is enabled
        if (_enableVision)
        {
            services.AddScoped<IImageToTextService>(sp =>
                new AnthropicImageToTextService(apiKey, model));
        }
    }

    private void ConfigureGpuStack(IServiceCollection services)
    {
        var apiKey = _config.GpuStackApiKey;
        var endpoint = _config.GpuStackEndpoint ?? "http://localhost:8080";
        var model = _config.GpuStackModel;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "GPU-Stack API key not found. Set GPUSTACK_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                "GPU-Stack model not specified. Set GPUSTACK_MODEL environment variable.");
        }

        // GPU-Stack uses OpenAI-compatible API
        services.AddScoped<ITextCompletionService>(sp =>
            new OpenAITextCompletionService(apiKey, model, endpoint));

        // Register image-to-text service if vision is enabled
        if (_enableVision)
        {
            services.AddScoped<IImageToTextService>(sp =>
                new OpenAIImageToTextService(apiKey, model, endpoint, _verbose));
        }
    }

    private void ConfigureGoogle(IServiceCollection services)
    {
        var apiKey = _config.GoogleApiKey;
        var model = _config.GoogleModel ?? "gemini-2.0-flash";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Google API key not found. Set GOOGLE_API_KEY or GEMINI_API_KEY environment variable.");
        }

        // Register text completion service
        services.AddScoped<ITextCompletionService>(sp =>
            new GoogleTextCompletionService(apiKey, model));

        // Register image-to-text service if vision is enabled
        if (_enableVision)
        {
            services.AddScoped<IImageToTextService>(sp =>
                new GoogleImageToTextService(apiKey, model, _verbose));
        }
    }

    private void ConfigureLMSupply(IServiceCollection services)
    {
        var options = new LMSupplyOptions
        {
            GeneratorModel = _config.LMSupplyModel,
            UseGpuAcceleration = _config.LMSupplyUseGpu,
            MaxGenerationTokens = 2048,
            AutoSelectMultilingualModel = true
        };

        // Register LMSupply service factory as singleton for resource management
        services.AddSingleton(options);
        services.AddSingleton<LMSupplyServiceFactory>();

        // Create progress reporter for model downloads
        var progress = new ConsoleDownloadProgress();

        // Register text completion service - lazy initialization
        services.AddScoped<ITextCompletionService>(sp =>
        {
            var factory = sp.GetRequiredService<LMSupplyServiceFactory>();
            return factory.GetGeneratorAsync(progress).GetAwaiter().GetResult();
        });

        // Register image-to-text service if vision is enabled
        if (_enableVision)
        {
            services.AddScoped<IImageToTextService>(sp =>
            {
                var factory = sp.GetRequiredService<LMSupplyServiceFactory>();
                return factory.GetCaptionerAsync(progress).GetAwaiter().GetResult();
            });
        }
    }

    /// <summary>
    /// Progress reporter that displays download progress to the console
    /// </summary>
    private class ConsoleDownloadProgress : IProgress<DownloadProgress>
    {
        private string? _currentFile;
        private ProgressTask? _progressTask;
        private ProgressContext? _progressContext;
        private readonly object _lock = new();

        public void Report(DownloadProgress value)
        {
            lock (_lock)
            {
                // Check if we're starting a new file
                if (_currentFile != value.FileName)
                {
                    _currentFile = value.FileName;

                    // Print new file being downloaded
                    var totalMb = value.TotalBytes > 0 ? $" ({value.TotalBytes / 1024.0 / 1024.0:F1} MB)" : "";
                    AnsiConsole.MarkupLine($"[blue]Downloading:[/] {Markup.Escape(value.FileName)}{totalMb}");
                }

                // Show progress percentage if we know total size
                if (value.TotalBytes > 0)
                {
                    var percent = (double)value.BytesDownloaded / value.TotalBytes * 100;
                    var downloaded = value.BytesDownloaded / 1024.0 / 1024.0;
                    var total = value.TotalBytes / 1024.0 / 1024.0;

                    // Update progress on same line
                    Console.Write($"\r  Progress: {percent:F1}% ({downloaded:F1}/{total:F1} MB)");

                    if (value.BytesDownloaded >= value.TotalBytes)
                    {
                        Console.WriteLine(" [Done]");
                    }
                }
            }
        }
    }
}
