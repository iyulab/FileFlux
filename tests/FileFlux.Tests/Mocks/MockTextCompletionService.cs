using FileFlux;

namespace FileFlux.Tests.Mocks;

/// <summary>
/// 테스트용 Mock Text Completion Service
/// </summary>
public class MockTextCompletionService : IDocumentAnalysisService
{
    public DocumentAnalysisServiceInfo ProviderInfo { get; } = new()
    {
        Name = "Mock Service",
        Type = DocumentAnalysisProviderType.Custom,
        SupportedModels = new[] { "mock-model" },
        MaxContextLength = 4096
    };
    private string? _mockResponse;

    /// <summary>
    /// 테스트를 위한 Mock 응답 설정
    /// </summary>
    public void SetMockResponse(string response)
    {
        _mockResponse = response;
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // 설정된 Mock 응답이 있으면 반환
        if (!string.IsNullOrEmpty(_mockResponse))
        {
            var response = _mockResponse;
            _mockResponse = null; // 한 번만 사용하고 초기화
            return Task.FromResult(response);
        }

        // Simple mock that returns a score based on prompt content
        if (prompt.Contains("relevance", StringComparison.OrdinalIgnoreCase))
        {
            // Return different scores based on content
            if (prompt.Contains("machine learning", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("deep learning", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("0.8");
            }
            if (prompt.Contains("weather", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("pizza", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("0.2");
            }
        }
        
        // Default response
        return Task.FromResult("0.5");
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}