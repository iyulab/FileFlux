using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FileFlux.RealWorldBenchmark.Services;

/// <summary>
/// OpenAI Embedding API 서비스
/// </summary>
public class OpenAiEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    
    private const string EmbeddingEndpoint = "https://api.openai.com/v1/embeddings";

    public OpenAiEmbeddingService(string apiKey, string model = "text-embedding-3-small")
    {
        _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        _model = model;
        
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "FileFlux-Benchmark/1.0");
    }

    /// <summary>
    /// 텍스트에 대한 임베딩 생성
    /// </summary>
    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new float[0];

        try
        {
            // 텍스트 길이 제한 (8000자 이상이면 자름)
            if (text.Length > 8000)
            {
                text = text.Substring(0, 8000) + "...";
            }

            var request = new
            {
                input = text,
                model = _model,
                encoding_format = "float"
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(EmbeddingEndpoint, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error ({response.StatusCode}): {error}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(jsonResponse);

            if (embeddingResponse?.Data == null || embeddingResponse.Data.Count == 0)
                throw new Exception("Invalid embedding response from OpenAI");

            return embeddingResponse.Data[0].Embedding;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Embedding generation failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 여러 텍스트에 대한 임베딩 일괄 생성
    /// </summary>
    public async Task<List<float[]>> GenerateEmbeddingsAsync(List<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts == null || texts.Count == 0)
            return new List<float[]>();

        // 텍스트 길이 제한
        var processedTexts = texts.Select(text => 
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Length > 8000 ? text.Substring(0, 8000) + "..." : text;
        }).ToList();

        try
        {
            var request = new
            {
                input = processedTexts,
                model = _model,
                encoding_format = "float"
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(EmbeddingEndpoint, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenAI API Error ({response.StatusCode}): {error}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(jsonResponse);

            if (embeddingResponse?.Data == null)
                throw new Exception("Invalid embedding response from OpenAI");

            return embeddingResponse.Data.Select(d => d.Embedding).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Batch embedding generation failed: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// Response classes for JSON deserialization
public class EmbeddingResponse
{
    public List<EmbeddingData> Data { get; set; } = new();
    public UsageInfo Usage { get; set; } = new();
}

public class EmbeddingData
{
    public float[] Embedding { get; set; } = new float[0];
    public int Index { get; set; }
}

public class UsageInfo
{
    public int Prompt_tokens { get; set; }
    public int Total_tokens { get; set; }
}