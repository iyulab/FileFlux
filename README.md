# FileFlux

> .NET document processing library for RAG systems

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## Overview

FileFlux is a .NET library that transforms various document formats into optimized chunks for RAG (Retrieval-Augmented Generation) systems. Built on high-performance Rust FFI libraries for document parsing.

### Key Features

- **5-Stage Stateful Pipeline**: Extract → Rule-Refine → LLM-Refine → Chunk → Enrich
- **Native Document Readers**: Rust FFI-based readers (Unpdf, Undoc, Unhwp) for 2-5x faster processing. Binaries are NuGet-pinned for reproducibility; runtime self-update from GitHub releases is opt-in (off by default — set `UndocNativeLoader.AutoUpdateEnabled = true` / `UnhwpNativeLoader.AutoUpdateEnabled = true` or the `FILEFLUX_NATIVE_AUTOUPDATE=1` environment variable)
- **Multiple Document Formats**: PDF, DOCX, XLSX, PPTX, HWP, HWPX, Markdown, HTML, TXT, JSON, CSV
- **Chunking Strategies**: Auto, Sentence, Paragraph, Token, Hierarchical, Semantic (`ChunkingStrategies` — see [Chunking Strategies](#chunking-strategies))
- **Interface-Driven AI**: Define AI service interfaces, implement with your preferred provider
- **Document Graph**: Inter-chunk relationship tracking with sequential, hierarchical, and semantic edges
- **Structural Metadata**: HeadingPath, page numbers, ContextDependency scores for enhanced RAG
- **Language Detection**: Automatic language detection using NTextCat
- **Document Metadata Extraction** (standalone service, not a pipeline stage): `AIMetadataEnricher` — `EnrichAsync(content, MetadataSchema)` returns topics, keywords, a description and schema-specific fields (General, ProductManual, TechnicalDoc) through your `IDocumentAnalysisService`, with a rule-based fallback and a content cache. Construct it with a `RuleBasedMetadataExtractor` and an `IMemoryCache` and call it on the text you want described
- **IEnrichedChunk Interface**: Standardized interface for RAG system integration
- **Extensible Architecture**: Interface-based design for easy customization
- **Async Processing**: Streaming and parallel processing for large documents
- **Trimmed, Native AOT and file-based apps**: `FileFlux.Core` and `FileFlux` are `IsAotCompatible` — nothing in them needs reflection-based JSON. `FileSystemOutputWriter` (the CLI's disk output) writes UTF-8 JSON without a byte order mark; a `Props` value of a type it does not know is serialized by reflection where the app allows it and written as its `ToString()` text where it does not

## Installation

### Full RAG Pipeline
```bash
dotnet add package FileFlux
```

### Extraction Only (Minimal Dependencies)
```bash
dotnet add package FileFlux.Core
```

**Package Comparison**:
| Feature | FileFlux.Core | FileFlux |
|---------|---------------|----------|
| Document Readers (PDF, DOCX, etc.) | ✅ | ✅ |
| Core Interfaces & Models | ✅ | ✅ |
| AI Service Interfaces (`IDocumentAnalysisService`, `IImageToTextService`, `IAudioToTextService`, `IEmbeddingService`) | ❌ | ✅ |
| Chunking Strategies | ❌ | ✅ |
| FluxCurator & FluxImprover | ❌ | ✅ |
| DocumentProcessor | ❌ | ✅ |
| Use Case | Custom chunking | Full RAG pipeline |

`FileFlux.Providers.LMSupply` adds local AI implementations of those interfaces ([below](#local-ai-with-lmsupply-v0200)), and
`FileFlux.CLI` is a command-line tool over the same pipeline (`dotnet tool install -g FileFlux.CLI`).

## Quick Start

### Basic Usage

`AddFileFlux()` registers an `IDocumentProcessorFactory`. A processor handles one document: create it from a path (or a
`Stream`/`byte[]` with its extension), run the pipeline, and read the chunks from `Result` — which is itself the chunk
sequence.

```csharp
using FileFlux;
using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFileFlux();   // no logger or AI service required
using var provider = services.BuildServiceProvider();

var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");

await processor.ProcessAsync();   // Extract → Refine → (LLM refine, skipped without an AI service) → Chunk

foreach (var chunk in processor.Result)
{
    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.Content}");
}
```

> **Clean chunk content.** Before a chunk is surfaced, FileFlux removes **all HTML comments**
> (`<!-- ... -->`) from `chunk.Content`. This covers the internal structural markers FileFlux emits and
> consumes during boundary detection (`<!-- HEADING_START:H2 -->`, `<!-- TABLE_START -->`,
> `<!-- DOCUMENT_IMAGES_START -->`, etc.), so consumers no longer need their own marker-removal step.
> Note that any HTML comment authored in your source document is also stripped from chunk content. When a
> chunk begins with a heading marker, its level (1-6) is preserved in
> `chunk.Props[ChunkPropsKeys.HierarchyHeadingLevel]`.
>
> Markdown **link reference definitions** (`[label]: https://…`) are likewise excluded from chunk
> content — they are document metadata, not body text. The referencing link keeps its display text and
> resolved target inline, so no content is lost.
>
> _Known limitation:_ if a chunker splits a marker across a chunk boundary (e.g. `<!-- HEADING_ST` |
> `ART:H1 -->`), neither half matches and both leak — identical to a downstream `<!--.*?-->` regex.

### Streaming Processing

`ProcessStreamAsync` yields chunks as they are produced; `Result.Chunks` holds all of them once the enumeration ends.

```csharp
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");

await foreach (var chunk in processor.ProcessStreamAsync())
{
    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.Content.Length} chars");
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
        Strategy = ChunkingStrategies.Auto,   // see Chunking Strategies below
        MaxChunkSize = 512,                   // maximum chunk size (default 1024)
        OverlapSize = 64                      // overlap between chunks (default 128)
    }
});
```

### Stateful Pipeline (v0.9.0+)

The new stateful pipeline provides explicit control over each processing stage:

```csharp
using FileFlux.Core;

// Create processor via factory
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
using var processor = factory.Create("document.pdf");

// Execute stages explicitly
await processor.ExtractAsync();     // Stage 1: Raw content extraction
await processor.RefineAsync();      // Stage 2: Rule-based text cleaning
await processor.LlmRefineAsync();   // Stage 3: LLM-powered refinement (optional)
await processor.ChunkAsync();       // Stage 4: Content chunking
await processor.EnrichAsync();      // Stage 5: LLM-powered enrichment (optional)

// Access results at each stage
Console.WriteLine($"State: {processor.State}");
Console.WriteLine($"Raw text length: {processor.Result.Raw?.Text.Length}");
Console.WriteLine($"Sections found: {processor.Result.Refined?.Sections.Count}");
Console.WriteLine($"Chunks created: {processor.Result.Chunks?.Count}");

// Or run full pipeline at once
await processor.ProcessAsync(new ProcessingOptions
{
    IncludeEnrich = true,
    Enrich = new EnrichOptions { BuildGraph = true }
});

// Text you extracted earlier and stored: start at the Extracted stage, keep the source locations
using var stored = factory.Create(new RawContent
{
    Text = storedText,
    Spans = storedSpans,   // SourceSpan(start, end) { Page = 3 } / { StartTime = …, EndTime = … }
    File = new SourceFileInfo { Name = "report.md", Extension = ".md" },
});
await stored.ChunkAsync();         // each chunk's Location.StartPage/EndPage/StartTime/EndTime comes from the spans

// Access the document graph
if (processor.Result.Graph != null)
{
    Console.WriteLine($"Graph nodes: {processor.Result.Graph.NodeCount}");
    Console.WriteLine($"Graph edges: {processor.Result.Graph.EdgeCount}");
}
```

**Pipeline Stages**:
| Stage | Interface | AI | Description |
|-------|-----------|:--:|-------------|
| Extract | `IDocumentReader` | ❌ | Raw content extraction from files |
| Rule-Refine | `IDocumentRefiner` | ❌ | Text cleaning, normalization, structure analysis |
| LLM-Refine | `ILlmRefiner` | ✅ | AI-powered noise removal, sentence restoration |
| Chunk | `IChunkerFactory` | Optional | Content segmentation with various strategies |
| Enrich | `IDocumentEnricher` | ✅ | LLM-powered summaries, keywords, contextual text |

**Page-scoped, checked LLM refinement.** By default `LlmRefineAsync` rewrites the whole document in one pass, and the
page spans are lost. With `Scope = LlmRefineScope.Pages`, each selected page goes to the refiner alone. An output is kept
only if it keeps `MinNativeCoverage` (0.95) of the page's text and, with `RequireSameNumbers`, exactly its numbers.
Coverage finds each of the page's words in the output however it is spaced, so joining words a line wrap split
(«대응하 여» → «대응하여») keeps everything while a dropped or reworded passage does not. Otherwise the page keeps its
text. The page spans are re-expressed over the result, so chunks still carry their pages. `Result.LlmRefined.Pages`
lists every page's outcome (`Refined` / `Native` / `Rejected` / `Skipped`), its reason, both coverage views
(`NativeCoverage`, and `TokenCoverage` by whitespace-separated words), the refiner's `Passes` (`LlmRefinementPass`:
`NotNeeded` / `Kept` / `Applied` / `Failed` with `truncated` · `context_too_small` · `empty_output` · `error`) and its
`Notes`. A `Native` page says why the model did not change it: `no_pass_needed` (no call was made) or `passes_failed`
(every call failed) — no reason means the model kept the page.
`SelectPages` picks pages from the reader's page record (`RawContent.Quality.Pages`).

```csharp
await processor.LlmRefineAsync(new LlmRefineOptions
{
    Scope = LlmRefineScope.Pages,
    SelectPages = page => page.ReplacementCharacters > 0 || page.SuppressedTextRuns > 0,
    MaxPageCharacters = 12_000,   // a longer page is skipped, not cut
});
foreach (var page in processor.Result.LlmRefined!.Pages)
    Console.WriteLine($"page {page.Page}: {page.Outcome} {page.Reason}");
```

### AI Service Interfaces

FileFlux defines AI service interfaces - consumer applications provide implementations.

#### Available Interfaces

| Interface | Purpose | Example Implementations |
|-----------|---------|------------------------|
| `IDocumentAnalysisService` | Text generation, intelligent chunking. Override `GenerateAsync(prompt, GenerationSettings, ct)` so `LlmRefineOptions`/`ParsingOptions` `Temperature`/`MaxTokens` reach your model, and throw `GenerationTruncatedException` (or, from a service on the shared completion port, its base `Flux.Abstractions.TextCompletionTruncatedException`) when the model stops at the token limit so a cut-off rewrite is never adopted. Set `ProviderInfo.MaxContextLength` and the refiner will not send a pass that cannot fit | OpenAI, Anthropic, LMSupply |
| `IImageToTextService` | Image captioning, OCR | OpenAI Vision, LMSupply Captioner/OCR |
| `IAudioToTextService` | Speech transcription — makes audio files readable (0.30.0+); without one, audio is unsupported | LMSupply Transcriber |
| `IEmbeddingService` | Embedding generation for your own use. The pipeline does not consume it yet — `Semantic` chunking takes FluxCurator's `IEmbedder` (see [Chunking Strategies](#chunking-strategies)) | OpenAI, LMSupply Embedder |

`OpenAICompatibleDocumentAnalysisService` (in `FileFlux`) is a ready `IDocumentAnalysisService` for OpenAI, Azure OpenAI,
Ollama and other OpenAI-compatible endpoints.

#### Example: Custom AI Provider

```csharp
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Your implementations, registered before or after AddFileFlux() — the pipeline resolves them when it runs
services.AddSingleton<IDocumentAnalysisService>(myAnalysisService);   // LLM refine, enrichment
services.AddSingleton<IImageToTextService>(myVisionService);          // images inside documents

services.AddFileFlux();
```

`AddFileFlux(ServiceLifetime.Singleton)` registers the pipeline with the lifetime of a singleton or hosted consumer (default
`Scoped`); `AddDocumentReader<T>()` / `AddDocumentParser<T>()` add your own reader or parser for a format — an added reader wins
over the built-in one for its extensions, registered before or after `AddFileFlux()` (`AddNativeOfficeReader()` uses this for the
native DOCX/XLSX/PPTX reader).

#### Local AI with LMSupply (v0.20.0+)

For local AI processing without external API calls or keys, reference the
[`FileFlux.Providers.LMSupply`](https://github.com/iyulab/FileFlux) package (built on
[LMSupply](https://github.com/iyulab/lm-supply)) — it mirrors the
`FluxIndex.Providers.LMSupply` convention used elsewhere in the ecosystem:

```bash
dotnet add package FileFlux.Providers.LMSupply
```

```csharp
using FileFlux;
using FileFlux.Providers.LMSupply.Extensions;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddLMSupplyDocumentAnalysis();          // "default": LMSupply picks a GGUF model for this host
services.AddLMSupplyEmbedding("default");        // an IEmbeddingService for your own use (not read by the pipeline)
services.AddLMSupplyCaptioner();   // or AddLMSupplyOcr() for scanned/text-bearing images
services.AddLMSupplyTranscriber(); // .wav/.mp3 become readable; chunks carry Location.StartTime/EndTime
// services.AddLMSupplyTranscriber(configure: o => { o.Diarize = true; o.NumSpeakers = 3; }); // label who said what

services.AddFileFlux();
```

An ONNX Runtime GenAI model id (for example `microsoft/Phi-4-mini-instruct-onnx`) also needs the
`LMSupply.Generator.Onnx` package, registered with `OnnxGeneratorBackend.Register()` at startup.

`LMSupplyServiceFactory` (also in this package) offers lazy/cached service creation with
download-progress reporting for interactive apps like the FileFlux CLI, which consumes this
package the same way rather than carrying its own copy.

**Note**: `FileFlux.Providers.LMSupply` is a separate, optional package — `FileFlux` itself has no
LMSupply dependency. Fully custom providers (Example above) remain the way to integrate any other
AI backend.

## Supported Document Formats

| Format | Extension | Reader | Features |
|--------|-----------|--------|----------|
| PDF | .pdf | Unpdf (Rust FFI) | Text, tables, image extraction (each image with its `PageNumber`) |
| Word | .docx, .doc | Undoc (Rust FFI) | Style and structure preservation; Word 97-2003 `.doc` since 0.50.0 (a Word 6.0/95 file fails with `extraction_error_kind=UnsupportedFormat`) |
| Excel | .xlsx | Undoc (Rust FFI) | Multi-sheet and table structure |
| Excel (legacy) | .xls | Built-in (ExcelDataReader) | BIFF binary workbooks; per-sheet tables (text + `RawContent.Tables`); CP949 (EUC-KR) fallback for codepage-less BIFF5/7 |
| PowerPoint | .pptx, .ppt | Undoc (Rust FFI) | Slide and notes extraction; PowerPoint 97-2003 `.ppt` since 0.50.0; each image with the slide that shows it (`PageNumber`, since 0.50.1; every slide for a reused picture in `PageNumbers`) |
| HWP | .hwp, .hwpx | Unhwp (Rust FFI) | Native Korean document support |
| Markdown | .md | Built-in | Structure preservation |
| HTML | .html, .htm | Built-in | Web content extraction |
| CSV/TSV | .csv, .tsv | Built-in (CsvHelper) | Header-aware markdown table serialization; UTF-8/BOM + CP949 (EUC-KR) fallback decoding |
| Text | .txt, .json | Built-in | Basic text processing |
| Audio | .wav, .mp3 | `IAudioToTextService` (e.g. `AddLMSupplyTranscriber()`) | Speech as text, one paragraph per segment (speaker-labelled when the service separates speakers); chunks carry `Location.StartTime`/`EndTime`. Unsupported when no service is registered |

> **Picture alt text is content only when an author wrote it** (Word, PowerPoint, Excel, HWP) — that text reaches the
> body (`![alt](…)`) and `ImageInfo.Caption`, on one line. A file path or bare file name Office recorded when the picture
> was inserted is dropped. A description Office generated (ending with its «AI-generated content may be incorrect»
> disclaimer, or the older «Description automatically generated») stays out of the body and is exposed as
> `ImageInfo.Properties["generated_alt_text"]`, without the disclaimer.

> **The name is a claim, the content decides (since 0.35.0)** — when a file's name selects the wrong
> reader, or none, reader selection consults the content: a PDF (`%PDF-` header), an OOXML
> package (`.docx` / `.xlsx` / `.pptx`, told apart by the package's part folders), an HWPX
> package (its `mimetype` entry), or a compound file whose directory holds an HWP 5 document, a
> legacy workbook (since 0.36.0), or a Word or PowerPoint 97-2003 document (since 0.50.0) is read by
> the reader for what it is. A browser's "Save as PDF"
> kept under a `.docx` name, an `.hwp` sent as `.doc`, or a download named `.bin`, extracts instead
> of failing. The result says so: `RawContent.File.Extension` is the format that was parsed, the
> `declared_extension` hint keeps the name's claim, and a warning carries `[extension_mismatch]`.
> Content the detector cannot tell apart (text, HTML, an encrypted Office file, a damaged package)
> keeps the declared reader. A reader handed content another reader parses — a workbook under a
> `.docx` name, given to the Word reader directly — fails with `container_mismatch` and
> `detected_extension` rather than returning that content as its own format. `FormatSignature` (`DetectFile` /
> `DetectStream` / `DetectBytes`) is public, and `IDocumentReaderFactory.GetReader(fileName,
> detectedExtension)` selects with it; the PDF, OOXML, HWP and legacy Excel readers accept content
> they parse whatever its name.
>
> **Mislabelled workbooks (since 0.17.0)** — the two Excel readers route on the container's magic
> bytes rather than the declared extension, in both directions: a compound-file (`.xls`) workbook
> named `.xlsx` extracts through the legacy reader, and an OOXML package named `.xls` extracts
> through the OOXML one. `RawContent.File.Extension` reports the container that was actually parsed,
> not the name the file arrived under. Content that is neither container fails with
> `extraction_failure_reason=container_mismatch` instead of the ZIP parser's "could not find EOCD",
> which reads as corruption when the file is simply not a workbook. When the content is a format
> another reader parses, the message names it as `detected_extension` (for example `.pdf`).

## Known Limitations

### PDF Processing
- **Vector Graphics Tables**: Tables created with drawing primitives (lines/rectangles) instead of text layout may not be detected. These are rendered as images in most PDF viewers.
- **Complex Multi-column Layouts**: Documents with intricate multi-column arrangements may have suboptimal text ordering.
- **Scanned Documents**: OCR is not included; scanned PDFs require pre-processing with external OCR tools. When a PDF parses but yields no text — nothing, or only the references to its extracted images (a scanned page renders as `![](page1_Im0.png)`) — the reader sets `Hints["extraction_failure_reason"]` to `"no_text_layer"` (image-only/scanned — pages draw images without a readable text layer, via Unpdf page introspection, including a scan wrapped in a form XObject), `"text_not_extracted"` (a page content stream could not be decoded, so content is missing and the page is neither known blank nor known scanned; `Hints["undecodable_content_streams"]` gives the count, and is also set with a warning when other text did come out), or `"blank_page"` (no text or image content at all), plus an explanatory warning — so consumers can classify these distinctly from parse errors. Text drawn inside form XObjects is extracted like any other text.
- **Page Quality**: `RawContent.Quality.Pages` holds one `PageQuality` per PDF page — its character count and U+FFFD count in `Text`, the parser's text/image/form operator counts, whether an unreadable OCR layer was dropped, and the suppressed text runs and undecodable content streams on that page (`HasTextLayer` = text operators and no dropped OCR layer), and the parser's layout outcome: `ImageCoverage` (share of the page painted by images — a full-page scan is near 1), `ColumnCount`, `ReadingRegions`, `AmbiguousLayoutRegions` (where the reading order had to guess between two columns), `RotatedTextRuns`, `RuledGrids`/`RuledTables` and `Rotation`. Use it to pick the pages you read another way (for example a scanned page: `!p.HasTextLayer && p.ImageCoverage > 0.5`); the thresholds are yours. Other formats leave `Pages` empty.
- **Page Reading** (PDF, opt-in): `ExtractOptions.PageReading = new PageReadingOptions { SelectPages = p => … }` renders each selected page (`Dpi`, default 150; `MaxPages` budget) and reads it through the registered `IImageToTextService`. The read replaces the page's text only where the page could not be read — no text layer, or lost content when the read keeps `MinNativeCoverage` of the page's text (each word, however spaced) — and never from a render that could not paint everything, nor over a page with tables. Every selected page gets a `RawContent.PageReads` entry (`Outcome`, `Reason`, the read `Text`, what the render could not paint), so a kept read is still yours to use. Through a processor: `await processor.ExtractAsync(new ExtractOptions { PageReading = … })` before refining or chunking (the multimodal PDF reader `AddFileFlux` registers does the reading).
- **Partial Extraction**: When whole-document extraction fails, FileFlux automatically falls back to per-page extraction. Pages that cannot be extracted are skipped and recorded in `RawContent.Errors`. `RawContent.Status` is set to `ProcessingStatus.Partial` when some pages fail, allowing RAG pipelines to use the successfully extracted content rather than losing the entire document.
- **Parse Failures**: `extraction_failure_reason` above covers documents that parse but yield no text; `extraction_error_kind` covers documents that fail to parse, naming Unpdf's structured failure classification (`PdfParse`, `UnknownFormat`, `Encrypted`, `Corrupted`, `Io`, `MissingObject`, …) so consumers can classify without matching on message prose. On partial extraction it arrives as `Hints["extraction_error_kind"]`, listing every distinct kind seen across the skipped pages (`"PdfParse+MissingObject"`). When extraction fails entirely there is no `RawContent` to carry hints, so the same value is embedded in the thrown `DocumentProcessingException.Message` as a `extraction_error_kind=<kind>` token:

```csharp
try
{
    var content = await reader.ExtractAsync("scan.pdf");
    if (content.Hints.TryGetValue("extraction_error_kind", out var kind))
        logger.LogWarning("Some pages unreadable: {Kind}", kind);   // e.g. "PdfParse"
}
catch (DocumentProcessingException ex)
{
    // ex.Message contains "... [extraction_error_kind=Corrupted]"
    logger.LogError(ex, "PDF could not be parsed");
}
```

A value from a newer native build passes through as its number rather than being dropped, so unknown kinds stay reportable. Kinds numbered 100 and above are raised at the native library's interop boundary rather than by the document, so a failure carrying one is a library-side problem to report upstream, not a defect in the file — the thrown message says so instead of filing it under parse failure.

- **Incomplete Extraction**: A damaged PDF does not always fail. If part of its page tree cannot be read, the parser recovers the rest and extraction succeeds over a *shorter* document. FileFlux flags that with `Hints["pages_incomplete"] = true` and `RawContent.Status = ProcessingStatus.Partial`, plus a warning — so a page that never arrived is not indexed as a page that never existed. `Hints["declared_page_count"]` carries the count the document declares, to compare against the extracted `page_count`. The flag is deliberately a boolean and never a loss figure: one unresolved page-tree node can cost a single page or a whole subtree, so the number of lost pages is not knowable.

```csharp
var content = await reader.ExtractAsync("damaged.pdf");
if (content.Hints.ContainsKey("pages_incomplete"))
{
    // declared_page_count is absent when the document's own declaration was unreadable —
    // itself a damage signal, so the flag can be set without a number to compare against.
    content.Hints.TryGetValue("declared_page_count", out var declared);
    logger.LogWarning("Indexing an incomplete document: declared {Declared}, extracted {Extracted}",
        declared ?? "unknown", content.Hints["page_count"]);
}
```

`ReadAsync` (stage 0) carries the same signal in `DocumentProps`, with `ReadResult.Status` set to `Partial` — that stage reports the page count, so it is where a short page set most easily passes for a whole document.

- **Suppressed Text Runs**: Some PDFs use fonts whose character codes the decoder cannot resolve — emitting the raw bytes would produce mojibake rather than text, so the decoder discards the run instead. FileFlux surfaces that with `Hints["suppressed_text_runs"]` (the discarded run count) and `RawContent.Status = ProcessingStatus.Partial`, plus a warning. When every run in the document was discarded, the empty result gets `Hints["extraction_failure_reason"] = "text_runs_suppressed"` instead of `"no_text_layer"` — this is not a scanned document, and does not need OCR.

```csharp
var content = await reader.ExtractAsync("broken-font.pdf");
if (content.Hints.TryGetValue("suppressed_text_runs", out var runs))
    logger.LogWarning("Document lost {Runs} text run(s) to an unresolvable font", runs);
```

`ReadAsync` (stage 0) carries the same signal in `DocumentProps`, with `ReadResult.Status` set to `Partial`.

### Tables
Tables come out twice, in step: inline in `RawContent.Text` as GFM tables, and as structured `RawContent.Tables`
(`TableData` — `Cells` grid, `HasHeader`, `MergedCells`, `PageNumber`, `Caption`, `DetectionMethod`, `Confidence`;
for sheets and sections `Props["section_name"]`). Filled by the PDF, Word, PowerPoint, Excel (.xlsx, .xls) and HWP readers
from the table structure (a section with an empty table yields none). The text is authoritative: refinement never writes `Tables` into the text again.
- **Excel** (.xlsx and legacy .xls): the text is written from the table structure (`TableMarkdown`), one `## sheet name` section per sheet, so
  every value under a merged cell keeps its column; each sheet is a `RawContent.Spans` entry (`Page` = sheet position).
- **PDF**: tables are inferred from page layout by the parser — `DetectionMethod = Heuristic`, `Confidence = 0.5`, and
  the header flag is the parser's default rather than a detection.
- `TableMarkdown.Render(table)` renders any `TableData` the same way (merged cells: text in the first position, the
  covered positions empty; the delimiter row is always as wide as the grid).

### Document-Specific Notes
- **Excel**: Very large worksheets (>100K rows) may impact memory usage
- **PowerPoint**: Embedded objects are extracted as placeholder text
- **HTML**: JavaScript-rendered content is not supported

## Chunking Strategies

| Strategy | Output characteristics | Prerequisites |
|----------|------------------------|---------------|
| `Auto` (default) | Resolved to a concrete strategy by content analysis: short text → Sentence, 4+ paragraphs → Paragraph, sentence-structured → Sentence, otherwise Token. The resolved strategy is logged and recorded in each chunk's `Strategy`. | — |
| `Sentence` | Sentence-boundary chunks, language-aware | — |
| `Paragraph` | Paragraph-boundary chunks; best for Markdown/blogs; oversized paragraphs fall back to sentence splits | — |
| `Token` | Token-budget chunks for unstructured text | — |
| `Hierarchical` | Heading-structure-aware chunks | — |
| `Semantic` | Embedding-similarity boundaries | Requires a FluxCurator `IEmbedder` in the container — otherwise chunker creation throws `ArgumentException` |

Structural metadata: every `ProcessAsync`/`ChunkAsync` chunk carries `Location.StartChar/EndChar`
(offsets into the refined text), `Location.HeadingPath`/`Section` (hierarchical heading context,
e.g. `Root Title > Sub Section`), and `Props[ChunkPropsKeys.HierarchyPath]` (`"hierarchy.path"`) — on the streaming `ChunkStreamAsync` too.
`Location.StartPage/EndPage` name the first and last source page of the chunk's text for PDFs (0.30.0+), and
`Location.StartTime/EndTime` carry the source time range for timed sources.

These come from `RawContent.Spans`: a reader describes where each stretch of its text came from (`SourceSpan` —
`Page`, `StartTime`/`EndTime`), refinement carries the spans onto `RefinedContent.Spans`, and chunking writes the
spans each chunk overlaps onto its `Location`. A custom `IDocumentReader` fills `Spans` to get the same. Spans are
dropped (and chunk pages left null) when a step rebuilds the text from scratch — an LLM rewrite.

## Advanced Features

AI services are optional — see [AI Service Interfaces](#ai-service-interfaces). With an `IDocumentAnalysisService` registered,
the LLM refine stage runs (noise removal, sentence restoration) and `EnrichAsync` builds summaries, keywords and the document
graph; without one those stages are skipped. 📖 See [Tutorial](docs/TUTORIAL.md) for AI service implementation examples.

### 📊 Quality Analysis

Evaluate and optimize chunking quality for RAG systems:

```csharp
using FileFlux.Infrastructure.Quality;

// Score the chunks a processing run produced
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
await using var processor = factory.Create("document.pdf");
await processor.ProcessAsync();

var metrics = await ChunkQualityEngine.CalculateQualityMetricsAsync(processor.Result);

Console.WriteLine($"Completeness: {metrics.AverageCompleteness:P0}");
Console.WriteLine($"Boundaries:   {metrics.BoundaryQuality:P0}");
Console.WriteLine($"Size spread:  {metrics.SizeDistribution:P0}");
```

To compare strategies, run `ProcessAsync` once per `ChunkingOptions.Strategy` (a new processor each time) and compare the metrics.

📖 See [Architecture](docs/ARCHITECTURE.md) for quality analysis details.

## Documentation

- [**Tutorial**](docs/TUTORIAL.md) - Detailed usage guide and examples
- [**Architecture**](docs/ARCHITECTURE.md) - System design and pipeline documentation
- [**Changelog**](CHANGELOG.md) - Version history and release notes

## Project Structure

```
FileFlux/
├── src/
│   ├── FileFlux.Core/                 # Extraction only (zero AI dependencies)
│   │   ├── Contracts/                 # IDocumentProcessor, ProcessingResult
│   │   ├── Core/                      # IDocumentRefiner, IDocumentEnricher
│   │   └── Domain/                    # DocumentGraph, RefinedContent, StructuredElement
│   ├── FileFlux/                      # Full RAG pipeline (interface-driven)
│   │   └── Infrastructure/            # StatefulDocumentProcessor, DocumentRefiner, DocumentEnricher
│   └── FileFlux.Providers.LMSupply/   # Optional local-AI provider package (v0.20.0+)
├── cli/                               # CLI (published: `dotnet tool install -g FileFlux.CLI`)
│   └── FileFlux.CLI/
├── tests/
│   └── FileFlux.Tests/                # Test suite (343+ tests)
└── samples/
    └── FileFlux.SampleApp/            # Usage examples
```

## Contributing

1. Create and discuss an issue
2. Work on a feature branch
3. Add/modify tests
4. Submit a pull request

## License

MIT License - See [LICENSE](LICENSE) file

## Support

- **Issue Reports**: [GitHub Issues](https://github.com/iyulab/FileFlux/issues)
- **Feature Requests**: [GitHub Discussions](https://github.com/iyulab/FileFlux/discussions)
