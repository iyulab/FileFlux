using FileFlux.CLI.Services.Providers;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.CLI.Services;

/// <summary>
/// Factory for creating AI provider services based on environment configuration
/// </summary>
public class AIProviderFactory
{
    private readonly CliEnvironmentConfig _config;

    public AIProviderFactory(CliEnvironmentConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
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

            case "none":
                // No AI provider configured - FileFlux will work without AI features
                break;

            default:
                throw new InvalidOperationException($"Provider '{provider}' is not supported. Currently only 'openai' is supported.");
        }
    }

    /// <summary>
    /// Get provider status for display
    /// </summary>
    public string GetProviderStatus()
    {
        var provider = _config.DetectProvider();

        return provider switch
        {
            "openai" => $"OpenAI ({_config.OpenAIModel})",
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

        services.AddScoped<ITextCompletionService>(sp =>
            new OpenAITextCompletionService(apiKey, model));
    }

}
