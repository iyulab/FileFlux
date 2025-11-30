using FileFlux.CLI.Services.Providers;
using FileFlux.Core;
using FileFlux;
using FluxImprover;
using Microsoft.Extensions.DependencyInjection;
using FluxImproverService = FluxImprover.Services.ITextCompletionService;

namespace FileFlux.CLI.Services;

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
            "none" => "No AI provider (basic processing only)",
            "ambiguous" => $"Ambiguous (set MODEL_PROVIDER: {string.Join(", ", _config.GetConfiguredProviders())})",
            _ => $"Unsupported: {provider}"
        };
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
    /// Create FluxImprover services for chunk enrichment and QA generation
    /// </summary>
    public FluxImproverServices? CreateFluxImproverServices()
    {
        var provider = _config.DetectProvider();
        FluxImproverService? completionService = provider switch
        {
            "openai" => CreateOpenAIFluxImproverService(),
            "anthropic" => CreateAnthropicFluxImproverService(),
            "gpustack" => CreateGpuStackFluxImproverService(),
            "google" => CreateGoogleFluxImproverService(),
            _ => null
        };

        if (completionService is null)
        {
            return null;
        }

        return new FluxImproverBuilder()
            .WithCompletionService(completionService)
            .Build();
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
        var model = _config.GoogleModel ?? "gemini-2.5-flash";
        return new Providers.FluxImprover.GoogleCompletionService(apiKey, model);
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
        var model = _config.GoogleModel ?? "gemini-2.5-flash";

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
}
