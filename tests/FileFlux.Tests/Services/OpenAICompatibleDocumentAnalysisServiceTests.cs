using System.Net;
using System.Text;
using System.Text.Json;
using FileFlux.Domain;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace FileFlux.Tests.Services;

/// <summary>
/// Unit tests for OpenAICompatibleDocumentAnalysisService.
/// Uses a mock HttpMessageHandler to simulate OpenAI-compatible API responses.
/// </summary>
public sealed class OpenAICompatibleDocumentAnalysisServiceTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAICompatibleDocumentAnalysisService> _logger;
    private readonly OpenAICompatibleDocumentAnalysisService _sut;

    public OpenAICompatibleDocumentAnalysisServiceTests()
    {
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://api.example.com/v1/")
        };
        _logger = Substitute.For<ILogger<OpenAICompatibleDocumentAnalysisService>>();
        _sut = new OpenAICompatibleDocumentAnalysisService(_httpClient, "test-model", _logger);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithEndpoint_SetsBaseAddress()
    {
        // Arrange & Act
        using var sut = new OpenAICompatibleDocumentAnalysisService(
            "https://api.openai.com/v1", "test-key", "gpt-4o", _logger);

        // Assert
        sut.ProviderInfo.Name.Should().Be("OpenAI-Compatible");
        sut.ProviderInfo.SupportedModels.Should().Contain("gpt-4o");
    }

    [Fact]
    public void Constructor_WithEndpoint_ThrowsOnNullEndpoint()
    {
        var act = () => new OpenAICompatibleDocumentAnalysisService(
            null!, "key", "model", _logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEndpoint_ThrowsOnEmptyModel()
    {
        var act = () => new OpenAICompatibleDocumentAnalysisService(
            "https://api.example.com", "key", "", _logger);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEndpoint_ThrowsOnNullLogger()
    {
        var act = () => new OpenAICompatibleDocumentAnalysisService(
            "https://api.example.com", "key", "model", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithHttpClient_ThrowsOnNullClient()
    {
        var act = () => new OpenAICompatibleDocumentAnalysisService(
            null!, "model", _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithHttpClient_ThrowsOnEmptyModel()
    {
        using var client = new HttpClient();
        var act = () => new OpenAICompatibleDocumentAnalysisService(
            client, " ", _logger);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region ProviderInfo Tests

    [Fact]
    public void ProviderInfo_ReturnsCorrectInfo()
    {
        _sut.ProviderInfo.Name.Should().Be("OpenAI-Compatible");
        _sut.ProviderInfo.Type.Should().Be(DocumentAnalysisProviderType.OpenAI);
        _sut.ProviderInfo.SupportedModels.Should().ContainSingle("test-model");
        _sut.ProviderInfo.MaxContextLength.Should().Be(128_000);
        _sut.ProviderInfo.ApiVersion.Should().Be("v1");
    }

    #endregion

    #region GenerateAsync Tests

    [Fact]
    public async Task GenerateAsync_ReturnsApiResponse()
    {
        _handler.SetResponse(CreateChatCompletionResponse("Hello, world!"));

        var result = await _sut.GenerateAsync("Say hello");

        result.Should().Be("Hello, world!");
    }

    [Fact]
    public async Task GenerateAsync_SendsCorrectRequest()
    {
        _handler.SetResponse(CreateChatCompletionResponse("response"));

        await _sut.GenerateAsync("test prompt");

        _handler.LastRequestUri.Should().Contain("chat/completions");
        var body = _handler.LastRequestBody;
        body.Should().Contain("test-model");
        body.Should().Contain("test prompt");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsEmpty_WhenNoChoices()
    {
        _handler.SetResponse("""{"choices": []}""");

        var result = await _sut.GenerateAsync("test");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_ThrowsOnHttpError()
    {
        _handler.SetStatusCode(HttpStatusCode.InternalServerError);

        var act = () => _sut.GenerateAsync("test");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region AnalyzeStructureAsync Tests

    [Fact]
    public async Task AnalyzeStructureAsync_ParsesJsonResponse()
    {
        var json = """
        {
            "confidence": 0.92,
            "sections": [
                {
                    "type": "HEADING_L1",
                    "title": "Introduction",
                    "startPosition": 0,
                    "endPosition": 100,
                    "level": 1,
                    "importance": 0.9
                },
                {
                    "type": "PARAGRAPH",
                    "title": "Body",
                    "startPosition": 100,
                    "endPosition": 500,
                    "level": 2,
                    "importance": 0.7
                }
            ]
        }
        """;
        _handler.SetResponse(CreateChatCompletionResponse(json));

        var result = await _sut.AnalyzeStructureAsync("analyze this", DocumentType.Text);

        result.DocumentType.Should().Be(DocumentType.Text);
        result.Confidence.Should().Be(0.92);
        result.Sections.Should().HaveCount(2);
        result.Sections[0].Type.Should().Be(SectionType.HeadingL1);
        result.Sections[0].Title.Should().Be("Introduction");
        result.Sections[1].Type.Should().Be(SectionType.Paragraph);
    }

    [Fact]
    public async Task AnalyzeStructureAsync_HandlesMalformedJson()
    {
        _handler.SetResponse(CreateChatCompletionResponse("not valid json"));

        var result = await _sut.AnalyzeStructureAsync("analyze", DocumentType.Pdf);

        result.DocumentType.Should().Be(DocumentType.Pdf);
        result.Confidence.Should().Be(0.5);
        result.RawResponse.Should().Be("not valid json");
    }

    [Fact]
    public async Task AnalyzeStructureAsync_HandlesMarkdownWrappedJson()
    {
        var json = """
        ```json
        {
            "confidence": 0.85,
            "sections": []
        }
        ```
        """;
        _handler.SetResponse(CreateChatCompletionResponse(json));

        var result = await _sut.AnalyzeStructureAsync("analyze", DocumentType.Markdown);

        result.Confidence.Should().Be(0.85);
    }

    #endregion

    #region SummarizeContentAsync Tests

    [Fact]
    public async Task SummarizeContentAsync_ParsesSummary()
    {
        var json = """
        {
            "summary": "A short summary of the document.",
            "keywords": ["test", "document", "summary"],
            "confidence": 0.9
        }
        """;
        _handler.SetResponse(CreateChatCompletionResponse(json));

        var result = await _sut.SummarizeContentAsync("some long text content", 200);

        result.Summary.Should().Be("A short summary of the document.");
        result.Keywords.Should().Contain("test");
        result.Confidence.Should().Be(0.9);
        result.OriginalLength.Should().Be("some long text content".Length);
    }

    [Fact]
    public async Task SummarizeContentAsync_FallsBackOnMalformedJson()
    {
        _handler.SetResponse(CreateChatCompletionResponse("plain text fallback"));

        var result = await _sut.SummarizeContentAsync("content", 200);

        result.Summary.Should().Be("plain text fallback");
        result.Confidence.Should().Be(0.5);
    }

    #endregion

    #region ExtractMetadataAsync Tests

    [Fact]
    public async Task ExtractMetadataAsync_ParsesMetadata()
    {
        var json = """
        {
            "keywords": ["AI", "machine learning"],
            "language": "en",
            "categories": ["technology"],
            "confidence": 0.88,
            "technicalMetadata": {
                "framework": ".NET"
            },
            "entities": {
                "organizations": ["Microsoft", "Google"]
            }
        }
        """;
        _handler.SetResponse(CreateChatCompletionResponse(json));

        var result = await _sut.ExtractMetadataAsync("text", DocumentType.Text);

        result.Keywords.Should().Contain("AI");
        result.Language.Should().Be("en");
        result.Categories.Should().Contain("technology");
        result.TechnicalMetadata.Should().ContainKey("framework");
        result.Entities.Should().ContainKey("organizations");
        result.Entities["organizations"].Should().Contain("Microsoft");
    }

    [Fact]
    public async Task ExtractMetadataAsync_FallsBackOnMalformedJson()
    {
        _handler.SetResponse(CreateChatCompletionResponse("not json"));

        var result = await _sut.ExtractMetadataAsync("text", DocumentType.Text);

        result.Confidence.Should().Be(0.5);
    }

    #endregion

    #region AssessQualityAsync Tests

    [Fact]
    public async Task AssessQualityAsync_ParsesAssessment()
    {
        var json = """
        {
            "confidenceScore": 0.85,
            "completenessScore": 0.9,
            "consistencyScore": 0.88,
            "explanation": "Document is well-structured.",
            "recommendations": [
                {
                    "type": "METADATA_ENHANCEMENT",
                    "description": "Add more keywords",
                    "priority": 3
                }
            ]
        }
        """;
        _handler.SetResponse(CreateChatCompletionResponse(json));

        var result = await _sut.AssessQualityAsync("assess this");

        result.ConfidenceScore.Should().Be(0.85);
        result.CompletenessScore.Should().Be(0.9);
        result.ConsistencyScore.Should().Be(0.88);
        result.Explanation.Should().Be("Document is well-structured.");
        result.Recommendations.Should().HaveCount(1);
        result.Recommendations[0].Type.Should().Be(RecommendationType.MetadataEnhancement);
        result.Recommendations[0].Priority.Should().Be(3);
    }

    [Fact]
    public async Task AssessQualityAsync_FallsBackOnMalformedJson()
    {
        _handler.SetResponse(CreateChatCompletionResponse("broken"));

        var result = await _sut.AssessQualityAsync("text");

        result.ConfidenceScore.Should().Be(0.8);
        result.Explanation.Should().Contain("JSON parsing failed");
    }

    #endregion

    #region IsAvailableAsync Tests

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_WhenApiResponds()
    {
        _handler.SetResponse(CreateChatCompletionResponse("ok"));

        var result = await _sut.IsAvailableAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenApiFails()
    {
        _handler.SetStatusCode(HttpStatusCode.ServiceUnavailable);

        var result = await _sut.IsAvailableAsync();

        result.Should().BeFalse();
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_WithOwnedClient_DisposesClient()
    {
        // When created with endpoint, it owns the HttpClient
        var sut = new OpenAICompatibleDocumentAnalysisService(
            "https://api.example.com/v1", null, "model", _logger);
        sut.Dispose();

        // Should not throw - second dispose is safe
        sut.Dispose();
    }

    [Fact]
    public void Dispose_WithExternalClient_DoesNotDisposeClient()
    {
        using var client = new HttpClient { BaseAddress = new Uri("https://api.example.com/v1/") };
        var sut = new OpenAICompatibleDocumentAnalysisService(client, "model", _logger);
        sut.Dispose();

        // Client should still be usable
        client.BaseAddress.Should().NotBeNull();
    }

    #endregion

    #region ExtractJson Tests

    [Fact]
    public void ExtractJson_ReturnsPlainJson()
    {
        var result = OpenAICompatibleDocumentAnalysisService.ExtractJson("""{"key": "value"}""");
        result.Should().Be("""{"key": "value"}""");
    }

    [Fact]
    public void ExtractJson_UnwrapsMarkdownCodeBlock()
    {
        var input = "```json\n{\"key\": \"value\"}\n```";
        var result = OpenAICompatibleDocumentAnalysisService.ExtractJson(input);
        result.Should().Be("{\"key\": \"value\"}\n");
    }

    [Fact]
    public void ExtractJson_TrimsWhitespace()
    {
        var result = OpenAICompatibleDocumentAnalysisService.ExtractJson("  {\"key\": \"value\"}  ");
        result.Should().Be("{\"key\": \"value\"}");
    }

    #endregion

    #region Helpers

    private static string CreateChatCompletionResponse(string content)
    {
        var escaped = content.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        return $$"""
        {
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": "{{escaped}}"
                    }
                }
            ]
        }
        """;
    }

    /// <summary>
    /// Mock HttpMessageHandler for testing HTTP-based services.
    /// </summary>
    internal sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private string _responseBody = "{}";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;

        public string? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        public void SetResponse(string body)
        {
            _responseBody = body;
            _statusCode = HttpStatusCode.OK;
        }

        public void SetStatusCode(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
            _responseBody = "";
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    #endregion
}
