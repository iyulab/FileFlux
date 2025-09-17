using Microsoft.Extensions.Configuration;

namespace TestConsoleApp;

/// <summary>
/// .env.local 파일에서 환경 변수를 로드하는 유틸리티 클래스
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// .env.local 파일에서 환경 변수를 로드
    /// </summary>
    public static void LoadFromFile(string? filePath = null)
    {
        // 기본 파일 경로 설정
        filePath ??= Path.Combine(FindProjectRoot(), ".env.local");

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"⚠️ Warning: .env.local file not found at {filePath}");
            return;
        }

        try
        {
            var lines = File.ReadAllLines(filePath);
            var loadedCount = 0;

            foreach (var line in lines)
            {
                // 빈 줄이나 주석 라인 건너뛰기
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    continue;

                var parts = line.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    // 따옴표 제거
                    if (value.StartsWith('"') && value.EndsWith('"'))
                    {
                        value = value[1..^1];
                    }

                    // 환경 변수가 이미 설정되어 있지 않으면 설정
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, value);
                        loadedCount++;
                    }
                }
            }

            Console.WriteLine($"✅ Loaded {loadedCount} environment variables from {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error loading .env.local file: {ex.Message}");
        }
    }

    /// <summary>
    /// 프로젝트 루트 디렉토리 찾기
    /// </summary>
    private static string FindProjectRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();

        // TestConsoleApp 디렉토리에서 상위로 이동하여 FileFlux 루트 찾기
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, ".env.local")))
        {
            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }

        return currentDir ?? Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// OpenAI 설정 검증
    /// </summary>
    public static bool ValidateOpenAIConfig()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        var embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL");

        var hasApiKey = !string.IsNullOrEmpty(apiKey);
        var hasModel = !string.IsNullOrEmpty(model);
        var hasEmbeddingModel = !string.IsNullOrEmpty(embeddingModel);

        Console.WriteLine("\n🔧 OpenAI Configuration Status:");
        Console.WriteLine($"  • API Key: {(hasApiKey ? "✅ Set" : "❌ Missing")}");
        Console.WriteLine($"  • Model: {(hasModel ? $"✅ {model}" : "❌ Missing")}");
        Console.WriteLine($"  • Embedding Model: {(hasEmbeddingModel ? $"✅ {embeddingModel}" : "❌ Missing")}");

        return hasApiKey && hasModel && hasEmbeddingModel;
    }
}