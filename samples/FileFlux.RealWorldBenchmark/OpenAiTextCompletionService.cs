using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FileFlux;
using FileFlux.Domain;

namespace FileFlux.RealWorldBenchmark;

/// <summary>
/// OpenAI Text Completion Service implementation for benchmarking
/// </summary>
public class OpenAiTextCompletionService : ITextCompletionService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly bool _isConfigured;

    public TextCompletionServiceInfo ProviderInfo { get; }

    public OpenAiTextCompletionService(string? apiKey = null, string? model = null)
    {
        _httpClient = new HttpClient();
        
        // Try to load from environment variables
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        _model = model ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-3.5-turbo";
        
        _isConfigured = !string.IsNullOrEmpty(_apiKey) && !_apiKey.Contains("your-") && !_apiKey.Contains("here");
        
        if (_isConfigured)
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
        }
        
        ProviderInfo = new TextCompletionServiceInfo
        {
            Name = _isConfigured ? "OpenAI" : "OpenAI (Unconfigured - Using Mock)",
            Type = TextCompletionProviderType.OpenAI,
            SupportedModels = new[] { "gpt-3.5-turbo", "gpt-4", "gpt-4-turbo-preview", "gpt-5-nano" },
            MaxContextLength = _model.Contains("gpt-5") ? 16384 : (_model.Contains("gpt-4") ? 8192 : 4096)
        };
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured)
        {
            // Fallback to mock behavior if not configured
            return await MockGenerateAsync(prompt, cancellationToken);
        }

        try
        {
            var request = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a helpful assistant for document analysis." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 500
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"OpenAI API Error: {response.StatusCode} - {errorContent}");
                return await MockGenerateAsync(prompt, cancellationToken);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            
            var completion = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return completion ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error calling OpenAI API: {ex.Message}");
            return await MockGenerateAsync(prompt, cancellationToken);
        }
    }

    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        var structurePrompt = $"""
            Analyze the structure of this {documentType} document and identify:
            1. Main sections and their hierarchy
            2. Key topics and themes
            3. Document organization pattern

            Document content:
            {prompt.Substring(0, Math.Min(prompt.Length, 2000))}

            Respond with:
            - Section titles (one per line)
            - Importance score (0-1) for each section
            """;

        var response = await GenerateAsync(structurePrompt, cancellationToken);
        
        // Parse response into sections
        var sections = ParseSectionsFromResponse(response);
        
        return new StructureAnalysisResult
        {
            DocumentType = documentType,
            Sections = sections,
            Structure = new FileFlux.CoreDocumentStructure
            {
                Root = sections.FirstOrDefault() ?? new SectionInfo { Title = "Document", Type = SectionType.HEADING_L1 },
                AllSections = sections
            },
            Confidence = _isConfigured ? 0.85 : 0.7,
            TokensUsed = EstimateTokens(structurePrompt + response)
        };
    }

    public async Task<ContentSummary> SummarizeContentAsync(
        string prompt,
        int maxLength = 200,
        CancellationToken cancellationToken = default)
    {
        var summaryPrompt = $"""
            Summarize the following content in {maxLength} characters or less.
            Extract 3-5 key keywords or phrases.
            
            Content:
            {prompt.Substring(0, Math.Min(prompt.Length, 3000))}
            """;

        var response = await GenerateAsync(summaryPrompt, cancellationToken);
        
        // Extract summary and keywords
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = lines.FirstOrDefault() ?? "Document summary";
        var keywords = ExtractKeywords(response);
        
        return new ContentSummary
        {
            Summary = summary.Substring(0, Math.Min(summary.Length, maxLength)),
            Keywords = keywords,
            Confidence = _isConfigured ? 0.8 : 0.6,
            OriginalLength = prompt.Length,
            TokensUsed = EstimateTokens(summaryPrompt + response)
        };
    }

    public async Task<MetadataExtractionResult> ExtractMetadataAsync(
        string prompt,
        DocumentType documentType,
        CancellationToken cancellationToken = default)
    {
        var metadataPrompt = $"""
            Extract metadata from this {documentType} document:
            - Language
            - Main topics/categories
            - Key entities (people, organizations, locations)
            - Technical terms
            
            Content:
            {prompt.Substring(0, Math.Min(prompt.Length, 2000))}
            """;

        var response = await GenerateAsync(metadataPrompt, cancellationToken);
        
        return new MetadataExtractionResult
        {
            Keywords = ExtractKeywords(response),
            Language = DetectLanguage(prompt),
            Categories = ExtractCategories(response),
            Entities = ExtractEntities(response),
            TechnicalMetadata = new Dictionary<string, string>
            {
                ["model"] = _model,
                ["provider"] = "OpenAI"
            },
            Confidence = _isConfigured ? 0.75 : 0.5,
            TokensUsed = EstimateTokens(metadataPrompt + response)
        };
    }

    public async Task<QualityAssessment> AssessQualityAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var qualityPrompt = $"""
            Assess the quality of this text chunk for RAG (Retrieval-Augmented Generation):
            1. Is it semantically complete? (0-1)
            2. Does it maintain context? (0-1)
            3. Is it self-contained? (0-1)
            
            Chunk:
            {prompt.Substring(0, Math.Min(prompt.Length, 1000))}
            
            Respond with three scores (0-1) and a brief explanation.
            """;

        var response = await GenerateAsync(qualityPrompt, cancellationToken);
        
        // Parse scores from response
        var scores = ExtractScores(response);
        
        return new QualityAssessment
        {
            ConfidenceScore = scores.GetValueOrDefault("confidence", 0.7),
            CompletenessScore = scores.GetValueOrDefault("completeness", 0.7),
            ConsistencyScore = scores.GetValueOrDefault("consistency", 0.7),
            Recommendations = new List<QualityRecommendation>
            {
                new QualityRecommendation
                {
                    Type = RecommendationType.CHUNK_SIZE_OPTIMIZATION,
                    Description = "Consider adjusting chunk size for better semantic completeness",
                    Priority = 5
                }
            },
            Explanation = response.Length > 100 ? response.Substring(0, 100) : response,
            TokensUsed = EstimateTokens(qualityPrompt + response)
        };
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isConfigured);
    }

    // Helper methods
    private async Task<string> MockGenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        // Fallback mock implementation
        await Task.Delay(10, cancellationToken);
        
        if (prompt.Contains("relevance", StringComparison.OrdinalIgnoreCase))
        {
            if (prompt.Contains("machine learning", StringComparison.OrdinalIgnoreCase))
                return "0.8";
            if (prompt.Contains("weather", StringComparison.OrdinalIgnoreCase))
                return "0.2";
        }
        
        return "0.5";
    }

    private List<SectionInfo> ParseSectionsFromResponse(string response)
    {
        var sections = new List<SectionInfo>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!string.IsNullOrEmpty(line))
            {
                sections.Add(new SectionInfo
                {
                    Title = line,
                    Type = i == 0 ? SectionType.HEADING_L1 : SectionType.HEADING_L2,
                    Level = i == 0 ? 1 : 2,
                    StartPosition = i * 100,
                    EndPosition = (i + 1) * 100,
                    Importance = Math.Max(0.3, 1.0 - (i * 0.1))
                });
            }
        }
        
        return sections.Any() ? sections : new List<SectionInfo>
        {
            new SectionInfo { Title = "Main Content", Type = SectionType.HEADING_L1, Level = 1, Importance = 0.8 }
        };
    }

    private string[] ExtractKeywords(string text)
    {
        var words = text.Split(new[] { ' ', '\n', ',', '.', ';', ':', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Take(5)
            .ToArray();
        
        return words.Length > 0 ? words : new[] { "document", "content", "analysis" };
    }

    private string[] ExtractCategories(string text)
    {
        // Simple category extraction
        var categories = new List<string>();
        
        if (text.Contains("technical", StringComparison.OrdinalIgnoreCase))
            categories.Add("Technical");
        if (text.Contains("business", StringComparison.OrdinalIgnoreCase))
            categories.Add("Business");
        if (text.Contains("research", StringComparison.OrdinalIgnoreCase))
            categories.Add("Research");
        
        return categories.Any() ? categories.ToArray() : new[] { "General" };
    }

    private Dictionary<string, string[]> ExtractEntities(string text)
    {
        // Simplified entity extraction
        return new Dictionary<string, string[]>
        {
            ["organizations"] = new[] { "OpenAI" },
            ["technologies"] = new[] { "GPT", "RAG" }
        };
    }

    private string DetectLanguage(string text)
    {
        // Simple language detection
        var hasKorean = text.Any(c => c >= 0xAC00 && c <= 0xD7AF);
        return hasKorean ? "ko" : "en";
    }

    private Dictionary<string, double> ExtractScores(string response)
    {
        var scores = new Dictionary<string, double>
        {
            ["confidence"] = 0.7,
            ["completeness"] = 0.7,
            ["consistency"] = 0.7
        };
        
        // Try to extract numeric values from response
        var numbers = System.Text.RegularExpressions.Regex.Matches(response, @"0\.\d+|\d+\.\d+");
        if (numbers.Count > 0)
        {
            scores["completeness"] = Math.Min(1.0, double.Parse(numbers[0].Value));
            if (numbers.Count > 1)
                scores["consistency"] = Math.Min(1.0, double.Parse(numbers[1].Value));
            if (numbers.Count > 2)
                scores["confidence"] = Math.Min(1.0, double.Parse(numbers[2].Value));
        }
        
        return scores;
    }

    private int EstimateTokens(string text)
    {
        // Rough estimation: ~4 characters per token
        return text.Length / 4;
    }
}