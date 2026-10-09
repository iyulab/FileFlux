# FileFlux Tutorial

Complete guide to using FileFlux for document processing and RAG system integration.

## Table of Contents

- [Installation](#installation)
- [Basic Usage](#basic-usage)
- [Stateful Pipeline](#stateful-pipeline) *(v0.9.0+)*
- [Document Formats](#document-formats)
- [Chunking Strategies](#chunking-strategies)
- [Advanced Features](#advanced-features)
- [RAG Integration](#rag-integration)
- [Error Handling](#error-handling)
- [Customization](#customization)

## Installation

Install FileFlux via NuGet:

```bash
dotnet add package FileFlux
```

## Basic Usage

### Service Registration

`AddFileFlux()` registers the pipeline and an `IDocumentProcessorFactory`. AI services are optional; register your
implementations before or after `AddFileFlux()` — the pipeline resolves them when it runs. No logger is required.

```csharp
using FileFlux;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Optional AI services:
// - IDocumentAnalysisService: LLM refinement and enrichment
// - IImageToTextService: text from images inside documents
services.AddSingleton<IDocumentAnalysisService>(myAnalysisService);
services.AddSingleton<IImageToTextService>(myVisionService);

services.AddFileFlux();

using var provider = services.BuildServiceProvider();
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
```

### Service Lifetime Configuration

FileFlux services are registered with `Scoped` lifetime by default, which suits web applications with a per-request
scope. When a singleton consumes FileFlux (a background or hosted service, a queue worker), register it as a singleton:

```csharp
// Default: Scoped lifetime (for web applications)
services.AddFileFlux();

// Singleton lifetime (for background services, IHostedService, etc.)
services.AddFileFlux(ServiceLifetime.Singleton);
```

```csharp
using FileFlux;
using System.Threading;

// A singleton worker that processes one file per call. Register it with services.AddSingleton<DocumentWorker>()
// (or run it from a BackgroundService) after services.AddFileFlux(ServiceLifetime.Singleton).
public sealed class DocumentWorker(IDocumentProcessorFactory factory)
{
    public async Task<int> CountChunksAsync(string path, CancellationToken cancellationToken)
    {
        await using var processor = factory.Create(path);
        await processor.ProcessAsync(cancellationToken: cancellationToken);
        return processor.Result.Chunks?.Count ?? 0;
    }
}
```

**Note:** Document readers are always registered as Transient (stateless), and converters/normalizers are always Singleton (thread-safe). Only the factory and processor services respect the lifetime parameter.

### Simple Document Processing

A processor handles one document: create it from a path (or a `Stream`/`byte[]` with its extension), run the pipeline,
and read the chunks from `Result`.

```csharp
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");

await processor.ProcessAsync();

foreach (var chunk in processor.Result)
{
    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.Content}");
}
```

### Streaming Processing

`ProcessStreamAsync` yields chunks as they are produced; `Result.Chunks` holds all of them once the enumeration ends.

```csharp
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");

await foreach (var chunk in processor.ProcessStreamAsync())
{
    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.Content.Length} chars, quality {chunk.Quality:F2}");
}
```

### Chunking Options

```csharp
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");

await processor.ProcessAsync(new ProcessingOptions
{
    Chunking = new ChunkingOptions
    {
        Strategy = ChunkingStrategies.Auto,   // automatic strategy selection (recommended)
        MaxChunkSize = 512,                   // maximum chunk size in tokens
        OverlapSize = 64                      // overlap between chunks
    }
});
```

## Stateful Pipeline

The stateful pipeline (v0.9.0+) provides explicit control over each processing stage with state management.

### Creating a Stateful Processor

```csharp
using FileFlux;
using FileFlux.Infrastructure.Factories;

var services = new ServiceCollection();
services.AddFileFlux();
var provider = services.BuildServiceProvider();

// Create processor via factory
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
using var processor = factory.Create("document.pdf");
```

### Stage-by-Stage Execution

```csharp
// Stage 1: Extract raw content
await processor.ExtractAsync();
Console.WriteLine($"Extracted: {processor.Result.Raw?.Text.Length} chars");

// Stage 2: Refine content with structure analysis
await processor.RefineAsync(new RefineOptions
{
    CleanNoise = true,
    BuildSections = true,
    ExtractStructures = true
});
Console.WriteLine($"Sections: {processor.Result.Refined?.Sections.Count}");

// Stage 3: Chunk content
await processor.ChunkAsync(new ChunkingOptions
{
    Strategy = "Auto",
    MaxChunkSize = 512
});
Console.WriteLine($"Chunks: {processor.Result.Chunks?.Count}");

// Stage 4: Enrich with LLM (optional)
await processor.EnrichAsync(new EnrichOptions
{
    BuildGraph = true,
    GenerateSummaries = true,
    ExtractKeywords = true
});
Console.WriteLine($"Graph nodes: {processor.Result.Graph?.NodeCount}");
```

### Full Pipeline Execution

```csharp
// Run all stages at once
await processor.ProcessAsync(new ProcessingOptions
{
    IncludeEnrich = true,
    Chunking = new ChunkingOptions { Strategy = "Auto", MaxChunkSize = 512 },
    Enrich = new EnrichOptions { BuildGraph = true }
});

// Access all results
var raw = processor.Result.Raw;
var refined = processor.Result.Refined;
var chunks = processor.Result.Chunks;
var graph = processor.Result.Graph;
```

### Processing State

```csharp
// Check current state
Console.WriteLine($"State: {processor.State}");
// Created → Extracted → Refined → Chunked → Enriched

// State transitions are automatic
await processor.ChunkAsync();  // Auto-runs Extract + Refine if needed
```

### Document Graph

```csharp
// Build graph showing relationships between chunks
var graph = processor.Result.Graph;

// Graph contains nodes (chunks) and edges (relationships)
foreach (var node in graph.Nodes)
{
    Console.WriteLine($"Node {node.Index}: {node.ChunkId}");
}

foreach (var edge in graph.Edges)
{
    Console.WriteLine($"Edge: {edge.FromIndex} → {edge.ToIndex} ({edge.Type})");
}

// Edge types: Sequential, Hierarchical, Semantic
```

### Streaming Enrichment

```csharp
// Process chunks as they're enriched
await foreach (var enrichedChunk in processor.EnrichStreamAsync())
{
    Console.WriteLine($"Chunk {enrichedChunk.Chunk.Index}: {enrichedChunk.Summary}");
}
```

## Document Formats

FileFlux supports the following document formats:

| Format | Extension | Text Extraction | Image Processing |
|--------|-----------|----------------|------------------|
| PDF | `.pdf` | ✅ | ✅ |
| Word | `.docx`, `.doc` | ✅ | Planned |
| Excel | `.xlsx`, `.xls` | ✅ | ❌ |
| PowerPoint | `.pptx`, `.ppt` | ✅ | Planned |
| Markdown | `.md` | ✅ | ❌ |
| HTML | `.html`, `.htm` | ✅ | ✅ |
| Text | `.txt` | ✅ | ❌ |
| JSON | `.json` | ✅ | ❌ |
| CSV | `.csv` | ✅ | ❌ |

### Format-Specific Features

**PDF**: Text and image extraction, structure recognition, metadata preservation

**Word**: Style recognition, headers, tables, and image captions

**Excel**: Multi-sheet support, formula extraction, table structure analysis

**PowerPoint**: Slide content, notes, and title structure extraction

**Markdown**: Header, code block, and table structure preservation

**HTML**: Web content extraction with structure preservation

**Text**: Plain text with automatic encoding detection

**JSON**: Structured data flattening and schema extraction

**CSV**: Table data with header preservation

### Extension Discovery

```csharp
var factory = provider.GetRequiredService<IDocumentReaderFactory>();

// Get all supported extensions
var extensions = factory.GetSupportedExtensions();
Console.WriteLine($"Supported: {string.Join(", ", extensions)}");

// Check specific extension
bool isSupported = factory.IsExtensionSupported(".pdf");

// Get extension-to-reader mapping
var mapping = factory.GetExtensionReaderMapping();
foreach (var kvp in mapping)
{
    Console.WriteLine($"{kvp.Key} → {kvp.Value}");
}
```

## Chunking Strategies

`ChunkingOptions.Strategy` takes one of the names in `ChunkingStrategies` (case-insensitive); any other name throws
`ArgumentException`. What each produces and what it needs is in the [README's strategy table](../README.md#chunking-strategies).

| Strategy | Use for |
|---|---|
| `Auto` (default) | Most documents: picks Sentence, Paragraph or Token from the text and records the choice in each chunk's `Strategy` |
| `Sentence` | Prose where sentences must stay whole (legal, medical, academic text) |
| `Paragraph` | Markdown, blog posts and other text with clear paragraphs |
| `Token` | Unstructured text that only needs even, token-bounded pieces |
| `Hierarchical` | Documents with a heading structure (manuals, specifications) |
| `Semantic` | Boundaries by embedding similarity; needs an embedder (`IEmbeddingService`, for example `AddLMSupplyEmbedding`) |

```csharp
var options = new ChunkingOptions
{
    Strategy = ChunkingStrategies.Paragraph,
    MaxChunkSize = 512,
    OverlapSize = 64
};
```

## Advanced Features

### Metadata Enrichment

`AIMetadataEnricher` (interface `IMetadataEnricher`) describes a piece of text — topics, keywords, a one-line
description and schema-specific fields. It is a **standalone service, not a pipeline stage**: `ProcessAsync` does not
call it and it writes nothing into chunks. Call it on the text you want described (a document's extracted text, a
section, a chunk) and store the result where you need it. Per-chunk summaries and keywords from the pipeline come from
the Enrich stage instead (see [Streaming Enrichment](#streaming-enrichment)).

`AddFileFlux()` does not register it. Construct it with a `RuleBasedMetadataExtractor`, an `IMemoryCache` (used only by
`EnrichWithCacheAsync`/`EnrichBatchAsync`) and, optionally, your `IDocumentAnalysisService`:

```csharp
using FileFlux.Core;
using FileFlux.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;

var enricher = new AIMetadataEnricher(
    new RuleBasedMetadataExtractor(),
    new MemoryCache(new MemoryCacheOptions()),
    myAnalysisService);   // optional: without it, the rule-based extractor answers every call

var documentText = await File.ReadAllTextAsync("guide.md");
var metadata = await enricher.EnrichAsync(documentText, MetadataSchema.General, new MetadataEnrichmentOptions
{
    ExtractionStrategy = MetadataExtractionStrategy.Smart,
    MinConfidence = 0.7
});

Console.WriteLine($"{metadata["extractionMethod"]} (confidence {metadata["confidence"]})");
if (metadata.TryGetValue("topics", out var topics) && topics is string[] topicList)
    Console.WriteLine($"Topics: {string.Join(", ", topicList)}");
```

`EnrichAsync` returns an `IDictionary<string, object>`. Two keys are always present: `confidence` (`double`, 0.0–1.0)
and `extractionMethod` — `"ai"`, `"hybrid"` (AI merged with rule-based, see below), `"rule-based"`, or
`"ai-parse-failed"` (the model's reply could not be parsed as a JSON object; confidence is then 0.5, which is below the
default `MinConfidence`, so by default such a reply is merged and reported as `"hybrid"`). The other keys are the fields the
schema asks for. On the AI path they are whatever the model returned: strings, numbers (`double`), booleans and string
arrays (`string[]`); a field the model left out is absent.

#### Metadata Schemas

`MetadataSchema` selects the prompt sent to the model and the rule-based patterns:

| Schema | AI prompt asks for | Rule-based extractor fills |
|---|---|---|
| `General` | `topics`, `keywords`, `description`, `documentType`, `language`, `categories` | `topics`, `keywords`, `description`, `documentType`, `language` |
| `ProductManual` | `productName`, `company`, `version`, `topics`, `keywords`, optional `releaseDate`, `model`, `categories`, plus `description`, `documentType`, `language` | `productName`, `company`, `version`, `releaseDate`, `topics`, `keywords`, `documentType` (`"manual"`) |
| `TechnicalDoc` | `topics`, `libraries`, `frameworks`, `technologies`, `keywords`, `description`, optional `categories`, plus `documentType`, `language` | `libraries`, `frameworks`, `technologies`, `topics`, `keywords`, `documentType` |
| `Custom` | the prompt in `MetadataEnrichmentOptions.CustomPrompt` (the `General` prompt when none is set) | same as `General` |

`CustomPrompt` replaces the schema prompt for any schema, not only `Custom`. Rule-based fields other than `documentType` and
`language` are present only when their patterns matched.

#### Options

Every `MetadataEnrichmentOptions` member applies only when an `IDocumentAnalysisService` was passed; without one the
call goes straight to the rule-based extractor.

| Option | Default | Effect |
|---|---|---|
| `ExtractionStrategy` | `Smart` | How much of the text goes into the prompt: `Fast` 2,000 characters, `Smart` 4,000, `Deep` 8,000. Longer text is cut at that point. |
| `MaxTokens` | `null` | When set, replaces the strategy's budget: `MaxTokens × 4` characters. |
| `CustomPrompt` | `null` | Replaces the schema prompt (see above). |
| `MinConfidence` | `0.6` | When the model's `confidence` is below it, the rule-based result is merged in: the model's fields win, missing ones are filled from the rules, `confidence` becomes the average of both, `extractionMethod` becomes `"hybrid"`. |
| `TimeoutMs` | `30000` | Time limit for one model call. |
| `MaxRetries` | `2` | Extra attempts after a failed or timed-out call (three calls in total by default). A reply without a numeric `confidence` is not a failure: it is used with confidence 0.5, like a reply that could not be parsed. |
| `RetryDelayMs` | `1000` | Wait before the first retry, doubling for each further retry (1 s, 2 s, 4 s …). |
| `ContinueOnEnrichmentFailure` | `true` | After the last failed attempt: `true` returns the rule-based result, `false` throws `InvalidOperationException` with the last error as its inner exception. |

Cancelling the `CancellationToken` you pass stops the call with `OperationCanceledException`; a cancelled call is not
retried and is not answered with the rule-based result. A call that runs past `TimeoutMs` is a failed attempt.

#### Caching

`EnrichAsync` itself does not cache. `EnrichWithCacheAsync` stores the result in the `IMemoryCache` you passed for one
hour, under the key you choose combined with what else changes the result: the schema, `ExtractionStrategy`, `MaxTokens`,
`MinConfidence` and `CustomPrompt`. Two calls with the same key and different options therefore do not share a result.
`GenerateCacheKey(filePath, schema)` builds a key from the file's SHA-256 hash and the schema:

```csharp
var key = enricher.GenerateCacheKey("guide.md", MetadataSchema.General);
var cached = await enricher.EnrichWithCacheAsync(documentText, key, MetadataSchema.General);
```

Each entry has size 1, so a `MemoryCacheOptions.SizeLimit` on your cache bounds the number of cached results.

#### Several Texts at Once

`EnrichBatchAsync` takes a list of `BatchMetadataRequest` (`DocumentId`, `Content`, optional `CacheKey`) and returns
one `EnrichedMetadataResult` per request (`Metadata`, `Confidence`, `FromCache`, `ExtractionMethod`). Requests with a
cached key (under the same schema and options) are answered from the cache; the rest are enriched one after another with the same rules as `EnrichAsync`.
A request that fails gets a result with `ExtractionMethod = "failed"` and the error message under `Metadata["error"]`
instead of failing the batch.

### Multimodal Processing

Process documents with images using vision AI:

```csharp
// Implement image-to-text service
public class OpenAiVisionService : IImageToTextService
{
    private readonly OpenAIClient _client;

    public OpenAiVisionService(string apiKey)
    {
        _client = new OpenAIClient(apiKey);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient("gpt-4-vision-preview");

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Extract all text from the image accurately."),
            new UserChatMessage(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(imageData), "image/jpeg"))
        };

        var response = await chatClient.CompleteChatAsync(messages, cancellationToken);

        return new ImageToTextResult
        {
            ExtractedText = response.Value.Content[0].Text,
            Confidence = 0.95,
            IsSuccess = true
        };
    }
}

// Register and use
services.AddScoped<IImageToTextService, OpenAiVisionService>();

// Process document with images
await foreach (var result in processor.ProcessStreamAsync("document-with-images.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            if (chunk.Props.ContainsKey("HasImages"))
            {
                Console.WriteLine($"Image text extracted: {chunk.Content}");
            }
        }
    }
}
```

### Step-by-Step Processing

Use the stateful pipeline for fine-grained control over each processing stage. The file path is set when creating the processor via the factory; individual stage methods take no path parameter:

```csharp
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
using var processor = factory.Create("document.pdf");

// Stage 1: Extract raw content
await processor.ExtractAsync();
Console.WriteLine($"Extracted: {processor.Result.Raw?.Text.Length} chars");

// Stage 2: Rule-based refine
await processor.RefineAsync();
Console.WriteLine($"Sections: {processor.Result.Refined?.Sections.Count ?? 0}");

// Stage 2.5: LLM refine (optional — skipped automatically if LLM unavailable)
await processor.LlmRefineAsync();

// Stage 3: Chunk content
await processor.ChunkAsync(new ChunkingOptions
{
    Strategy = "Auto",
    MaxChunkSize = 512,
    OverlapSize = 64
});
Console.WriteLine($"Chunks: {processor.Result.Chunks?.Count}");
```

### Quality Analysis

Analyze chunk quality metrics:

```csharp
// Process document first
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
using var processor = factory.Create("document.pdf");
await processor.ProcessAsync();
var chunks = processor.Result.Chunks ?? [];

// Calculate quality metrics (static method)
var metrics = await ChunkQualityEngine.CalculateQualityMetricsAsync(chunks);
Console.WriteLine($"Average Completeness: {metrics.AverageCompleteness:P}");
Console.WriteLine($"Content Consistency: {metrics.ContentConsistency:P}");
Console.WriteLine($"Boundary Quality: {metrics.BoundaryQuality:P}");
Console.WriteLine($"Size Distribution: {metrics.SizeDistribution:P}");
```

## RAG Integration

### Complete RAG Pipeline

```csharp
public class RagService
{
    private readonly IDocumentProcessor _processor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;

    public async Task IndexDocumentAsync(string filePath)
    {
        var options = new ChunkingOptions
        {
            Strategy = "Auto",
            MaxChunkSize = 512,
            OverlapSize = 64
        };

        await foreach (var result in _processor.ProcessStreamAsync(filePath, options))
        {
            if (result.IsSuccess && result.Result != null)
            {
                foreach (var chunk in result.Result)
                {
                    // Generate embedding
                    var embedding = await _embeddingService.GenerateAsync(chunk.Content);

                    // Store in vector database
                    await _vectorStore.StoreAsync(new
                    {
                        Id = chunk.Id,
                        Content = chunk.Content,
                        Metadata = chunk.Props,
                        Vector = embedding
                    });
                }
            }

            // Display progress
            if (result.Progress != null)
            {
                Console.WriteLine($"Progress: {result.Progress.PercentComplete:F1}%");
            }
        }
    }
}
```

### Batch Processing

```csharp
public async Task ProcessMultipleDocumentsAsync(string[] filePaths)
{
    var tasks = filePaths.Select(async filePath =>
    {
        var chunks = new List<DocumentChunk>();

        await foreach (var result in processor.ProcessStreamAsync(filePath))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks.AddRange(result.Result);
            }
        }

        return new { FilePath = filePath, Chunks = chunks };
    });

    var results = await Task.WhenAll(tasks);

    foreach (var result in results)
    {
        Console.WriteLine($"{result.FilePath}: {result.Chunks.Count} chunks");
    }
}
```

## Error Handling

### Exception Handling

```csharp
try
{
    var chunks = await processor.ProcessAsync("document.pdf");
}
catch (UnsupportedFileFormatException ex)
{
    Console.WriteLine($"Unsupported format: {ex.FileName}");
}
catch (DocumentProcessingException ex)
{
    Console.WriteLine($"Processing error: {ex.Message}");
    Console.WriteLine($"File: {ex.FileName}");
}
catch (FileNotFoundException)
{
    Console.WriteLine("File not found");
}
```

### Streaming Error Handling

```csharp
await foreach (var result in processor.ProcessStreamAsync("document.pdf"))
{
    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.Error}");
        continue; // Continue with next chunk
    }

    if (result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"Chunk {chunk.Index} processed successfully");
        }
    }
}
```

### Validation

```csharp
public async Task<bool> ValidateAndProcessAsync(string filePath)
{
    // Check file exists
    if (!File.Exists(filePath))
    {
        Console.WriteLine("File not found");
        return false;
    }

    // Check extension
    var factory = provider.GetRequiredService<IDocumentReaderFactory>();
    var extension = Path.GetExtension(filePath);

    if (!factory.IsExtensionSupported(extension))
    {
        Console.WriteLine($"Unsupported extension: {extension}");
        return false;
    }

    // Process
    try
    {
        var chunks = await processor.ProcessAsync(filePath);
        Console.WriteLine($"Processed: {chunks.Count()} chunks");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        return false;
    }
}
```

## Customization

### Custom Chunking Strategy

```csharp
public class CustomChunkingStrategy : IChunkingStrategy
{
    public string StrategyName => "Custom";

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        ParsedDocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = content.Content.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                Content = sentence.Trim(),
                Index = chunkIndex++,
                Location = new SourceLocation
                {
                    StartChar = 0,
                    EndChar = sentence.Length
                },
                Quality = CalculateQuality(sentence),
                Props = new Dictionary<string, object>
                {
                    ["Length"] = sentence.Length
                }
            });
        }

        return chunks;
    }

    private double CalculateQuality(string text)
    {
        return text.Length > 50 ? 0.8 : 0.5;
    }
}

// Register
services.AddTransient<IChunkingStrategy, CustomChunkingStrategy>();
```

### Custom Document Reader

`IDocumentReader` has two stages: `ReadAsync` (Stage 0 - document structure) and `ExtractAsync` (Stage 1 - raw content).

```csharp
public class CustomDocumentReader : IDocumentReader
{
    public string ReaderType => "CustomReader";
    public IEnumerable<string> SupportedExtensions => [".custom"];

    public bool CanRead(string fileName) =>
        Path.GetExtension(fileName).Equals(".custom", StringComparison.OrdinalIgnoreCase);

    // Stage 0: Parse document structure (page count, metadata)
    public Task<ReadResult> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        return Task.FromResult(new ReadResult
        {
            File = new SourceFileInfo
            {
                Name = fileInfo.Name,
                Extension = fileInfo.Extension,
                Size = fileInfo.Length
            },
            ReaderType = "CustomReader"
        });
    }

    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Stream reading not supported.");

    // Stage 1: Extract raw text content
    public async Task<RawContent> ExtractAsync(
        string filePath,
        ExtractOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        return new RawContent
        {
            Text = content,
            File = new SourceFileInfo
            {
                Name = fileInfo.Name,
                Extension = fileInfo.Extension,
                Size = fileInfo.Length
            },
            ReaderType = "CustomReader"
        };
    }

    public Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Stream extraction not supported.");
}

// Register
services.AddTransient<IDocumentReader, CustomDocumentReader>();
```

### Custom AI Service

```csharp
public class CustomTextCompletionService : IDocumentAnalysisService
{
    public async Task<string> GenerateAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Implement your LLM integration
        // Examples: OpenAI, Anthropic, Azure OpenAI, local models

        await Task.Delay(100, cancellationToken);
        return "Generated response";
    }
}

// Register
services.AddScoped<IDocumentAnalysisService, CustomTextCompletionService>();
```

## Related Documentation

- [Architecture](ARCHITECTURE.md) - System design and pipeline documentation
- [Changelog](../CHANGELOG.md) - Version history and release notes
- [GitHub Repository](https://github.com/iyulab/FileFlux)
- [NuGet Package](https://www.nuget.org/packages/FileFlux)
