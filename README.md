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
- **Local Embeddings**: Built-in LocalEmbedder support with zero configuration
- **Structural Metadata**: HeadingPath, page numbers, ContextDependency scores for enhanced RAG
- **Language Detection**: Automatic language detection using NTextCat
- **IEnrichedChunk Interface**: Standardized interface for RAG system integration
- **Metadata Enrichment**: AI-powered metadata extraction with caching and fallback
- **Extensible Architecture**: Interface-based design for easy customization
- **Async Processing**: Streaming and parallel processing for large documents

## Installation

```bash
dotnet add package FileFlux
```

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

### Local Embeddings

FileFlux includes **built-in local embedding support** via LocalEmbedder, providing high-quality embeddings without external API calls.

#### Zero Configuration

```csharp
// LocalEmbedder is automatically registered - no configuration needed!
services.AddFileFlux();

// Models are auto-downloaded from HuggingFace on first use
var processor = provider.GetRequiredService<IDocumentProcessor>();
var chunks = await processor.ProcessAsync("document.pdf");
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

#### Custom Configuration

```csharp
// Use high-quality models or GPU acceleration
services.AddFileFluxWithLocalEmbedder(options =>
{
    options.AnalysisModel = "all-mpnet-base-v2";      // 768 dimensions
    options.SearchModel = "all-mpnet-base-v2";         // High quality
    options.PrimaryDimension = 768;
    options.Provider = ExecutionProvider.Cuda;         // GPU acceleration
});
```

#### Available Models

| Model | Dimensions | Speed | Quality | Use Case |
|-------|------------|-------|---------|----------|
| `all-MiniLM-L6-v2` | 384 | Fast | Good | Analysis, chunking (default) |
| `all-mpnet-base-v2` | 768 | Medium | High | Semantic search, storage |
| `bge-small-en-v1.5` | 384 | Fast | Good | English documents |
| `bge-base-en-v1.5` | 768 | Medium | High | High-quality English |
| `multilingual-e5-small` | 384 | Fast | Good | Multilingual support |
| `multilingual-e5-base` | 768 | Medium | High | High-quality multilingual |

**Features:**
- **Auto-download**: Models downloaded from HuggingFace automatically
- **Caching**: Models cached locally (~/.cache/huggingface)
- **GPU Support**: CUDA, DirectML (Windows), CoreML (macOS)
- **Batch Processing**: Efficient multi-text embedding
- **Thread-Safe**: Concurrent access supported

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

## CLI Tool

FileFlux includes a command-line interface for quick document processing:

```powershell
# Install CLI tool
dotnet tool install -g FileFlux.CLI

# Complete processing pipeline (extract + chunk + enrich)
fileflux process "document.pdf" --ai --verbose

# Chunk with specific strategy
fileflux chunk "document.pdf" -s Smart -m 512 -l 64

# Extract only (with image extraction)
fileflux extract "document.pptx" --ai

# Available options
# --ai (-a)         Enable AI metadata enrichment
# --strategy (-s)   Chunking strategy: Auto, Smart, Intelligent, Semantic
# --max-size (-m)   Maximum chunk size in tokens (default: 512)
# --overlap (-l)    Overlap size between chunks (default: 64)
# --format (-f)     Output format: md, json, jsonl
# --verbose (-v)    Show detailed processing information
```

See [CLI Documentation](docs/CLI.md) for complete guide.

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

- [**Tutorial**](docs/TUTORIAL.md) - Detailed usage guide including CLI
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
│   ├── FileFlux/                    # Main package (unified project)
│   │   ├── Core/                    # Interfaces
│   │   ├── Domain/                  # Domain models
│   │   └── Infrastructure/          # Implementations
│   └── Directory.Build.props        # Build configuration
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
