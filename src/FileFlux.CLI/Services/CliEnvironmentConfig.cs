namespace FileFlux.CLI.Services;

/// <summary>
/// Environment variable configuration for CLI
/// Supports multiple naming conventions with fallbacks
/// Priority: Environment Variable > Config File > Default
/// </summary>
public class CliEnvironmentConfig
{
    private readonly ConfigManager _configManager;

    public CliEnvironmentConfig()
    {
        _configManager = new ConfigManager();
    }

    // Provider selection
    public string? Provider => GetValue("MODEL_PROVIDER");

    // OpenAI configuration
    public string? OpenAIApiKey => GetValue("OPENAI_API_KEY");

    public string? OpenAIModel => GetValue("OPENAI_MODEL") ?? "gpt-5-nano";

    // Anthropic configuration
    public string? AnthropicApiKey => GetValue("ANTHROPIC_API_KEY");

    public string? AnthropicModel => GetValue("ANTHROPIC_MODEL") ?? "claude-3-haiku-20240307";

    // GPU-Stack configuration
    public string? GpuStackApiKey => GetValue("GPUSTACK_API_KEY");

    public string? GpuStackEndpoint => GetValue("GPUSTACK_ENDPOINT") ?? "http://localhost:8080";

    public string? GpuStackModel => GetValue("GPUSTACK_MODEL");

    // Google Gemini configuration
    public string? GoogleApiKey => GetValue("GOOGLE_API_KEY") ?? GetValue("GEMINI_API_KEY");

    public string? GoogleModel => GetValue("GOOGLE_MODEL") ?? GetValue("GEMINI_MODEL") ?? "gemini-2.5-flash";

    /// <summary>
    /// Get value with priority: Environment Variable > Config File
    /// </summary>
    private string? GetValue(string key)
    {
        // First check environment variable
        var envValue = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(envValue))
            return envValue;

        // Then check config file
        return _configManager.Get(key);
    }

    /// <summary>
    /// Auto-detect provider from available credentials
    /// </summary>
    public string DetectProvider()
    {
        // If explicitly set, use it
        if (!string.IsNullOrWhiteSpace(Provider))
        {
            var provider = Provider.ToLowerInvariant();
            if (provider == "openai" || provider == "anthropic" || provider == "gpustack" || provider == "google" || provider == "gemini")
            {
                return provider == "gemini" ? "google" : provider;
            }
        }

        // Count configured API keys
        var configuredProviders = GetConfiguredProviders();

        // If multiple providers configured, require explicit selection
        if (configuredProviders.Count > 1)
        {
            return "ambiguous";
        }

        // Auto-detect from single API key
        if (configuredProviders.Count == 1)
        {
            return configuredProviders[0];
        }

        return "none";
    }

    /// <summary>
    /// Get list of configured providers (those with API keys)
    /// </summary>
    public List<string> GetConfiguredProviders()
    {
        var providers = new List<string>();

        if (!string.IsNullOrWhiteSpace(OpenAIApiKey))
            providers.Add("openai");

        if (!string.IsNullOrWhiteSpace(AnthropicApiKey))
            providers.Add("anthropic");

        if (!string.IsNullOrWhiteSpace(GpuStackApiKey))
            providers.Add("gpustack");

        if (!string.IsNullOrWhiteSpace(GoogleApiKey))
            providers.Add("google");

        return providers;
    }

    /// <summary>
    /// Check if any AI provider is configured
    /// </summary>
    public bool HasAnyProvider()
    {
        return DetectProvider() != "none";
    }
}
