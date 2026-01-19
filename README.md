# FileFlux

> .NET document processing library for RAG systems

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## Overview

FileFlux is a .NET library that transforms various document formats into optimized chunks for RAG (Retrieval-Augmented Generation) systems.

### Key Features

- **4-Stage Stateful Pipeline**: Extract → Refine → Chunk → Enrich with explicit state management
- **Multiple Document Formats**: PDF, DOCX, XLSX, PPTX, Markdown, HTML, TXT, JSON, CSV
- **Flexible Chunking Strategies**: Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize, Hierarchical, PageLevel
- **Interface-Driven AI**: Define AI service interfaces, implement with your preferred provider
- **Document Graph**: Inter-chunk relationship tracking with sequential, hierarchical, and semantic edges
- **Structural Metadata**: HeadingPath, page numbers, ContextDependency scores for enhanced RAG
- **Language Detection**: Automatic language detection using NTextCat
- **IEnrichedChunk Interface**: Standardized interface for RAG system integration
- **Metadata Enrichment**: AI-powered metadata extraction with caching and fallback
- **Extensible Architecture**: Interface-based design for easy customization
- **Async Processing**: Streaming and parallel processing for large documents

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
| AI Service Interfaces | ✅ | ✅ |
| Chunking Strategies | ❌ | ✅ |
| FluxCurator & FluxImprover | ❌ | ✅ |
| DocumentProcessor | ❌ | ✅ |
| Use Case | Custom chunking | Full RAG pipeline |

## Quick Start

### Basic Usage

```csharp
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Optional: Register AI services for advanced features
// services.AddScoped<ITextCompletionService, YourLLMService>();

// Register FileFlux services (no logger required)
services.AddFileFlux();

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();

// Process document
var chunks = await processor.ProcessAsync("document.pdf");

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.Index}: {chunk.Content}");
}
```

### Streaming Processing

```csharp
await foreach (var result in processor.ProcessStreamAsync("document.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"Chunk {chunk.Index}: {chunk.Content.Length} chars");
        }
    }
}
```

### Chunking Options

```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",      // Automatic strategy selection
    MaxChunkSize = 512,     // Maximum chunk size
    OverlapSize = 64        // Overlap between chunks
};

var chunks = await processor.ProcessAsync("document.pdf", options);
```

### Stateful Pipeline (v0.9.0+)

The new stateful pipeline provides explicit control over each processing stage:

```csharp
using FileFlux;
using FileFlux.Infrastructure.Factories;

// Create processor via factory
var factory = provider.GetRequiredService<IDocumentProcessorFactory>();
using var processor = factory.Create("document.pdf");

// Execute stages explicitly
await processor.ExtractAsync();   // Stage 1: Raw content extraction
await processor.RefineAsync();    // Stage 2: Text cleaning, structure analysis
await processor.ChunkAsync();     // Stage 3: Content chunking
await processor.EnrichAsync();    // Stage 4: LLM-powered enrichment (optional)

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

// Access the document graph
if (processor.Result.Graph != null)
{
    Console.WriteLine($"Graph nodes: {processor.Result.Graph.NodeCount}");
    Console.WriteLine($"Graph edges: {processor.Result.Graph.EdgeCount}");
}
```

**Pipeline Stages**:
| Stage | Interface | Description |
|-------|-----------|-------------|
| Extract | `IDocumentReader` | Raw content extraction from files |
| Refine | `IDocumentRefiner` | Text cleaning, normalization, structure analysis |
| Chunk | `IChunkerFactory` | Content segmentation with various strategies |
| Enrich | `IDocumentEnricher` | LLM-powered summaries, keywords, contextual text |

### Metadata Enrichment

```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",
    MaxChunkSize = 512,
    CustomProperties = new Dictionary<string, object>
    {
        ["enableMetadataEnrichment"] = true,
        ["metadataSchema"] = MetadataSchema.General
    }
};

var chunks = await processor.ProcessAsync("document.pdf", options);

// Access enriched metadata
foreach (var chunk in chunks)
{
    var keywords = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_keywords");
    var description = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_description");
    var documentType = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_documentType");
    var language = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_language");
}
```

### AI Service Interfaces

FileFlux defines AI service interfaces - consumer applications provide implementations.

#### Available Interfaces

| Interface | Purpose | Example Implementations |
|-----------|---------|------------------------|
| `ITextCompletionService` | Text generation, intelligent chunking | OpenAI, Anthropic, LMSupply |
| `IImageToTextService` | Image captioning, OCR | OpenAI Vision, LMSupply Captioner/OCR |
| `IEmbeddingService` | Embedding generation | OpenAI, LMSupply Embedder |

#### Example: Custom AI Provider

```csharp
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Implement your own AI service
services.AddScoped<ITextCompletionService, YourOpenAIService>();
services.AddScoped<IImageToTextService, YourVisionService>();
services.AddScoped<IEmbeddingService, YourEmbeddingService>();

// Register FileFlux
services.AddFileFlux();

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();
```

#### Local AI with LMSupply (CLI Example)

For local AI processing without external API calls, see [LMSupply](https://github.com/iyulab/lm-supply). The FileFlux CLI demonstrates LMSupply integration:

```csharp
// Example from FileFlux.CLI - local AI processing
var lmSupplyOptions = new LMSupplyOptions
{
    UseGpuAcceleration = true,
    EmbeddingModel = "default",
    GeneratorModel = "microsoft/Phi-4-mini-instruct-onnx"
};

// Create LMSupply service implementations
var embedder = await LMSupplyEmbedderService.CreateAsync(lmSupplyOptions);
var generator = await LMSupplyGeneratorService.CreateAsync(lmSupplyOptions);

// Register as AI service implementations
services.AddSingleton<IEmbeddingService>(embedder);
services.AddSingleton<ITextCompletionService>(generator);
services.AddFileFlux();
```

**Note**: LMSupply is not a direct dependency of FileFlux. Consumer applications that need local AI should reference LMSupply packages directly.

## Supported Document Formats

| Format | Extension | Features |
|--------|-----------|----------|
| PDF | .pdf | Text and image extraction |
| Word | .docx | Style and structure preservation |
| Excel | .xlsx | Multi-sheet and table structure |
| PowerPoint | .pptx | Slide and notes extraction |
| Markdown | .md | Structure preservation |
| HTML | .html, .htm | Web content extraction |
| Text | .txt, .json, .csv | Basic text processing |

## Known Limitations

### PDF Processing
- **Vector Graphics Tables**: Tables created with drawing primitives (lines/rectangles) instead of text layout may not be detected. These are rendered as images in most PDF viewers.
- **Complex Multi-column Layouts**: Documents with intricate multi-column arrangements may have suboptimal text ordering.
- **Scanned Documents**: OCR is not included; scanned PDFs require pre-processing with external OCR tools.

### Table Extraction
FileFlux uses layout-based table detection with confidence scoring:
- Tables with confidence score ≥ 0.5 are converted to Markdown format
- Low-confidence tables fall back to plain text to prevent garbled output
- Table quality metrics are exposed via `StructuralHints` for consumer applications

### Document-Specific Notes
- **Excel**: Very large worksheets (>100K rows) may impact memory usage
- **PowerPoint**: Embedded objects are extracted as placeholder text
- **HTML**: JavaScript-rendered content is not supported

## Chunking Strategies

| Strategy | Use Case |
|----------|----------|
| Auto | Automatic selection based on document type (recommended) |
| Smart | Legal, medical, academic documents |
| Intelligent | Technical documentation, API docs |
| Semantic | General documents, papers |
| Paragraph | Markdown, blogs |
| FixedSize | When uniform size is required |

## AI Service Integration

FileFlux defines interfaces while implementation is up to the consumer application.

```csharp
// Optional: Register AI services for advanced features
// - ITextCompletionService: For intelligent chunking and metadata enrichment
// - IImageToTextService: For multimodal document processing
services.AddScoped<ITextCompletionService, YourLLMService>();
services.AddScoped<IImageToTextService, YourVisionService>();

// Register FileFlux services (works without AI services too)
services.AddFileFlux();
```

**Note**: Logger registration is optional. FileFlux uses NullLogger internally if no logger is provided.

For AI service implementation examples, see the `samples/` directory.

## Advanced Features

### 🤖 AI Integration (Optional)

FileFlux defines interfaces - YOU implement them with your preferred AI provider.

```csharp
// Register your AI service implementation
services.AddScoped<ITextCompletionService, YourAIService>();
services.AddFileFlux();
```

**Features enabled with AI services:**
- Intelligent structure analysis for optimal chunking
- Semantic content summarization
- AI-powered quality assessment
- Q&A benchmark generation for RAG testing

📖 See [Tutorial](docs/TUTORIAL.md) for AI service implementation examples.

### 📊 Quality Analysis

Evaluate and optimize chunking quality for RAG systems:

```csharp
var analyzer = serviceProvider.GetRequiredService<IDocumentQualityAnalyzer>();

// Analyze document quality
var report = await analyzer.AnalyzeQualityAsync("document.pdf");
Console.WriteLine($"Quality Score: {report.OverallQualityScore:P2}");

// Generate Q&A benchmark for RAG testing
var benchmark = await analyzer.GenerateQABenchmarkAsync("document.pdf", questionCount: 20);

// Compare different chunking strategies
var strategies = new[] { "Intelligent", "Semantic", "Smart" };
var comparison = await analyzer.BenchmarkChunkingAsync("document.pdf", strategies);
```

📖 See [Architecture](docs/ARCHITECTURE.md) for quality analysis details.

### 🔧 Dependency Injection

FileFlux works with or without AI services:

```csharp
// Minimal setup (no AI)
services.AddFileFlux();

// With AI service
services.AddScoped<ITextCompletionService, YourAIService>();
services.AddFileFlux();

// Environment-specific configuration
if (Environment.IsDevelopment())
    services.AddScoped<ITextCompletionService, MockTextCompletionService>();
else
    services.AddScoped<ITextCompletionService, ProductionAIService>();

services.AddFileFlux();
```

📖 See [Tutorial](docs/TUTORIAL.md) for more DI patterns and examples.

## Documentation

- [**Tutorial**](docs/TUTORIAL.md) - Detailed usage guide and examples
- [**Architecture**](docs/ARCHITECTURE.md) - System design and pipeline documentation
- [**Changelog**](CHANGELOG.md) - Version history and release notes

## Project Structure

```
FileFlux/
├── src/
│   ├── FileFlux.Core/               # Extraction only (zero AI dependencies)
│   │   ├── Contracts/               # IDocumentProcessor, ProcessingResult
│   │   ├── Core/                    # IDocumentRefiner, IDocumentEnricher
│   │   └── Domain/                  # DocumentGraph, RefinedContent, StructuredElement
│   └── FileFlux/                    # Full RAG pipeline (interface-driven)
│       └── Infrastructure/          # StatefulDocumentProcessor, DocumentRefiner, DocumentEnricher
├── cli/                             # CLI with LMSupply integration (not published)
│   └── FileFlux.CLI/
│       └── Services/LMSupply/       # LMSupply service implementations
├── tests/
│   └── FileFlux.Tests/              # Test suite (343+ tests)
└── samples/
    └── FileFlux.SampleApp/          # Usage examples
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
