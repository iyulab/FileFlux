namespace FileFlux.CLI.Services;

/// <summary>
/// Environment variable configuration for CLI
/// Supports multiple naming conventions with fallbacks
/// </summary>
public class CliEnvironmentConfig
{
    // Provider selection
    public string? Provider => GetEnv("FILEFLUX_PROVIDER") ?? GetEnv("PROVIDER");

    // OpenAI configuration
    public string? OpenAIApiKey => GetEnv("FILEFLUX_OPENAI_API_KEY")
        ?? GetEnv("OPENAI_API_KEY")
        ?? GetEnv("API_KEY");

    public string? OpenAIModel => GetEnv("FILEFLUX_OPENAI_MODEL")
        ?? GetEnv("OPENAI_MODEL")
        ?? GetEnv("MODEL")
        ?? "gpt-5-nano";

    // Anthropic configuration
    public string? AnthropicApiKey => GetEnv("FILEFLUX_ANTHROPIC_API_KEY")
        ?? GetEnv("ANTHROPIC_API_KEY")
        ?? GetEnv("API_KEY");

    public string? AnthropicModel => GetEnv("FILEFLUX_ANTHROPIC_MODEL")
        ?? GetEnv("ANTHROPIC_MODEL")
        ?? GetEnv("MODEL")
        ?? "claude-3-haiku-20240307";

    private static string? GetEnv(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Auto-detect provider from available credentials
    /// </summary>
    public string DetectProvider()
    {
        if (!string.IsNullOrWhiteSpace(Provider))
        {
            var provider = Provider.ToLowerInvariant();
            // Only support OpenAI for now
            if (provider == "openai")
            {
                return provider;
            }
        }

        if (!string.IsNullOrWhiteSpace(OpenAIApiKey))
        {
            return "openai";
        }

        return "none";
    }

    /// <summary>
    /// Check if any AI provider is configured
    /// </summary>
    public bool HasAnyProvider()
    {
        return DetectProvider() != "none";
    }
}
