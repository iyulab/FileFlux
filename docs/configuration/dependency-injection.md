# Dependency Injection Patterns

> **FileFlux Philosophy**: Register what you need. We adapt to what's available.

## Overview

FileFlux follows a **composition over configuration** approach. All services are optional, and FileFlux adapts its behavior based on what's registered in your DI container.

---

## Quick Start

### Minimal Setup (No AI Services)

FileFlux works perfectly without any AI services:

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// That's it! FileFlux uses fallback mechanisms for all AI features
services.AddFileFlux();

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();
```

**What You Get:**
- ✅ Full document parsing (8 formats)
- ✅ All chunking strategies (7 strategies)
- ✅ Basic quality metrics (statistical)
- ✅ Metadata extraction (rule-based)
- ❌ AI-powered quality analysis
- ❌ Q&A benchmark generation

---

## Standard Setup (With AI Services)

### Pattern 1: Single AI Service

```csharp
var services = new ServiceCollection();

// 1. Register your AI service implementation
services.AddScoped<ITextCompletionService, MyOpenAIService>();

// 2. Register FileFlux (detects and uses your service)
services.AddFileFlux();

var provider = services.BuildServiceProvider();
```

### Pattern 2: Multiple Optional Services

```csharp
var services = new ServiceCollection();

// Text completion (for intelligent chunking and quality analysis)
services.AddScoped<ITextCompletionService, MyOpenAIService>();

// Image extraction (for multimodal documents)
services.AddScoped<IImageToTextService, MyVisionService>();

// Embeddings (for semantic chunking)
services.AddScoped<IEmbeddingService, MyEmbeddingService>();

// FileFlux adapts based on what's available
services.AddFileFlux();
```

---

## Service Lifetimes

FileFlux services have specific lifetime requirements:

| Service | Recommended Lifetime | Reason |
|---------|---------------------|--------|
| **IDocumentProcessor** | `Scoped` | Maintains processing state per request |
| **IDocumentQualityAnalyzer** | `Scoped` | Uses IDocumentProcessor internally |
| **ITextCompletionService** | `Scoped` or `Singleton` | Depends on your HTTP client management |
| **IImageToTextService** | `Scoped` or `Singleton` | Depends on your HTTP client management |
| **IEmbeddingService** | `Scoped` or `Singleton` | Depends on your HTTP client management |
| **IDocumentReaderFactory** | `Singleton` | Stateless factory |
| **IChunkingStrategy** | `Singleton` | Stateless strategies |

### Why Scoped for AI Services?

```csharp
// ✅ GOOD: Scoped with HttpClient per request
services.AddHttpClient<ITextCompletionService, MyOpenAIService>();
services.AddFileFlux();

// ✅ GOOD: Singleton with shared HttpClientFactory
services.AddHttpClient();
services.AddSingleton<ITextCompletionService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    return new MyOpenAIService(httpClientFactory);
});
services.AddFileFlux();

// ❌ BAD: Singleton with HttpClient directly (socket exhaustion)
services.AddSingleton<ITextCompletionService>(new MyOpenAIService(new HttpClient()));
services.AddFileFlux();
```

---

## Environment-Specific Configuration

### Development vs Production

```csharp
public void ConfigureServices(IServiceCollection services)
{
    if (Environment.IsDevelopment())
    {
        // Use mock for development (no API costs)
        services.AddScoped<ITextCompletionService, MockTextCompletionService>();
    }
    else if (Environment.IsStaging())
    {
        // Use cheaper model for staging
        services.AddScoped<ITextCompletionService>(sp =>
            new OpenAIService(sp, modelName: "gpt-3.5-turbo"));
    }
    else // Production
    {
        // Use best model for production
        services.AddScoped<ITextCompletionService>(sp =>
            new OpenAIService(sp, modelName: "gpt-4"));
    }

    services.AddFileFlux();
}
```

### Configuration-Based Registration

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    var aiConfig = configuration.GetSection("AI");

    // Register based on configuration
    if (aiConfig.GetValue<bool>("Enabled"))
    {
        var provider = aiConfig.GetValue<string>("Provider");

        switch (provider)
        {
            case "OpenAI":
                services.AddScoped<ITextCompletionService, OpenAIService>();
                break;

            case "Azure":
                services.AddScoped<ITextCompletionService, AzureOpenAIService>();
                break;

            case "Anthropic":
                services.AddScoped<ITextCompletionService, AnthropicService>();
                break;

            default:
                // No AI service - FileFlux uses fallbacks
                break;
        }
    }

    services.AddFileFlux();
}
```

**appsettings.json:**
```json
{
  "AI": {
    "Enabled": true,
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "sk-...",
      "Model": "gpt-4"
    }
  }
}
```

---

## Advanced Patterns

### Pattern 1: Conditional Service Registration

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileFluxWithOptionalAI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Always register FileFlux
        services.AddFileFlux();

        // Conditionally register AI services
        var apiKey = configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrEmpty(apiKey))
        {
            services.AddScoped<ITextCompletionService>(sp =>
                new OpenAIService(apiKey, sp.GetRequiredService<IHttpClientFactory>()));
        }

        return services;
    }
}

// Usage
services.AddFileFluxWithOptionalAI(configuration);
```

### Pattern 2: Factory-Based Registration

```csharp
// Register factory for dynamic service selection
services.AddSingleton<ITextCompletionServiceFactory, TextCompletionServiceFactory>();

services.AddScoped<ITextCompletionService>(sp =>
{
    var factory = sp.GetRequiredService<ITextCompletionServiceFactory>();
    var config = sp.GetRequiredService<IConfiguration>();

    // Select service based on runtime conditions
    return factory.Create(config["AI:Provider"]);
});

services.AddFileFlux();
```

### Pattern 3: Decorator Pattern for Logging/Monitoring

```csharp
// Wrapper for logging/telemetry
public class LoggingTextCompletionService : ITextCompletionService
{
    private readonly ITextCompletionService _inner;
    private readonly ILogger _logger;

    public LoggingTextCompletionService(
        ITextCompletionService inner,
        ILogger<LoggingTextCompletionService> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(...)
    {
        _logger.LogInformation("Analyzing structure for {DocumentType}", documentType);
        var sw = Stopwatch.StartNew();

        try
        {
            var result = await _inner.AnalyzeStructureAsync(prompt, documentType, cancellationToken);
            _logger.LogInformation("Analysis completed in {ElapsedMs}ms, Tokens: {Tokens}",
                sw.ElapsedMilliseconds, result.TokensUsed);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }

    // Implement other methods...
}

// Registration
services.AddScoped<ITextCompletionService, OpenAIService>();
services.Decorate<ITextCompletionService, LoggingTextCompletionService>();
services.AddFileFlux();
```

---

## Service Detection and Fallback

### How FileFlux Detects Services

```csharp
// FileFlux checks at runtime
public class DocumentProcessor : IDocumentProcessor
{
    private readonly ITextCompletionService? _textService;
    private readonly IImageToTextService? _imageService;

    public DocumentProcessor(
        IServiceProvider serviceProvider)
    {
        // Try to resolve optional services
        _textService = serviceProvider.GetService<ITextCompletionService>();
        _imageService = serviceProvider.GetService<IImageToTextService>();

        // Adapt behavior based on availability
        _useAIEnhancement = _textService != null;
        _useImageExtraction = _imageService != null;
    }

    public async Task<List<DocumentChunk>> ProcessAsync(string filePath)
    {
        // Use AI if available
        if (_textService != null && await _textService.IsAvailableAsync())
        {
            return await ProcessWithAI(filePath);
        }

        // Fallback to rule-based processing
        return await ProcessWithoutAI(filePath);
    }
}
```

### Fallback Behavior

| Feature | With AI Service | Without AI Service |
|---------|----------------|-------------------|
| **Chunking** | AI-guided boundaries | Statistical boundaries |
| **Metadata** | Semantic extraction | Rule-based extraction |
| **Quality Analysis** | AI-powered scores | Statistical scores |
| **Q&A Generation** | Full generation | Not available |
| **Structure Analysis** | Deep understanding | Heuristic parsing |

---

## Testing Configurations

### Unit Tests (Mock Services)

```csharp
public class ProcessorTests
{
    [Fact]
    public async Task ProcessDocument_WithMockService_ReturnsChunks()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<ITextCompletionService, MockTextCompletionService>();
        services.AddFileFlux();

        var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<IDocumentProcessor>();

        // Act
        var chunks = await processor.ProcessAsync("test.pdf");

        // Assert
        Assert.NotEmpty(chunks);
    }
}
```

### Integration Tests (Real Services)

```csharp
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessDocument_WithRealService_ReturnsQualityChunks()
    {
        // Requires OPENAI_API_KEY environment variable
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Assume.That(apiKey, Is.Not.Null, "Integration test requires API key");

        var services = new ServiceCollection();
        services.AddScoped<ITextCompletionService>(sp =>
            new OpenAIService(apiKey, sp.GetRequiredService<IHttpClientFactory>()));
        services.AddFileFlux();

        var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<IDocumentProcessor>();

        var chunks = await processor.ProcessAsync("real-document.pdf");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.True(chunk.Metadata.QualityScore > 0.7));
    }
}
```

---

## ASP.NET Core Integration

### Startup.cs / Program.cs

```csharp
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add FileFlux with AI services
        builder.Services.AddHttpClient(); // For AI service HTTP calls

        // Register custom AI service
        builder.Services.AddScoped<ITextCompletionService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<OpenAIService>>();

            return new OpenAIService(
                config["OpenAI:ApiKey"],
                httpClientFactory,
                logger);
        });

        // Register FileFlux services
        builder.Services.AddFileFlux();

        // Other services
        builder.Services.AddControllers();

        var app = builder.Build();
        app.MapControllers();
        app.Run();
    }
}
```

### Controller Usage

```csharp
[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentProcessor _processor;
    private readonly IDocumentQualityAnalyzer _qualityAnalyzer;

    public DocumentsController(
        IDocumentProcessor processor,
        IDocumentQualityAnalyzer qualityAnalyzer)
    {
        _processor = processor;
        _qualityAnalyzer = qualityAnalyzer;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessDocument(IFormFile file)
    {
        var tempPath = Path.GetTempFileName();
        using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }

        var chunks = await _processor.ProcessAsync(tempPath);
        System.IO.File.Delete(tempPath);

        return Ok(new { ChunkCount = chunks.Count, Chunks = chunks });
    }

    [HttpPost("analyze-quality")]
    public async Task<IActionResult> AnalyzeQuality(IFormFile file)
    {
        var tempPath = Path.GetTempFileName();
        using (var stream = System.IO.File.Create(tempPath))
        {
            await file.CopyToAsync(stream);
        }

        var report = await _qualityAnalyzer.AnalyzeQualityAsync(tempPath);
        System.IO.File.Delete(tempPath);

        return Ok(report);
    }
}
```

---

## Background Services / Workers

```csharp
public class DocumentProcessingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentProcessingWorker> _logger;

    public DocumentProcessingWorker(
        IServiceProvider serviceProvider,
        ILogger<DocumentProcessingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Create scope for scoped services
            using var scope = _serviceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessor>();

            try
            {
                // Process pending documents
                var pendingDocs = await GetPendingDocuments();
                foreach (var doc in pendingDocs)
                {
                    var chunks = await processor.ProcessAsync(doc.Path);
                    await SaveChunks(doc.Id, chunks);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing documents");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

// Registration
services.AddHostedService<DocumentProcessingWorker>();
services.AddFileFlux();
```

---

## Troubleshooting

### Service Not Found

**Error:** `Cannot resolve service 'IDocumentProcessor'`

**Solution:**
```csharp
// Ensure AddFileFlux() is called
services.AddFileFlux(); // ✅ Required
```

---

### AI Service Not Being Used

**Symptom:** FileFlux processes documents but doesn't use AI features

**Diagnosis:**
```csharp
// Check if service is registered
var textService = serviceProvider.GetService<ITextCompletionService>();
if (textService == null)
{
    Console.WriteLine("ITextCompletionService not registered");
}
else if (!await textService.IsAvailableAsync())
{
    Console.WriteLine("ITextCompletionService registered but unavailable");
}
else
{
    Console.WriteLine("ITextCompletionService registered and available");
}
```

**Solutions:**
1. Verify service registration: `services.AddScoped<ITextCompletionService, MyService>()`
2. Check service availability: Implement `IsAvailableAsync` correctly
3. Verify API keys and configuration

---

### Lifetime Issues

**Error:** `Cannot access a disposed object`

**Cause:** Service lifetime mismatch

**Solution:**
```csharp
// ❌ BAD: Singleton depends on Scoped
services.AddSingleton<MySingletonService>(); // Tries to inject IDocumentProcessor
services.AddFileFlux(); // IDocumentProcessor is Scoped

// ✅ GOOD: Inject IServiceProvider and create scope
public class MySingletonService
{
    private readonly IServiceProvider _serviceProvider;

    public MySingletonService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task ProcessAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IDocumentProcessor>();
        await processor.ProcessAsync("doc.pdf");
    }
}
```

---

## Best Practices

### ✅ DO
- Use `AddFileFlux()` for automatic service registration
- Register AI services as `Scoped` with `HttpClientFactory`
- Use environment-specific configurations
- Create scopes for background services
- Implement `IsAvailableAsync()` correctly
- Log AI service calls for monitoring

### ❌ DON'T
- Register FileFlux services manually (use `AddFileFlux()`)
- Use `Singleton` lifetime with HttpClient directly
- Inject Scoped services into Singletons
- Skip error handling in AI service implementations
- Ignore service availability checks

---

## Related Documentation

- [ITextCompletionService Integration](../integration/text-completion-service.md) - AI service implementation
- [Mock Implementations](../testing/mock-implementations.md) - Testing without AI
- [Quality Analysis](../features/quality-analysis.md) - Using analyzer services

---

## Summary

**FileFlux DI is:**
- 🔧 **Flexible** - Works with or without AI services
- 🎯 **Adaptive** - Detects and uses what's available
- 📦 **Simple** - One method: `AddFileFlux()`
- 🧩 **Composable** - Mix and match services as needed

**Key Principles:**
1. **Optional Everything** - All AI services are optional
2. **Graceful Degradation** - Falls back to rule-based when AI unavailable
3. **Lifetime Awareness** - Use correct service lifetimes
4. **Environment Specific** - Configure per environment

**Quick Checklist:**
- ✅ Call `AddFileFlux()` to register core services
- ✅ Optionally register `ITextCompletionService` for AI features
- ✅ Use `Scoped` lifetime for AI services with HTTP clients
- ✅ Test with Mock in development, real in staging/production
- ✅ Implement `IsAvailableAsync()` correctly in your AI services
