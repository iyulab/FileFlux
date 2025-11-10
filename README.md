# FileFlux

> .NET document processing library for RAG systems

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

## Overview

FileFlux is a .NET library that transforms various document formats into optimized chunks for RAG (Retrieval-Augmented Generation) systems.

### Key Features

- **Multiple Document Formats**: PDF, DOCX, XLSX, PPTX, Markdown, HTML, TXT, JSON, CSV
- **Flexible Chunking Strategies**: Auto, Smart, Intelligent, Semantic, Paragraph, FixedSize
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
    var topics = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_topics");
    var keywords = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_keywords");
}
```

### ZIP Archive Processing

FileFlux automatically processes ZIP archives containing supported document formats:

```csharp
// Process ZIP archive directly - no manual extraction needed
var chunks = await processor.ProcessAsync("documents.zip");

// Archive processing includes:
// - Automatic extraction of supported files (PDF, DOCX, etc.)
// - Security validation (path traversal, zip bomb detection)
// - Resource limits (file size, count, compression ratio)
// - Automatic cleanup of temporary files
```

**Security Features**:
- **Path Traversal Protection**: Blocks malicious paths like `../../etc/passwd`
- **Zip Bomb Detection**: Validates compression ratios (default max: 100x)
- **Resource Limits**: Configurable size and file count limits
- **Safe Extraction**: Isolated temporary directory with automatic cleanup

**Configuration Options**:
```csharp
var options = new ZipProcessingOptions
{
    MaxZipFileSize = 100 * 1024 * 1024,      // 100MB (default)
    MaxExtractedSize = 1024 * 1024 * 1024,   // 1GB (default)
    MaxFileCount = 1000,                      // Max files (default)
    MaxCompressionRatio = 100,                // Zip bomb threshold (default)
    EnableParallelProcessing = true           // Parallel file processing (default)
};

var readerFactory = serviceProvider.GetRequiredService<IDocumentReaderFactory>();
var zipReader = new ZipArchiveReader(readerFactory, options);
```

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
| **ZIP Archive** | **.zip** | **Automatic extraction and processing of supported documents** |

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
# Deploy CLI locally
.\scripts\deploy-cli-local.ps1

# Extract documents with Vision API
fileflux extract "document.pptx" --enable-vision

# Process with specific strategy
fileflux chunk "document.pdf" -s Smart
```

See [CLI Usage](docs/TUTORIAL.md#cli-usage) in the tutorial for complete guide.

## Documentation

- [**Tutorial**](docs/TUTORIAL.md) - Detailed usage guide including CLI
- [**Architecture**](docs/ARCHITECTURE.md) - System design document

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
