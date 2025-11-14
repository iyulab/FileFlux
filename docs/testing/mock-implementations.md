# Using Mock Implementations for Testing

> **Best Practice**: Test your FileFlux integration without real AI service calls

## Overview

FileFlux provides `MockTextCompletionService` for testing purposes. This allows you to:
- Test document processing pipeline without AI costs
- Create deterministic unit tests
- Understand interface contracts through working code
- Develop integration patterns before implementing real services

---

## MockTextCompletionService

### Location
```
Namespace: FileFlux.Tests.Mocks
Class: MockTextCompletionService
```

### Purpose

1. **Testing Reference**: See how FileFlux expects responses to be structured
2. **Development**: Build your application before AI service is ready
3. **CI/CD**: Run tests without API keys or external dependencies
4. **Learning**: Understand the interface contract through working code

---

## Basic Usage

### 1. In Unit Tests

```csharp
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;

public class DocumentProcessingTests
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
        var chunks = await processor.ProcessAsync("test-document.pdf");

        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.NotNull(chunk.Content));
    }
}
```

### 2. In Development Environment

```csharp
// Program.cs or Startup.cs
public void ConfigureServices(IServiceCollection services)
{
    if (Environment.IsDevelopment())
    {
        // Use Mock in development
        services.AddScoped<ITextCompletionService, MockTextCompletionService>();
    }
    else
    {
        // Use real service in production
        services.AddScoped<ITextCompletionService, MyProductionService>();
    }

    services.AddFileFlux();
}
```

---

## Mock Behavior

### What Mock Returns

The mock service provides **reasonable default responses** for all interface methods:

#### AnalyzeStructureAsync
```csharp
{
    DocumentType = <input document type>,
    Sections = [
        {
            Type = HeadingL1,
            Title = "Test Section",
            StartPosition = 0,
            EndPosition = 100,
            Level = 1,
            Importance = 0.8
        }
    ],
    Confidence = 0.8,
    TokensUsed = 100
}
```

#### SummarizeContentAsync
```csharp
{
    Summary = "Mock summary for testing purposes",
    Keywords = ["test", "mock", "sample"],
    Confidence = 0.8,
    OriginalLength = <prompt length>,
    TokensUsed = 50
}
```

#### ExtractMetadataAsync
```csharp
{
    Keywords = ["test", "mock"],
    Language = "en",
    Categories = ["test"],
    Entities = {},
    TechnicalMetadata = {},
    Confidence = 0.9,
    TokensUsed = 75
}
```

#### AssessQualityAsync
```csharp
{
    ConfidenceScore = 0.85,
    CompletenessScore = 0.8,
    ConsistencyScore = 0.9,
    Recommendations = [
        {
            Type = CHUNK_SIZE_OPTIMIZATION,
            Description = "Mock recommendation",
            Priority = 5
        }
    ],
    Explanation = "Mock quality assessment",
    TokensUsed = 60
}
```

#### GenerateAsync
Returns simple score strings like `"0.5"`, `"0.8"`, etc. depending on prompt content.

---

## Advanced: Custom Mock Responses

Mock supports setting custom responses for testing specific scenarios:

```csharp
[Fact]
public async Task TestCustomMockResponse()
{
    // Arrange
    var mockService = new MockTextCompletionService();
    mockService.SetMockResponse("Custom test response");

    // Act
    var result = await mockService.GenerateAsync("test prompt");

    // Assert
    Assert.Equal("Custom test response", result);
}
```

**Note**: Custom responses are consumed once and reset automatically.

---

## Using Mock as Implementation Reference

### Pattern 1: Understanding Return Types

```csharp
// Read MockTextCompletionService source to understand:
public class MyTextCompletionService : ITextCompletionService
{
    public async Task<StructureAnalysisResult> AnalyzeStructureAsync(...)
    {
        // See Mock for what fields are required
        return new StructureAnalysisResult
        {
            DocumentType = documentType,           // Always echo back
            Sections = ParseSections(response),    // Parse from LLM response
            Structure = BuildStructure(sections),  // Build hierarchy
            Confidence = 0.85,                     // Your confidence calculation
            RawResponse = response,                // Store original response
            TokensUsed = tokenCount                // Track usage
        };
    }
}
```

### Pattern 2: Error Handling

```csharp
// Mock never throws - it returns valid empty results
// Your implementation should do the same:

public async Task<MetadataExtractionResult> ExtractMetadataAsync(...)
{
    try
    {
        // Your LLM call
        return await _client.ExtractMetadata(prompt);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Metadata extraction failed");

        // Return valid empty result like Mock does
        return new MetadataExtractionResult
        {
            Keywords = Array.Empty<string>(),
            Language = "unknown",
            Categories = Array.Empty<string>(),
            Confidence = 0.0,
            TokensUsed = 0
        };
    }
}
```

### Pattern 3: Confidence Scoring

```csharp
// Mock uses reasonable confidence scores (0.7-0.9)
// Your implementation should calculate based on:

private double CalculateConfidence(string response)
{
    double confidence = 0.5; // Start neutral

    // Adjust based on response quality
    if (HasStructuredFormat(response)) confidence += 0.2;
    if (HasRequiredFields(response)) confidence += 0.2;
    if (HasValidValues(response)) confidence += 0.1;

    return Math.Clamp(confidence, 0.0, 1.0);
}
```

---

## Testing Strategies

### Strategy 1: Pipeline Testing

Test the complete FileFlux pipeline with Mock:

```csharp
[Theory]
[InlineData("document.pdf", ChunkingStrategies.Intelligent)]
[InlineData("document.docx", ChunkingStrategies.Smart)]
[InlineData("document.md", ChunkingStrategies.Paragraph)]
public async Task ProcessWithDifferentStrategies_UsesMockService(
    string fileName,
    string strategy)
{
    // Arrange
    var services = new ServiceCollection();
    services.AddScoped<ITextCompletionService, MockTextCompletionService>();
    services.AddFileFlux();

    var provider = services.BuildServiceProvider();
    var processor = provider.GetRequiredService<IDocumentProcessor>();

    var options = new ChunkingOptions { Strategy = strategy };

    // Act
    var chunks = await processor.ProcessAsync(fileName, options);

    // Assert - FileFlux pipeline works with Mock
    Assert.NotEmpty(chunks);
}
```

### Strategy 2: Quality Analysis Testing

```csharp
[Fact]
public async Task QualityAnalysis_WithMockService_ReturnsMetrics()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddScoped<ITextCompletionService, MockTextCompletionService>();
    services.AddFileFlux();

    var provider = services.BuildServiceProvider();
    var analyzer = provider.GetRequiredService<IDocumentQualityAnalyzer>();

    // Act
    var report = await analyzer.AnalyzeQualityAsync("test.pdf");

    // Assert - Quality analysis works with Mock
    Assert.InRange(report.OverallQualityScore, 0.0, 1.0);
    Assert.NotNull(report.ChunkingQuality);
}
```

### Strategy 3: Fallback Behavior Testing

Test FileFlux behavior when AI service is unavailable:

```csharp
[Fact]
public async Task Processing_WhenMockUnavailable_UsesFallback()
{
    // Arrange
    var mockService = new MockTextCompletionService();
    // Mock is always available, but you can test real service unavailability

    var services = new ServiceCollection();
    services.AddScoped<ITextCompletionService>(sp => mockService);
    services.AddFileFlux();

    var provider = services.BuildServiceProvider();
    var processor = provider.GetRequiredService<IDocumentProcessor>();

    // Act
    var chunks = await processor.ProcessAsync("test.pdf");

    // Assert - Should still work (fallback mechanisms)
    Assert.NotEmpty(chunks);
}
```

---

## Comparison with Real Implementation

### Mock vs Real Service

| Aspect | MockTextCompletionService | Your Real Service |
|--------|---------------------------|-------------------|
| **Purpose** | Testing, development | Production use |
| **Performance** | Instant (no network calls) | 1-5 seconds per call |
| **Cost** | Free | Per-token cost |
| **Accuracy** | Generic responses | Actual AI analysis |
| **Availability** | Always 100% | Depends on provider |
| **Setup** | No configuration | API keys, endpoints |

### When to Use Each

**Use Mock When:**
- Writing unit tests
- Developing without AI service ready
- Running CI/CD pipelines
- Testing error handling
- Learning FileFlux integration

**Use Real Service When:**
- Testing actual AI quality
- Benchmarking chunking strategies
- Validating production behavior
- Measuring real performance
- Cost estimation

---

## Creating Your Own Mock

You can create custom mocks for specific test scenarios:

```csharp
public class CustomTestMock : ITextCompletionService
{
    public TextCompletionServiceInfo ProviderInfo => new()
    {
        Name = "Custom Test Mock",
        Type = TextCompletionProviderType.Custom
    };

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<StructureAnalysisResult> AnalyzeStructureAsync(...)
    {
        // Return custom test data
        return Task.FromResult(new StructureAnalysisResult
        {
            Sections = GetTestSections(), // Your test data
            Confidence = 1.0  // Perfect confidence for testing
        });
    }

    // Implement other methods with test-specific behavior
}
```

---

## Migration Path: Mock → Real

### Step 1: Develop with Mock
```csharp
// Use Mock during development
services.AddScoped<ITextCompletionService, MockTextCompletionService>();
```

### Step 2: Implement Real Service
```csharp
// Implement your service following the interface contract
public class MyOpenAIService : ITextCompletionService
{
    // Your implementation
}
```

### Step 3: Test Both
```csharp
// Test with Mock for fast iteration
[Fact]
public async Task FastTest_WithMock() { /* ... */ }

// Test with real service for validation
[Fact]
[Trait("Category", "Integration")]
public async Task RealTest_WithOpenAI() { /* ... */ }
```

### Step 4: Switch to Production
```csharp
// Use real service in production
if (Environment.IsProduction())
{
    services.AddScoped<ITextCompletionService, MyOpenAIService>();
}
else
{
    services.AddScoped<ITextCompletionService, MockTextCompletionService>();
}
```

---

## Mock Source Code Reference

Want to see exactly how Mock implements the interface?

**Location:** `tests/FileFlux.Tests/Mocks/MockTextCompletionService.cs`

**Key Learnings from Source:**
1. All methods return valid non-null objects
2. Scores are always in valid ranges (0.0-1.0)
3. Empty collections instead of null
4. Reasonable default values
5. Simple parsing logic for test scenarios

**Recommended:** Read the Mock source code alongside this guide.

---

## Best Practices

### ✅ DO
- Use Mock for unit tests
- Use Mock in CI/CD pipelines
- Reference Mock for interface understanding
- Create custom mocks for specific test scenarios
- Test both with Mock and real service

### ❌ DON'T
- Use Mock in production
- Rely on Mock for quality benchmarking
- Assume Mock behavior matches real AI
- Skip testing with real service entirely
- Copy Mock logic directly (it's simplified)

---

## Troubleshooting

### Mock Not Found
**Error:** Cannot resolve `MockTextCompletionService`

**Solution:** Add test project reference:
```xml
<ItemGroup>
  <ProjectReference Include="..\FileFlux.Tests\FileFlux.Tests.csproj" />
</ItemGroup>
```

Or copy the Mock class to your test project.

---

### Tests Pass with Mock but Fail with Real Service
**Cause:** Mock doesn't validate real-world scenarios

**Solution:**
1. Add integration tests with real service
2. Compare Mock and real service outputs
3. Adjust your service implementation
4. Check error handling in your implementation

---

## Related Documentation

- [ITextCompletionService Integration Guide](../integration/text-completion-service.md) - Full interface specification
- [Dependency Injection Patterns](../configuration/dependency-injection.md) - Service registration
- [Quality Analysis Features](../features/quality-analysis.md) - Using AI services

---

## Summary

**MockTextCompletionService is your:**
- 🧪 **Testing tool** - No AI costs, fast tests
- 📚 **Learning resource** - Understand interface contracts
- 🏗️ **Development aid** - Build before AI is ready
- 📖 **Implementation guide** - Reference for your own service

**Next Steps:**
1. Use Mock in your tests
2. Study Mock source code
3. Implement your real service
4. Test both Mock and real scenarios

---

**Questions?** Check the Mock source code for implementation details.
