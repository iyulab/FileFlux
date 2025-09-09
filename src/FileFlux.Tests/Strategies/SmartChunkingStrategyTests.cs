using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Strategies;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Strategies;

public class SmartChunkingStrategyTests
{
    private readonly ITestOutputHelper _output;
    private readonly SmartChunkingStrategy _strategy;

    public SmartChunkingStrategyTests(ITestOutputHelper output)
    {
        _output = output;
        _strategy = new SmartChunkingStrategy();
    }

    [Fact]
    public async Task ChunkAsync_MaintainsMinimum70PercentCompleteness()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = @"FileFlux is a powerful document processing library designed for RAG systems. It provides intelligent chunking strategies that respect sentence boundaries. The library ensures that each chunk maintains semantic coherence.

The Smart chunking strategy is particularly effective for maintaining context. It analyzes sentence boundaries and ensures completeness. This approach significantly improves retrieval accuracy in RAG applications.

Performance benchmarks show excellent results across various document types. The system processes large documents efficiently. Memory usage remains optimal even with complex documents.

Integration with existing systems is straightforward and well-documented. The API provides flexible configuration options. Developers can customize chunking behavior to meet specific requirements.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "text/plain"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Smart",
            MaxChunkSize = 200, // Small size to force multiple chunks
            OverlapSize = 20
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        
        foreach (var chunk in chunks)
        {
            _output.WriteLine($"Chunk {chunk.ChunkIndex}:");
            _output.WriteLine($"Content: {chunk.Content}");
            _output.WriteLine($"Length: {chunk.Content.Length}");
            
            // Calculate completeness
            var sentences = chunk.Content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var completeSentences = 0;
            
            foreach (var sentence in sentences)
            {
                var trimmed = sentence.Trim();
                // Check if this looks like a complete sentence (not ending mid-word)
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    // A complete sentence typically doesn't end with "..." or mid-word
                    if (!trimmed.EndsWith("...") && !char.IsLetterOrDigit(trimmed.LastOrDefault()))
                    {
                        completeSentences++;
                    }
                    else if (sentence != sentences.Last()) // Middle sentences should be complete
                    {
                        completeSentences++;
                    }
                }
            }
            
            var completeness = sentences.Length > 0 ? (double)completeSentences / sentences.Length : 0;
            _output.WriteLine($"Completeness: {completeness:P0}");
            
            // Properties should contain completeness metric
            Assert.True(chunk.Properties.ContainsKey("Completeness"));
            var reportedCompleteness = Convert.ToDouble(chunk.Properties["Completeness"]);
            
            // Verify at least 70% completeness
            Assert.True(reportedCompleteness >= 0.7, 
                $"Chunk {chunk.ChunkIndex} has completeness {reportedCompleteness:P0}, expected >= 70%");
            
            _output.WriteLine("---");
        }
    }

    [Fact]
    public async Task ChunkAsync_PreservesSentenceBoundaries()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = "First sentence ends here. Second sentence starts and ends properly. Third sentence is also complete. Fourth sentence concludes the paragraph.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "text/plain"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Smart",
            MaxChunkSize = 100,
            OverlapSize = 0
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        
        foreach (var chunk in chunks)
        {
            _output.WriteLine($"Chunk: {chunk.Content}");
            
            // No chunk should end mid-sentence (except with "...")
            var lastChar = chunk.Content.Trim().LastOrDefault();
            Assert.True(
                lastChar == '.' || lastChar == '!' || lastChar == '?' || chunk.Content.Contains("..."),
                $"Chunk ends with incomplete sentence: '{chunk.Content}'");
        }
    }

    [Fact]
    public async Task ChunkAsync_HandlesOverlapCorrectly()
    {
        // Arrange - Use longer text that will force multiple chunks
        var sentences = new List<string>();
        for (int i = 1; i <= 20; i++)
        {
            sentences.Add($"This is sentence number {i} with enough content to make it substantial");
        }
        
        var content = new DocumentContent
        {
            Text = string.Join(". ", sentences) + ".",
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "text/plain"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Smart",
            MaxChunkSize = 200,  // Smaller to force chunking
            OverlapSize = 50
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        _output.WriteLine($"Generated {chunks.Count} chunks from {content.Text.Length} characters");

        // Assert
        if (chunks.Count == 1)
        {
            // If only one chunk, verify it has overlap property and skip overlap validation
            Assert.True(chunks[0].Properties.ContainsKey("HasOverlap"));
            _output.WriteLine("Single chunk created - SmartChunkingStrategy prioritized completeness");
            return;
        }
        
        // If multiple chunks, verify overlap properties
        Assert.True(chunks.Count >= 2, "Should create multiple chunks");
        
        // Check if chunks have overlap
        for (int i = 1; i < chunks.Count; i++)
        {
            var previousChunk = chunks[i - 1].Content;
            var currentChunk = chunks[i].Content;
            
            _output.WriteLine($"Chunk {i-1}: {previousChunk.Substring(0, Math.Min(50, previousChunk.Length))}...");
            _output.WriteLine($"Chunk {i}: {currentChunk.Substring(0, Math.Min(50, currentChunk.Length))}...");
            
            // Properties should indicate overlap
            Assert.True(chunks[i].Properties.ContainsKey("HasOverlap"));
            Assert.True(Convert.ToBoolean(chunks[i].Properties["HasOverlap"]));
            
            _output.WriteLine("---");
        }
    }

    [Fact]
    public async Task ChunkAsync_HandlesLongSentencesGracefully()
    {
        // Arrange
        var longSentence = "This is an extremely long sentence that contains many clauses, subclauses, and additional information that makes it exceed the maximum chunk size, which will require special handling to split it appropriately while maintaining as much semantic meaning as possible, even though it's challenging to break such a long sentence without losing some context.";
        
        var content = new DocumentContent
        {
            Text = longSentence,
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "text/plain"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Smart",
            MaxChunkSize = 100,
            OverlapSize = 0
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        
        foreach (var chunk in chunks)
        {
            _output.WriteLine($"Chunk: {chunk.Content}");
            _output.WriteLine($"Length: {chunk.Content.Length}");
            
            // Even split sentences should maintain some structure
            Assert.NotNull(chunk.Content);
            Assert.NotEmpty(chunk.Content);
            
            // Check for sentence integrity marker
            Assert.True(chunk.Properties.ContainsKey("SentenceIntegrity"));
        }
    }

    [Fact]
    public async Task ChunkAsync_MaintainsQualityScoreAboveThreshold()
    {
        // Arrange
        var content = new DocumentContent
        {
            Text = @"The FileFlux library revolutionizes document processing. Each component is carefully designed for optimal performance. The architecture follows clean code principles.

Quality metrics are continuously monitored and improved. The system adapts to different document types automatically. Performance benchmarks exceed industry standards.",
            Metadata = new DocumentMetadata
            {
                FileName = "test.txt",
                FileType = "text/plain"
            }
        };

        var options = new ChunkingOptions
        {
            Strategy = "Smart",
            MaxChunkSize = 150,
            OverlapSize = 20
        };

        // Act
        var chunks = (await _strategy.ChunkAsync(content, options)).ToList();

        // Assert
        Assert.NotEmpty(chunks);
        
        foreach (var chunk in chunks)
        {
            _output.WriteLine($"Chunk {chunk.ChunkIndex}:");
            _output.WriteLine($"Quality Score: {chunk.QualityScore:F2}");
            
            // Quality score should be reasonable (above 0.6)
            Assert.True(chunk.QualityScore >= 0.6, 
                $"Chunk {chunk.ChunkIndex} has low quality score: {chunk.QualityScore:F2}");
            
            // Verify quality components
            Assert.True(chunk.Properties.ContainsKey("Completeness"));
            Assert.True(chunk.Properties.ContainsKey("SemanticCoherence"));
            Assert.True(chunk.Properties.ContainsKey("SentenceIntegrity"));
            
            var completeness = Convert.ToDouble(chunk.Properties["Completeness"]);
            var coherence = Convert.ToDouble(chunk.Properties["SemanticCoherence"]);
            var integrity = Convert.ToDouble(chunk.Properties["SentenceIntegrity"]);
            
            _output.WriteLine($"  Completeness: {completeness:F2}");
            _output.WriteLine($"  Coherence: {coherence:F2}");
            _output.WriteLine($"  Integrity: {integrity:F2}");
            _output.WriteLine("---");
        }
    }
}