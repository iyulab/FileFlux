# FileFlux

> .NET document processing library for RAG systems

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## Overview

FileFlux is a .NET library that transforms various document formats into optimized chunks for RAG (Retrieval-Augmented Generation) systems.

### Key Features

- **Multiple Document Formats**: PDF, DOCX, XLSX, PPTX, Markdown, HTML, TXT, JSON, CSV
- **Flexible Chunking Strategies**: Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize, Hierarchical, PageLevel
- **Local AI Processing**: Built-in LocalAI support for embeddings, text generation, captioning, and OCR
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
| Chunking Strategies | ❌ | ✅ |
| FluxCurator & FluxImprover | ❌ | ✅ |
| LocalAI Integration | ❌ | ✅ |
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

### Local AI Processing

FileFlux includes **built-in local AI capabilities** via [LocalAI](https://github.com/iyulab/local-ai), providing embeddings, text generation, image captioning, and OCR without external API calls.

#### Full LocalAI Integration

```csharp
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Register FileFlux with all LocalAI services
services.AddFileFluxWithLocalAI();

// Or with custom configuration
services.AddFileFluxWithLocalAI(options =>
{
    options.UseGpuAcceleration = true;           // DirectML, CUDA, CoreML
    options.EmbeddingModel = "default";          // all-MiniLM-L6-v2
    options.GeneratorModel = "microsoft/Phi-4-mini-instruct-onnx";
    options.WarmupOnInit = true;                 // Preload models
});

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();
```

#### Selective Service Registration

```csharp
// Register only the services you need
services.AddLocalAIEmbedder();    // IEmbeddingService
services.AddLocalAIGenerator();   // ITextCompletionService
services.AddLocalAICaptioner();   // IImageToTextService (captions)
services.AddLocalAIOcr();         // IImageToTextService (OCR)
```

#### Semantic Similarity

```csharp
var embeddingService = provider.GetRequiredService<IEmbeddingService>();

// Generate embeddings for semantic search
var queryEmb = await embeddingService.GenerateEmbeddingAsync(
    "machine learning algorithms",
    EmbeddingPurpose.SemanticSearch);

var docEmb = await embeddingService.GenerateEmbeddingAsync(
    "AI models learn patterns from data",
    EmbeddingPurpose.SemanticSearch);

// Calculate cosine similarity
var similarity = embeddingService.CalculateSimilarity(queryEmb, docEmb);
// Returns ~0.7 for related content
```

#### LocalAI Services

| Service | Interface | Description |
|---------|-----------|-------------|
| Embedder | `IEmbeddingService` | Local embedding generation (384/768 dimensions) |
| Generator | `ITextCompletionService` | Local text generation with Phi models |
| Captioner | `IImageToTextService` | Image captioning for visual content |
| OCR | `IImageToTextService` | Text extraction from images |

**Features:**
- **Auto-download**: Models downloaded automatically on first use
- **GPU Support**: CUDA, DirectML (Windows), CoreML (macOS)
- **Thread-Safe**: Concurrent access with lazy initialization
- **Multi-language OCR**: Support for English, Korean, Chinese, Japanese
- **Zero API Costs**: All processing runs locally

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

📖 **Documentation:**
- [ITextCompletionService Integration Guide](docs/integration/text-completion-service.md) - Implementing the interface
- [Mock Implementations](docs/testing/mock-implementations.md) - Testing reference
- [Community Implementations](docs/community/implementations.md) - Ready-to-use packages (OpenAI, Azure, Anthropic, etc.)

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

📖 **[Quality Analysis Guide](docs/features/quality-analysis.md)** - Comprehensive quality metrics and optimization

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

📖 **[Dependency Injection Patterns](docs/configuration/dependency-injection.md)** - Service registration and lifecycle management

## Documentation

- [**Tutorial**](docs/TUTORIAL.md) - Detailed usage guide
- [**Architecture**](docs/ARCHITECTURE.md) - System design document

### Integration Guides
- [ITextCompletionService Integration](docs/integration/text-completion-service.md) - AI service implementation
- [Dependency Injection Patterns](docs/configuration/dependency-injection.md) - Service registration

### Features
- [Quality Analysis](docs/features/quality-analysis.md) - RAG optimization and benchmarking

### Testing & Development
- [Mock Implementations](docs/testing/mock-implementations.md) - Testing without AI services

### Community
- [Community Implementations](docs/community/implementations.md) - Third-party AI integrations

## Project Structure

```
FileFlux/
├── src/
│   ├── FileFlux.Core/               # Extraction only (zero AI dependencies)
│   └── FileFlux/                    # Full RAG pipeline with LocalAI
├── cli/                             # CLI for local testing (not published)
├── tests/
│   └── FileFlux.Tests/              # Test suite
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
