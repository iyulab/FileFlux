using System.Text.Json;

namespace FileFlux.CLI.Services;

/// <summary>
/// Manages FileFlux CLI configuration stored in local app data
/// </summary>
public class ConfigManager
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FileFlux");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private readonly Dictionary<string, string> _config;

    // Valid configuration keys
    public static readonly Dictionary<string, string> ValidKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MODEL_PROVIDER"] = "AI provider (openai, anthropic, gpustack)",
        ["OPENAI_API_KEY"] = "OpenAI API key",
        ["OPENAI_MODEL"] = "OpenAI model name",
        ["ANTHROPIC_API_KEY"] = "Anthropic API key",
        ["ANTHROPIC_MODEL"] = "Anthropic model name",
        ["GPUSTACK_API_KEY"] = "GPU-Stack API key",
        ["GPUSTACK_ENDPOINT"] = "GPU-Stack endpoint URL",
        ["GPUSTACK_MODEL"] = "GPU-Stack model name"
    };

    public ConfigManager()
    {
        _config = Load();
    }

    /// <summary>
    /// Get a configuration value
    /// </summary>
    public string? Get(string key)
    {
        var normalizedKey = NormalizeKey(key);
        return _config.TryGetValue(normalizedKey, out var value) ? value : null;
    }

    /// <summary>
    /// Set a configuration value
    /// </summary>
    public void Set(string key, string value)
    {
        var normalizedKey = NormalizeKey(key);
        _config[normalizedKey] = value;
        Save();
    }

    /// <summary>
    /// Remove a configuration value
    /// </summary>
    public bool Unset(string key)
    {
        var normalizedKey = NormalizeKey(key);
        var removed = _config.Remove(normalizedKey);
        if (removed) Save();
        return removed;
    }

    /// <summary>
    /// Get all configuration values
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAll() => _config;

    /// <summary>
    /// Check if a key is valid
    /// </summary>
    public static bool IsValidKey(string key)
    {
        return ValidKeys.ContainsKey(NormalizeKey(key));
    }

    /// <summary>
    /// Get config file path
    /// </summary>
    public static string GetConfigPath() => ConfigPath;

    private static string NormalizeKey(string key)
    {
        return key.ToUpperInvariant().Replace("-", "_");
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Ignore errors, return empty config
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(_config, s_jsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }
}
