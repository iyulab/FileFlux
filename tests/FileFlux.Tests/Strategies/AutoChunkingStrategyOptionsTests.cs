using FileFlux.Domain;
using FileFlux.Infrastructure.Strategies;
using FileFlux.Infrastructure.Factories;
using FileFlux.Core;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Strategies;

/// <summary>
/// AutoChunkingStrategy 옵션 시스템 테스트
/// Phase A: 전략 옵션 시스템 검증
/// </summary>
public class AutoChunkingStrategyOptionsTests
{
    private readonly ITestOutputHelper _output;

    public AutoChunkingStrategyOptionsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void StrategyOptions_CanSetAndRetrieveValues()
    {
        // Arrange
        var options = new ChunkingOptions();

        // Act
        options.StrategyOptions["ConfidenceThreshold"] = 0.8;
        options.StrategyOptions["PreferSpeed"] = true;
        options.StrategyOptions["MaxAnalysisTime"] = 60;
        options.StrategyOptions["ForceStrategy"] = "Smart";

        // Assert
        Assert.Equal(0.8, options.StrategyOptions["ConfidenceThreshold"]);
        Assert.Equal(true, options.StrategyOptions["PreferSpeed"]);
        Assert.Equal(60, options.StrategyOptions["MaxAnalysisTime"]);
        Assert.Equal("Smart", options.StrategyOptions["ForceStrategy"]);

        _output.WriteLine("StrategyOptions successfully stores and retrieves values");
    }

    [Fact]
    public async Task AutoChunkingStrategy_RespectsForceStrategy()
    {
        // Arrange
        var selector = new TestAdaptiveStrategySelector();
        var factory = new ChunkingStrategyFactory();
        var autoStrategy = new AutoChunkingStrategy(selector, factory);

        var content = new DocumentContent
        {
            Text = "This is a test document for chunking. It has multiple sentences to test the chunking behavior properly.",
            Metadata = new DocumentMetadata { FileName = "test.txt" }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Auto",
            MaxChunkSize = 500
        };
        options.StrategyOptions["ForceStrategy"] = "FixedSize";

        // Act
        var chunks = (await autoStrategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Contains("Auto(FixedSize)", chunks[0].Strategy);
        Assert.Equal("FixedSize", chunks[0].Props["AutoSelectedStrategy"]);
        Assert.Contains("Forced strategy", chunks[0].Props["SelectionReasoning"]?.ToString() ?? "");

        _output.WriteLine($"ForceStrategy option correctly applied: {chunks[0].Strategy}");
    }

    [Fact]
    public async Task AutoChunkingStrategy_FallsBackOnLowConfidence()
    {
        // Arrange
        var selector = new TestAdaptiveStrategySelector(confidence: 0.4); // Low confidence
        var factory = new ChunkingStrategyFactory();
        var autoStrategy = new AutoChunkingStrategy(selector, factory);

        var content = new DocumentContent
        {
            Text = "Test document content for confidence threshold testing. Multiple sentences here.",
            Metadata = new DocumentMetadata { FileName = "test.txt" }
        };

        var options = new ChunkingOptions { Strategy = "Auto" };
        options.StrategyOptions["ConfidenceThreshold"] = 0.7; // Higher than selector's confidence

        // Act
        var chunks = (await autoStrategy.ChunkAsync(content, options)).ToList();

        // Assert - Should process without error
        Assert.NotEmpty(chunks);
        _output.WriteLine($"Strategy selected: {chunks[0].Strategy}");
        _output.WriteLine($"Reasoning: {chunks[0].Props["SelectionReasoning"]}");

        // Low confidence should trigger default strategy fallback
        var reasoning = chunks[0].Props["SelectionReasoning"]?.ToString() ?? "";
        Assert.True(
            reasoning.Contains("Low confidence") || reasoning.Contains("default"),
            $"Expected low confidence fallback, got: {reasoning}");
    }

    [Fact]
    public async Task AutoChunkingStrategy_RespectsPreferSpeed()
    {
        // Arrange
        var selector = new TestAdaptiveStrategySelector(strategyName: "Smart", confidence: 0.9);
        var factory = new ChunkingStrategyFactory();
        var autoStrategy = new AutoChunkingStrategy(selector, factory);

        var content = new DocumentContent
        {
            Text = "Test document for speed preference testing. Contains multiple sentences for proper chunking.",
            Metadata = new DocumentMetadata { FileName = "test.txt" }
        };

        var options = new ChunkingOptions { Strategy = "Auto" };
        options.StrategyOptions["PreferSpeed"] = true;

        // Act
        var chunks = (await autoStrategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        var reasoning = chunks[0].Props["SelectionReasoning"]?.ToString() ?? "";
        Assert.Contains("[Speed optimized]", reasoning);

        _output.WriteLine($"PreferSpeed option applied: {reasoning}");
    }

    [Fact]
    public async Task AutoChunkingStrategy_RespectsPreferQuality()
    {
        // Arrange
        var selector = new TestAdaptiveStrategySelector(strategyName: "Paragraph", confidence: 0.9);
        var factory = new ChunkingStrategyFactory();
        var autoStrategy = new AutoChunkingStrategy(selector, factory);

        var content = new DocumentContent
        {
            Text = "Test document for quality preference testing. Multiple sentences for testing purposes.",
            Metadata = new DocumentMetadata { FileName = "test.txt" }
        };

        var options = new ChunkingOptions { Strategy = "Auto" };
        options.StrategyOptions["PreferQuality"] = true;

        // Act
        var chunks = (await autoStrategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        var reasoning = chunks[0].Props["SelectionReasoning"]?.ToString() ?? "";
        Assert.Contains("[Quality optimized]", reasoning);

        _output.WriteLine($"PreferQuality option applied: {reasoning}");
    }

    [Theory]
    [InlineData("ConfidenceThreshold", 0.8, typeof(double))]
    [InlineData("MaxAnalysisTime", 120, typeof(int))]
    [InlineData("PreferSpeed", true, typeof(bool))]
    [InlineData("ForceStrategy", "Smart", typeof(string))]
    public void StrategyOptions_SupportsVariousTypes(string key, object value, Type expectedType)
    {
        // Arrange
        var options = new ChunkingOptions();

        // Act
        options.StrategyOptions[key] = value;
        var retrieved = options.StrategyOptions[key];

        // Assert
        Assert.Equal(value, retrieved);
        Assert.IsType(expectedType, retrieved);

        _output.WriteLine($"Key: {key}, Value: {value}, Type: {retrieved.GetType().Name}");
    }

    [Fact]
    public void StrategyOptions_StringConversion_WorksCorrectly()
    {
        // Arrange
        var options = new ChunkingOptions();
        options.StrategyOptions["ConfidenceThreshold"] = "0.75"; // String instead of double
        options.StrategyOptions["MaxAnalysisTime"] = "180"; // String instead of int
        options.StrategyOptions["PreferSpeed"] = "true"; // String instead of bool

        // Act & Assert - These should be stored as strings
        Assert.Equal("0.75", options.StrategyOptions["ConfidenceThreshold"]);
        Assert.Equal("180", options.StrategyOptions["MaxAnalysisTime"]);
        Assert.Equal("true", options.StrategyOptions["PreferSpeed"]);

        _output.WriteLine("String values stored correctly - GetStrategyOption<T> will handle conversion");
    }

    [Fact]
    public void StrategyOptions_IndependentFromCustomProperties()
    {
        // Arrange
        var options = new ChunkingOptions();

        // Act
        options.StrategyOptions["TestKey"] = "StrategyValue";
        options.CustomProperties["TestKey"] = "CustomValue";

        // Assert - StrategyOptions and CustomProperties should be independent
        Assert.Equal("StrategyValue", options.StrategyOptions["TestKey"]);
        Assert.Equal("CustomValue", options.CustomProperties["TestKey"]);

        _output.WriteLine("StrategyOptions and CustomProperties are independent dictionaries");
    }

    [Fact]
    public async Task AutoChunkingStrategy_DefaultsWhenNoOptionsSet()
    {
        // Arrange
        var selector = new TestAdaptiveStrategySelector(strategyName: "Semantic", confidence: 0.8);
        var factory = new ChunkingStrategyFactory();
        var autoStrategy = new AutoChunkingStrategy(selector, factory);

        var content = new DocumentContent
        {
            Text = "Test document without any custom strategy options. Should use defaults.",
            Metadata = new DocumentMetadata { FileName = "test.txt" }
        };

        var options = new ChunkingOptions { Strategy = "Auto" };
        // No StrategyOptions set

        // Act
        var chunks = (await autoStrategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        Assert.Contains("Auto(", chunks[0].Strategy);

        _output.WriteLine($"Default behavior works: {chunks[0].Strategy}");
    }
}

/// <summary>
/// IAdaptiveStrategySelector 테스트 구현체
/// </summary>
internal class TestAdaptiveStrategySelector : IAdaptiveStrategySelector
{
    private readonly string _strategyName;
    private readonly double _confidence;
    private readonly string _reasoning;

    public TestAdaptiveStrategySelector(
        string strategyName = "Smart",
        double confidence = 0.85,
        string reasoning = "Test selection")
    {
        _strategyName = strategyName;
        _confidence = confidence;
        _reasoning = reasoning;
    }

    public Task<StrategySelectionResult> SelectOptimalStrategyAsync(
        string filePath,
        DocumentContent? extractedContent = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StrategySelectionResult
        {
            StrategyName = _strategyName,
            Confidence = _confidence,
            Reasoning = _reasoning,
            UsedLLM = false
        });
    }
}
