using System.Net;
using System.Text;
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

        var result = await _sut.GenerateAsync("Say hello", TestContext.Current.CancellationToken);

        result.Should().Be("Hello, world!");
    }

    [Fact]
    public async Task GenerateAsync_SendsCorrectRequest()
    {
        _handler.SetResponse(CreateChatCompletionResponse("response"));

        await _sut.GenerateAsync("test prompt", TestContext.Current.CancellationToken);

        _handler.LastRequestUri.Should().Contain("chat/completions");
        var body = _handler.LastRequestBody;
        body.Should().Contain("test-model");
        body.Should().Contain("test prompt");
    }

    [Fact]
    public async Task GenerateAsync_ReturnsEmpty_WhenNoChoices()
    {
        _handler.SetResponse("""{"choices": []}""");

        var result = await _sut.GenerateAsync("test", TestContext.Current.CancellationToken);

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

    #region IsAvailableAsync Tests

    [Fact]
    public async Task IsAvailableAsync_ReturnsTrue_WhenApiResponds()
    {
        _handler.SetResponse(CreateChatCompletionResponse("ok"));

        var result = await _sut.IsAvailableAsync(TestContext.Current.CancellationToken);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_ReturnsFalse_WhenApiFails()
    {
        _handler.SetStatusCode(HttpStatusCode.ServiceUnavailable);

        var result = await _sut.IsAvailableAsync(TestContext.Current.CancellationToken);

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
