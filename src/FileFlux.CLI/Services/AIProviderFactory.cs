using FileFlux.CLI.Services.Providers;
using FileFlux.Core;
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.CLI.Services;

/// <summary>
/// Factory for creating AI provider services based on environment configuration
/// </summary>
public class AIProviderFactory
{
    private readonly CliEnvironmentConfig _config;
    private readonly bool _enableVision;

    public AIProviderFactory(CliEnvironmentConfig config, bool enableVision = false)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _enableVision = enableVision;
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

            case "none":
                // No AI provider configured - FileFlux will work without AI features
                break;

            default:
                throw new InvalidOperationException($"Provider '{provider}' is not supported. Currently 'openai' and 'anthropic' are supported.");
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
            "none" => "No AI provider (basic processing only)",
            _ => $"Unsupported: {provider}"
        };
    }

    /// <summary>
    /// Check if AI features are available
    /// </summary>
    public bool HasAIProvider()
    {
        return _config.DetectProvider() != "none";
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
                new OpenAIImageToTextService(apiKey, model));
        }
    }

    private void ConfigureAnthropic(IServiceCollection services)
    {
        var apiKey = _config.AnthropicApiKey;
        var model = _config.AnthropicModel ?? "claude-3-5-sonnet-20241022";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Anthropic API key not found. Set ANTHROPIC_API_KEY or FILEFLUX_ANTHROPIC_API_KEY environment variable.");
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

}
