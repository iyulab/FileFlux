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

`ProcessStreamAsync` runs extraction, refinement and chunking, then yields the chunks one at a time. The stages are not incremental, so the first chunk arrives once chunking is done; enumerate to handle chunks one by one (for example, embed each as you read it). `Result.Chunks` holds all of them once the enumeration ends.

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

A processor runs one document through five stages and keeps every stage's output in `Result`. Each stage method first runs
the earlier stages it needs (Extract before Refine, Refine before LlmRefine and Chunk, Chunk before Enrich) and skips a stage
that has already run.

### Creating a Stateful Processor

```csharp
using FileFlux;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFileFlux();
using var provider = services.BuildServiceProvider();

// One processor per document
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");
```

### Stage-by-Stage Execution

```csharp
// Stage 1: extract the raw text (ExtractOptions selects images, page reading and slide rendering)
await processor.ExtractAsync();
Console.WriteLine($"Extracted: {processor.Result.Raw?.Text.Length} chars");

// Stage 2: rule-based refinement
await processor.RefineAsync(new RefineOptions
{
    CleanNoise = true,
    BuildSections = true,
    ExtractStructures = true
});
Console.WriteLine($"Sections: {processor.Result.Refined?.Sections.Count}");

// Stage 3: LLM refinement - skipped when no IDocumentAnalysisService is registered
await processor.LlmRefineAsync();
Console.WriteLine($"LLM used: {processor.Result.LlmWasUsed}");

// Stage 4: chunking (from the LLM-refined text when there is one)
await processor.ChunkAsync(new ChunkingOptions
{
    Strategy = ChunkingStrategies.Auto,
    MaxChunkSize = 512,
    OverlapSize = 64
});
Console.WriteLine($"Chunks: {processor.Result.Chunks?.Count}");

// Stage 5: enrichment and the chunk graph (see below)
await processor.EnrichAsync(new EnrichOptions
{
    BuildGraph = true,
    GenerateSummaries = true,
    ExtractKeywords = true
});
Console.WriteLine($"Graph nodes: {processor.Result.Graph?.NodeCount}");
```

### Full Pipeline Execution

`ProcessAsync` runs Extract, Refine, LlmRefine (unless `IncludeLlmRefine = false`) and Chunk, and Enrich when
`IncludeEnrich` is set. It extracts with the reader's default `ExtractOptions`; to pass your own, call `ExtractAsync` first —
`ProcessAsync` then continues from the extracted text.

```csharp
await processor.ProcessAsync(new ProcessingOptions
{
    Chunking = new ChunkingOptions { Strategy = ChunkingStrategies.Auto, MaxChunkSize = 512 },
    IncludeEnrich = true,
    Enrich = new EnrichOptions { BuildGraph = true }
});

// Every stage's output
var raw = processor.Result.Raw;
var refined = processor.Result.Refined;
var llmRefined = processor.Result.LlmRefined;
var chunks = processor.Result.Chunks;
var graph = processor.Result.Graph;
var timing = processor.Result.Metrics.TotalDuration;
```

### Processing State

```csharp
// Created, Extracted, Refined, LlmRefined, Chunked, Enriched - or Failed, Disposed
Console.WriteLine($"State: {processor.State}");

// On a new processor, ChunkAsync extracts and refines first (it does not run the LLM refinement)
await processor.ChunkAsync();
```

### Document Graph

`EnrichAsync` builds `Result.Graph` when `EnrichOptions.BuildGraph` is true (the default): one node per chunk, and edges
between chunks. Without an AI service the edges are `Sequential` (each chunk to the next, label `follows`) and
`Hierarchical` (from the nearest earlier chunk whose heading path is a shorter prefix of this chunk's, label `contains`). With an
`IDocumentAnalysisService` registered, edges for the relationships the model finds between chunks are added (`Semantic`,
`Reference`, `Contrast`, `Continuation`, `Example`, `Sequential`). At most 10 edges leave one chunk.

```csharp
await processor.EnrichAsync();
var graph = processor.Result.Graph!;

foreach (var node in graph.Nodes)
{
    Console.WriteLine($"Node {node.Index}: {node.ChunkId} ({string.Join(" > ", node.SectionPath)})");
}

foreach (var edge in graph.Edges)
{
    Console.WriteLine($"Edge: {edge.SourceId} -> {edge.TargetId} {edge.Type} ({edge.Label}, weight {edge.Weight})");
}

// The chunks one chunk is connected to, in either direction
var first = graph.Nodes[0].ChunkId;
var neighbours = graph.GetConnectedChunks(first).ToList();
var hierarchy = graph.GetEdgesByType(EdgeType.Hierarchical).Count();
```

### Enrichment Results

With an `IDocumentAnalysisService` registered, `EnrichAsync` writes a summary, keywords and a contextual description of
each chunk into its `Props`, which `DocumentChunk` exposes as typed properties. Up to `EnrichOptions.MaxConcurrency` chunks
are enriched at a time; a chunk whose call fails keeps its other values. Without an analysis service the chunks are left
as they are and only the graph is built.

```csharp
await processor.EnrichAsync(new EnrichOptions
{
    GenerateSummaries = true,
    ExtractKeywords = true,
    AddContextualText = true
});

foreach (var chunk in processor.Result)
{
    if (!chunk.HasEnrichment)
        continue;

    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.EnrichedSummary}");
    Console.WriteLine($"  Keywords: {string.Join(", ", chunk.EnrichedKeywords ?? [])}");
    Console.WriteLine($"  Context: {chunk.EnrichedContextualText}");
}
```

## Document Formats

`AddFileFlux()` registers readers for PDF (`.pdf`), Word (`.docx`, `.doc`), Excel (`.xlsx`, `.xls`), PowerPoint
(`.pptx`, `.ppt`), HWP (`.hwp`, `.hwpx`), Markdown (`.md`, `.markdown`), HTML (`.html`, `.htm`), CSV/TSV (`.csv`, `.tsv`)
and plain text (`.txt`, `.json`), and for audio files when an `IAudioToTextService` is registered. What each reader
extracts, and its limits, are in the README's [Supported Document Formats](../README.md#supported-document-formats) and
[Known Limitations](../README.md#known-limitations). With an `IImageToTextService` registered, the PDF, Word, Excel and
PowerPoint readers also describe the images in a document (see [Multimodal Processing](#multimodal-processing)).

### Extension Discovery

```csharp
var readers = provider.GetRequiredService<IDocumentReaderFactory>();

// Every extension a registered reader takes
var extensions = readers.GetAllReaders()
    .SelectMany(r => r.SupportedExtensions)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Order(StringComparer.OrdinalIgnoreCase);
Console.WriteLine($"Supported: {string.Join(", ", extensions)}");

// Whether a reader takes a file name, and which one
bool isSupported = readers.CanRead("report.pdf");
Console.WriteLine(readers.GetReader("report.pdf")?.ReaderType);
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
the Enrich stage instead (see [Enrichment Results](#enrichment-results)).

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

Register an `IImageToTextService` and the PDF, Word, Excel and PowerPoint readers send the images in a document to it.
Each description that comes back with text is added after the document's own text, so it is refined and chunked with the
rest. The readers describe the images extraction returned (`RawContent.Images`), so a processor over a file path, a
`Stream` or a `byte[]` gets the same descriptions, and the image options decide what is sent:
`ExtractOptions.ExtractImages = false` sends nothing, `MaxImageSize` leaves out a larger image (pass the options to
`processor.ExtractAsync`). Icon-sized pictures (under 100 x 100 pixels) and an image on a PDF page replaced by its `PageReading` read are not sent.
`FileFlux.Providers.LMSupply` has local ones (`AddLMSupplyCaptioner()`, `AddLMSupplyOcr()`); for any other vision model,
implement the interface. The readers pass the image bytes (the `byte[]` overload) and use `ExtractedText`:

```csharp
using System.Threading;

services.AddSingleton<IImageToTextService, MyVisionService>();   // before or after AddFileFlux()

public sealed class MyVisionService : IImageToTextService
{
    public string ProviderName => "my-vision-model";

    public IEnumerable<string> SupportedImageFormats => [".png", ".jpg", ".jpeg"];

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
    {
        var text = await DescribeAsync(imageData, options?.CustomPrompt, cancellationToken);
        return new ImageToTextResult { ExtractedText = text };
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        Stream imageStream, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer, cancellationToken);
        return await ExtractTextAsync(buffer.ToArray(), options, cancellationToken);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        string imagePath, ImageToTextOptions? options = null, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return await ExtractTextAsync(bytes, options, cancellationToken);
    }

    // Call your vision model here
    private static Task<string> DescribeAsync(byte[] image, string? prompt, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
```

The processor is used as before. The extraction result says how many images were described:

```csharp
await using var processor = factory.Create("document-with-images.pdf");
await processor.ProcessAsync();

if (processor.Result.Raw!.Hints.TryGetValue("IncludedImageCount", out var described))
{
    Console.WriteLine($"{described} image description(s) added to the text");
}
```

Scanned PDF pages and drawn PowerPoint slides are read through the same service when you ask for it in `ExtractOptions`
(`PageReading`, `SlideRendering` — see the README's [Known Limitations](../README.md#known-limitations)). Pass the options
to `ExtractAsync` before processing:

```csharp
await using var processor = factory.Create("scanned.pdf");
await processor.ExtractAsync(new ExtractOptions
{
    PageReading = new PageReadingOptions { SelectPages = page => !page.HasTextLayer }
});
await processor.ProcessAsync();   // continues from the extracted text
```

### Quality Analysis

`ChunkQualityEngine` scores a set of chunks:

```csharp
using FileFlux.Infrastructure.Quality;

var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");
await processor.ProcessAsync();

var metrics = await ChunkQualityEngine.CalculateQualityMetricsAsync(processor.Result);
Console.WriteLine($"Average Completeness: {metrics.AverageCompleteness:P}");
Console.WriteLine($"Content Consistency: {metrics.ContentConsistency:P}");
Console.WriteLine($"Boundary Quality: {metrics.BoundaryQuality:P}");
Console.WriteLine($"Size Distribution: {metrics.SizeDistribution:P}");
Console.WriteLine($"Overlap Effectiveness: {metrics.OverlapEffectiveness:P}");
```

## RAG Integration

### Complete RAG Pipeline

FileFlux produces the chunks; embedding them and storing the vectors is yours. `IEmbeddingService` is FileFlux's embedding
interface (`AddLMSupplyEmbedding` registers a local one); the vector store below stands for your own.

```csharp
using System.Threading;

services.AddScoped<DocumentIndexer>();   // with an IEmbeddingService and your IMyVectorStore registered

public interface IMyVectorStore
{
    public Task UpsertAsync(Guid id, string content, float[] vector, IReadOnlyDictionary<string, object> props,
        CancellationToken cancellationToken);
}

public sealed class DocumentIndexer(
    IDocumentProcessorFactory factory, IEmbeddingService embeddings, IMyVectorStore store)
{
    public async Task IndexAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var processor = factory.Create(filePath);
        var options = new ProcessingOptions
        {
            Chunking = new ChunkingOptions { Strategy = ChunkingStrategies.Auto, MaxChunkSize = 512, OverlapSize = 64 }
        };

        await foreach (var chunk in processor.ProcessStreamAsync(options, cancellationToken))
        {
            var vector = await embeddings.GenerateEmbeddingAsync(chunk.Content, EmbeddingPurpose.Storage, cancellationToken);
            await store.UpsertAsync(chunk.Id, chunk.Content, vector, chunk.Props, cancellationToken);
        }
    }
}
```

Besides `Content`, each chunk carries its place in the source (`Location`: character offsets, heading path, pages or times)
and `Props` (see [Chunking Strategies](../README.md#chunking-strategies) in the README) — worth storing next to the vector.

### Batch Processing

A processor handles one document. For several files, create one per file; processors are independent, so the files can
run in parallel:

```csharp
using System.Collections.Concurrent;

string[] files = ["report.pdf", "notes.docx", "data.xlsx"];
var chunkCounts = new ConcurrentDictionary<string, int>();

await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (path, cancellationToken) =>
{
    await using var processor = factory.Create(path);
    await processor.ProcessAsync(cancellationToken: cancellationToken);
    chunkCounts[path] = processor.Result.Chunks?.Count ?? 0;
});

foreach (var (path, count) in chunkCounts)
{
    Console.WriteLine($"{path}: {count} chunks");
}
```

## Error Handling

### Exception Handling

A stage that fails leaves the processor in the `Failed` state and throws:

- FileFlux's own exceptions as themselves — `UnsupportedFileFormatException` for a file no registered reader takes, or a
  reader's `DocumentProcessingException` (its message carries `extraction_error_kind=<kind>` for a PDF that cannot be
  parsed). All of them derive from `FileFluxException`.
- Anything else wrapped in `DocumentProcessingException`: `FileName` is the processor's `FilePath`, the message names the
  stage (`Extraction failed: …`, `Refinement failed: …`, `Chunking failed: …`, `Enrichment failed: …`) and
  `InnerException` holds the cause.

A failed processor has no result to continue from: calling any stage on it again throws `InvalidOperationException` naming
the stage that failed. To try a document again, create a new processor. Cancelling through the `CancellationToken` throws
`OperationCanceledException` and does not fail the processor; the interrupted stage runs again on the next call. A failure
of the LLM refinement does not fail the pipeline: the stage keeps the rule-based text. An unknown `ChunkingOptions.Strategy`
throws `ArgumentException` from `ProcessAsync` before any stage runs.

```csharp
await using var processor = factory.Create("document.pdf");
try
{
    await processor.ProcessAsync();
}
catch (UnsupportedFileFormatException ex)
{
    Console.WriteLine($"Not a supported format: {ex.Message}");
}
catch (FileFluxException ex)
{
    Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    Console.WriteLine($"Cause: {ex.InnerException?.GetType().Name}");
    Console.WriteLine($"State: {processor.State}");   // Failed
}
```

### Streaming Error Handling

`ProcessStreamAsync` throws the same exceptions from the `await foreach`; the chunks yielded before the failure are already
yours:

```csharp
await using var processor = factory.Create("document.pdf");
var received = new List<DocumentChunk>();
try
{
    await foreach (var chunk in processor.ProcessStreamAsync())
    {
        received.Add(chunk);
    }
}
catch (FileFluxException ex)
{
    Console.WriteLine($"Stopped after {received.Count} chunks: {ex.Message}");
}
```

### Validation

The processor picks its reader by the file name and, when the content is recognised as another format, by the content.
`IDocumentReaderFactory` makes the same choice before you create a processor:

```csharp
using FileFlux.Core.Infrastructure.Readers;

var readers = provider.GetRequiredService<IDocumentReaderFactory>();
var path = "upload.bin";

if (!File.Exists(path))
{
    Console.WriteLine("File not found");
}
else if (readers.GetReader(path, FormatSignature.DetectFile(path)) is { } selected)
{
    Console.WriteLine($"{path} is read by {selected.ReaderType}");
}
else
{
    Console.WriteLine($"No reader takes {path}");
}
```

## Customization

### Custom Chunking

The strategies are the `ChunkingStrategies` names (see [Chunking Strategies](#chunking-strategies)); there is no interface
for adding one. To split a document your own way, run the pipeline up to refinement and split the refined text:

```csharp
await using var processor = factory.Create("document.pdf");
await processor.RefineAsync();   // extracts first

var refined = processor.Result.Refined!;
var pieces = refined.Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
Console.WriteLine($"{pieces.Length} pieces, {refined.Sections.Count} sections");
```

### Custom Document Reader

`IDocumentReader` has two stages: `ReadAsync` (Stage 0 - document structure) and `ExtractAsync` (Stage 1 - raw content).
Register it with `AddDocumentReader<T>()`, before or after `AddFileFlux()`; it takes its extensions over a built-in reader.

```csharp
using System.Threading;

services.AddDocumentReader<CustomDocumentReader>();

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
```

To give chunks page or time locations, fill `RawContent.Spans` (see the README's [Chunking Strategies](../README.md#chunking-strategies)).

### Custom AI Service

`OpenAICompatibleDocumentAnalysisService` (namespace `FileFlux.Infrastructure.Services`) is a ready `IDocumentAnalysisService`
for OpenAI, Azure OpenAI, Ollama and other OpenAI-compatible endpoints, and `FileFlux.Providers.LMSupply` has a local one
(`AddLMSupplyDocumentAnalysis()`). For another model, implement the interface. FileFlux calls the two `GenerateAsync`
overloads and reads `ProviderInfo`.

```csharp
using System.Threading;

services.AddSingleton<IDocumentAnalysisService, MyAnalysisService>();   // before or after AddFileFlux()

public sealed class MyAnalysisService : IDocumentAnalysisService
{
    // The LLM refiner does not send a pass longer than MaxContextLength (0: unknown)
    public DocumentAnalysisServiceInfo ProviderInfo { get; } = new() { Name = "my-model", MaxContextLength = 32_768 };

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default) =>
        GenerateAsync(prompt, GenerationSettings.Default, cancellationToken);

    // Pass settings.Temperature and settings.MaxTokens to your model. When it stops at the token limit, throw
    // GenerationTruncatedException so a cut-off rewrite is never used.
    public Task<string> GenerateAsync(string prompt, GenerationSettings settings, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}
```

## Related Documentation

- [Architecture](ARCHITECTURE.md) - System design and pipeline documentation
- [Changelog](../CHANGELOG.md) - Version history and release notes
- [GitHub Repository](https://github.com/iyulab/FileFlux)
- [NuGet Package](https://www.nuget.org/packages/FileFlux)
