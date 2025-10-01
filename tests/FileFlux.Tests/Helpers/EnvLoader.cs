using System;
using System.IO;

namespace FileFlux.Tests.Helpers;

/// <summary>
/// Simple .env.local file loader for tests
/// </summary>
public static class EnvLoader
{
    private static bool _loaded = false;
    private static readonly object _lock = new();

    public static void Load()
    {
        lock (_lock)
        {
            if (_loaded) return;

            var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
            if (!File.Exists(envPath))
            {
                // Try parent directories (for when tests run from bin/Debug)
                var searchDir = Directory.GetCurrentDirectory();
                for (int i = 0; i < 5; i++)
                {
                    var parent = Directory.GetParent(searchDir);
                    if (parent == null) break;
                    
                    searchDir = parent.FullName;
                    envPath = Path.Combine(searchDir, ".env.local");
                    if (File.Exists(envPath)) break;
                }
            }

            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                        continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }
            }

            _loaded = true;
        }
    }

    public static bool IsOpenAiConfigured()
    {
        Load();
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        return !string.IsNullOrEmpty(apiKey) && apiKey.StartsWith("sk-");
    }

    public static string? GetOpenAiApiKey()
    {
        Load();
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }

    public static string GetOpenAiModel()
    {
        Load();
        return Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-5-nano";
    }
}