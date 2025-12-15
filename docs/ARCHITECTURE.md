# FileFlux Architecture Guide

> Architectural overview of the document processing SDK for RAG systems

## Design Principles

### 1. Clean Architecture

FileFlux follows clean architecture principles with a **two-package structure**:

- **FileFlux.Core**: Pure document extraction with zero AI dependencies
  - Standard document readers (PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV, HTML)
  - Core interfaces and domain models
  - No AI service dependencies
- **FileFlux**: Full RAG pipeline with LocalAI integration
  - MultiModal document readers (AI-enhanced)
  - AI service interfaces and LocalAI implementations
  - Chunking strategies (FluxCurator)
  - Content enhancement (FluxImprover)
  - Processing orchestration

### 2. Interface-Driven Design

- Extensible plugin architecture
- Loose coupling through dependency injection
- Strategy and Factory patterns

### 3. Domain Model Optimization

- **Simplified property names**: `Properties` → `Props`, `ChunkIndex` → `Index`
- **Props dictionary pattern**: Extensible metadata storage
- **Guid-based traceability**: Track entire pipeline stages
- **Simplified Quality**: Changed from complex object to double type
- **Unified API**: Integrated batch/streaming through IDocumentProcessor

## System Architecture

```mermaid
graph TB
    A[Client Application] --> B[IDocumentProcessor]
    B --> C[DocumentProcessor]

    C --> D[IDocumentReaderFactory]
    C --> E[IChunkingStrategyFactory]
    C --> M[IMetadataEnricher]

    D --> F[PdfReader]
    D --> G[WordReader]
    D --> H[ExcelReader]
    D --> I[PowerPointReader]
    D --> J[MarkdownReader]
    D --> K[TextReader]
    D --> L[JsonReader]
    D --> N[CsvReader]
    D --> O[HtmlReader]

    E --> P[AutoChunkingStrategy]
    E --> Q[SmartChunkingStrategy]
    E --> R[IntelligentChunkingStrategy]
    E --> S[SemanticChunkingStrategy]
    E --> T[ParagraphChunkingStrategy]
    E --> U[FixedSizeChunkingStrategy]
    E --> V[MemoryOptimizedIntelligentStrategy]

    M --> X[AIMetadataEnricher]
    M --> Y[RuleBasedMetadataExtractor]

    C --> W[DocumentChunk[]]

    style A fill:#e1f5fe
    style B fill:#f3e5f5
    style C fill:#fff3e0
    style W fill:#e8f5e8
    style M fill:#e8eaf6
```

### Project Structure

FileFlux uses a **two-package architecture** for flexibility:

```
FileFlux.Core/                    # Extraction-Only Package (Zero AI Dependencies)
├── Exceptions/                   # Exception types
│   ├── FileFluxException
│   ├── DocumentProcessingException
│   └── UnsupportedFileFormatException
├── Infrastructure/
│   └── Readers/                  # Standard Document Readers
│       ├── PdfDocumentReader
│       ├── WordDocumentReader
│       ├── ExcelDocumentReader
│       ├── PowerPointDocumentReader
│       ├── MarkdownDocumentReader
│       ├── HtmlDocumentReader
│       ├── TextDocumentReader
│       ├── JsonDocumentReader
│       └── CsvDocumentReader
├── Utils/                        # Utilities
│   └── FileNameHelper
├── IDocumentReader.cs            # Reader interface
├── IDocumentParser.cs            # Parser interface
├── IChunkingStrategy.cs          # Strategy interface
├── DocumentChunk.cs              # Chunk model
├── RawContent.cs                 # Extraction result model
├── ParsedContent.cs              # Parsed content model
└── ChunkingOptions.cs            # Options model

FileFlux/                         # Full RAG Pipeline Package
├── Core/                         # AI Service Interfaces
│   ├── IDocumentProcessor
│   ├── ITextCompletionService    # AI text generation interface
│   ├── IImageToTextService       # Vision AI interface
│   ├── IImageRelevanceEvaluator  # Image relevance interface
│   ├── IEmbeddingService         # Embedding generation interface
│   ├── IMetadataEnricher
│   └── Factories/
├── Infrastructure/
│   ├── Readers/                  # MultiModal Readers (AI-enhanced)
│   │   ├── MultiModalPdfDocumentReader
│   │   ├── MultiModalWordDocumentReader
│   │   ├── MultiModalExcelDocumentReader
│   │   └── MultiModalPowerPointDocumentReader
│   ├── Strategies/               # Chunking Strategies
│   │   ├── AutoChunkingStrategy
│   │   ├── SmartChunkingStrategy
│   │   ├── IntelligentChunkingStrategy
│   │   └── SemanticChunkingStrategy
│   ├── Languages/                # Language Profiles
│   │   └── LanguageProfiles.cs
│   ├── Services/                 # Processing Services
│   │   ├── LocalAI/              # LocalAI Implementations
│   │   │   ├── LocalAIEmbedderService
│   │   │   ├── LocalAIGeneratorService
│   │   │   ├── LocalAICaptionerService
│   │   │   ├── LocalAIOcrService
│   │   │   ├── LocalAIServiceFactory
│   │   │   └── LocalAIOptions
│   │   ├── AIMetadataEnricher
│   │   ├── FluxCurator
│   │   └── FluxImprover
│   └── Factories/                # Factory implementations
└── DocumentProcessor.cs          # Main orchestrator
```

### Layer Structure

```
┌─────────────────────────────────────────────────────────────┐
│                    Client Layer                             │
│  • Application Code                                         │
│  • RAG Systems Integration                                  │
│  • AI Service Implementation                                │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────────────┐  ┌─────────────────────────┐  │
│  │   FileFlux.Core         │  │     FileFlux            │  │
│  │  (Extraction Only)      │  │  (Full RAG Pipeline)    │  │
│  │                         │  │                         │  │
│  │  • Document Readers     │──│  • Chunking Strategies  │  │
│  │  • Core Interfaces      │  │  • FluxCurator          │  │
│  │  • Domain Models        │  │  • FluxImprover         │  │
│  │  • AI Service Contracts │  │  • DocumentProcessor    │  │
│  │  • Zero AI Dependencies │  │  • Orchestration        │  │
│  │                         │  │                         │  │
│  └─────────────────────────┘  └─────────────────────────┘  │
│                                                             │
│   Use Case:                    Use Case:                    │
│   - Extract documents only     - Full processing pipeline   │
│   - Implement own chunking     - Use built-in strategies    │
│   - Minimal dependencies       - AI-enhanced features       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. IDocumentProcessor (Main Interface)

**Role**: Single entry point for all document processing

**Key Methods**:
- `ProcessAsync(filePath/stream)`: Complete processing pipeline
- `ProcessStreamAsync(filePath/stream)`: Streaming processing
- `ExtractAsync()`: Extract raw content
- `ParseAsync()`: Parse structure
- `ChunkAsync()`: Apply chunking

**Responsibilities**: Pipeline orchestration, error handling, result validation

### 2. DocumentProcessor (Orchestrator)

**Processing Pipeline**:
1. Input validation
2. Document type detection
3. Reader selection
4. Content extraction
5. Optional metadata enrichment (if enabled)
6. Structure parsing
7. Strategy selection
8. Chunking application
9. Post-processing

**Dependency Management**:
- Optional logger support via NullLogger pattern
- Optional IMetadataEnricher for advanced metadata features
- Optional ITextCompletionService for AI-powered features
- All optional dependencies use graceful fallback strategies

### 3. IDocumentReader (Content Extraction)

**Current Implementations**:
- **PdfDocumentReader**: PDF text and image extraction
- **WordDocumentReader**: DOCX with style preservation
- **ExcelDocumentReader**: XLSX multi-sheet support
- **PowerPointDocumentReader**: PPTX slide extraction
- **MarkdownDocumentReader**: Markdown structure preservation
- **HtmlDocumentReader**: HTML content extraction
- **TextDocumentReader**: Plain text processing
- **JsonDocumentReader**: JSON structured data
- **CsvDocumentReader**: CSV table data

### 4. IChunkingStrategy (Content Splitting)

**Strategy Types**:
- **AutoChunkingStrategy**: Automatic strategy selection (recommended)
- **SmartChunkingStrategy**: Sentence boundary-based with high completeness
- **IntelligentChunkingStrategy**: LLM-based semantic boundary detection
- **MemoryOptimizedIntelligentChunkingStrategy**: Memory-efficient intelligent chunking
- **SemanticChunkingStrategy**: Sentence-based semantic chunking
- **ParagraphChunkingStrategy**: Paragraph-level segmentation
- **FixedSizeChunkingStrategy**: Fixed-size token-based chunking

### 5. ILanguageProfile (Multilingual Text Segmentation)

**Purpose**: Language-specific rules for accurate sentence boundary detection and text segmentation.

**Key Properties**:
- `LanguageCode`: ISO 639-1 code (en, ko, zh, ja, etc.)
- `ScriptCode`: ISO 15924 script code (Latn, Hang, Hans, Arab, Deva, Cyrl, Jpan)
- `WritingDirection`: LTR, RTL, or TopToBottom
- `NumberFormat`: Decimal/thousands separator conventions
- `QuotationMarks`: Language-specific quote characters
- `SentenceEndPattern`: Regex for sentence boundaries
- `Abbreviations`: Non-breaking abbreviation list
- `CategorizedAbbreviations`: Typed abbreviations (Prepositive/Postpositive/General)

**Supported Languages** (11):
| Language | Script | Direction | Number Format |
|----------|--------|-----------|---------------|
| English | Latn | LTR | Standard (1,234.56) |
| Korean | Hang | LTR | Standard |
| Chinese | Hans | LTR | NoGrouping |
| Japanese | Jpan | LTR | Standard |
| Spanish | Latn | LTR | European (1.234,56) |
| French | Latn | LTR | SpaceSeparated (1 234,56) |
| German | Latn | LTR | European |
| Arabic | Arab | **RTL** | Standard |
| Hindi | Deva | LTR | Standard |
| Portuguese | Latn | LTR | European |
| Russian | Cyrl | LTR | SpaceSeparated |

**Provider Pattern**:
- `ILanguageProfileProvider`: Manages language profile lookup and auto-detection
- `DefaultLanguageProfileProvider`: Built-in provider with Unicode script analysis
- Auto-detection analyzes text Unicode ranges to determine language

## Processing Pipeline

```mermaid
graph TB
    A[Document Input] --> B[Type Detection]
    B --> C[Reader Selection]
    C --> D[Content Extraction]
    D --> E[Metadata Enrichment]
    E --> F[Structure Parsing]
    F --> G[Strategy Selection]
    G --> H[Chunking Process]
    H --> I[Post Processing]
    I --> J[DocumentChunk[]]

    style A fill:#e1f5fe
    style E fill:#e8eaf6
    style J fill:#e8f5e8
```

### 1. Input Processing

- File path or stream input support
- File existence and access permission validation
- Supported format verification

### 2. Content Extraction

- Dedicated reader for each document type
- Text content and metadata extraction
- Document structure preservation

### 3. Metadata Enrichment (Optional)

- AI-powered metadata extraction with ITextCompletionService
- Three-tier fallback: AI → Hybrid → Rule-based
- Automatic caching based on file content hash
- Schema-based extraction (General, ProductManual, TechnicalDoc)
- Enriched metadata stored in CustomProperties with "enriched_" prefix

### 4. Chunking Processing

- Content splitting based on selected strategy
- Overlap between chunks
- Metadata propagation and indexing

## Factory Patterns

### DocumentReaderFactory

- File extension-based reader selection
- New reader registration and management
- Unsupported format exception handling
- Extension discovery API

### ChunkingStrategyFactory

- Strategy name-based selection system
- Default and fallback strategy management
- Dynamic strategy registration support

## Configuration and Options

### ChunkingOptions

**Main Settings**:
- **Strategy**: Chunking strategy name ("Auto", "Smart", "Intelligent", etc.)
- **MaxChunkSize**: Maximum chunk size (default: 1024 tokens)
- **OverlapSize**: Overlap size between chunks (default: 128 tokens)
- **PreserveStructure**: Whether to preserve document structure
- **StrategyOptions**: Strategy-specific detailed options
- **CustomProperties**: Extensible configuration dictionary for features like metadata enrichment

**Metadata Enrichment Configuration**:
```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",
    CustomProperties = new Dictionary<string, object>
    {
        ["enableMetadataEnrichment"] = true,
        ["metadataSchema"] = MetadataSchema.General,
        ["metadataOptions"] = new MetadataEnrichmentOptions
        {
            ExtractionStrategy = MetadataExtractionStrategy.Smart,
            MinConfidence = 0.7
        }
    }
};
```

### Dependency Injection Setup

**Basic Registration** (no AI):
```csharp
services.AddFileFlux();  // Pure extraction + chunking
```

**With LocalAI** (local AI processing):
```csharp
// Full LocalAI integration
services.AddFileFluxWithLocalAI();

// With custom configuration
services.AddFileFluxWithLocalAI(options =>
{
    options.UseGpuAcceleration = true;
    options.EmbeddingModel = "default";
    options.GeneratorModel = "microsoft/Phi-4-mini-instruct-onnx";
    options.WarmupOnInit = true;
});
```

**Selective LocalAI Services**:
```csharp
services.AddLocalAIEmbedder();    // IEmbeddingService only
services.AddLocalAIGenerator();   // ITextCompletionService only
services.AddLocalAICaptioner();   // IImageToTextService (captions)
services.AddLocalAIOcr();         // IImageToTextService (OCR)
```

**With External AI Services**:
```csharp
// Use your own AI service implementations
services.AddScoped<ITextCompletionService, YourLLMService>();
services.AddScoped<IImageToTextService, YourVisionService>();
services.AddFileFlux();
```

**Extension Registration**: Add custom readers/strategies

## Performance Considerations

### Memory Management

- Stream-based processing for large files
- IDisposable pattern for resource cleanup
- ConfigureAwait(false) to minimize context switching

### Concurrency

- All public interfaces are thread-safe
- Factories use immutable collections
- No shared mutable state

### Scalability

- Minimal memory allocation
- Efficient string processing
- Reusable component design

## Extension Points

### Adding Custom Reader

1. Implement IDocumentReader interface
2. Register in DI container
3. Implement SupportedExtensions and CanRead methods

**Example**:
```csharp
public class CustomDocumentReader : IDocumentReader
{
    public string ReaderType => "CustomReader";
    public IEnumerable<string> SupportedExtensions => [".custom"];

    public bool CanRead(string fileName) =>
        Path.GetExtension(fileName).Equals(".custom", StringComparison.OrdinalIgnoreCase);

    public async Task<RawContent> ReadAsync(string filePath, CancellationToken cancellationToken)
    {
        // Implementation
    }
}

// Registration
services.AddTransient<IDocumentReader, CustomDocumentReader>();
```

### Adding Custom Chunking Strategy

1. Implement IChunkingStrategy interface
2. Define StrategyName and DefaultOptions
3. Implement chunking logic in ChunkAsync method

**Example**:
```csharp
public class CustomChunkingStrategy : IChunkingStrategy
{
    public string StrategyName => "Custom";

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        ParsedDocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken)
    {
        // Implementation
    }
}

// Registration
services.AddTransient<IChunkingStrategy, CustomChunkingStrategy>();
```

## Error Handling

### Exception Hierarchy

- **FileFluxException**: Base class for all exceptions
- **UnsupportedFileFormatException**: Unsupported file format
- **DocumentProcessingException**: Error during document processing
- **ChunkingException**: Error during chunking process

### Error Handling Patterns

- Early error detection through input validation
- Meaningful error messages with context
- Preserve inner exceptions for cause tracking
- Include debugging information (filename, strategy name)

## RAG System Integration

### Processing Result

**DocumentChunk**:
- `Id` (Guid): Unique chunk identifier
- `Content` (string): Chunk text content
- `Index` (int): Chunk order index
- `Location` (SourceLocation): StartChar/EndChar position info
- `Quality` (double): Quality score (0.0~1.0)
- `Props` (Dictionary<string, object>): Extensible metadata

**RawContent**:
- `Text`: Extracted raw text
- `File` (SourceFileInfo): File information
- `ReaderType`: Reader type used
- `ExtractedAt`: Extraction timestamp
- `Warnings`: Processing warnings
- `Hints`: Processing hints

**ParsedDocumentContent**:
- `Content`: Parsed text content
- `Sections`: Structured sections (optional)
- `RawId`: Reference to RawContent.Id
- `Props`: Additional metadata

**SourceFileInfo**:
- `Name`: Filename
- `Extension`: File extension
- `Size`: File size
- `Path`: File path (optional)

### Integration Patterns

1. **Streaming Processing**: Sequential processing per chunk with ProcessStreamAsync
2. **Batch Processing**: Collect all chunks then batch process
3. **Pipeline Processing**: Simultaneous chunk generation and embedding generation

### Extensibility Pattern

```csharp
// Extensible metadata with Props dictionary
chunk.Props["ContextualHeader"] = "Document: Technical";
chunk.Props["DocumentDomain"] = "Technical";
chunk.Props["HasImages"] = true;

// Maintain backward compatibility with extension methods
public static string? ContextualHeader(this DocumentChunk chunk)
    => chunk.Props.TryGetValue("ContextualHeader", out var v) ? v?.ToString() : null;
```

### Metadata Enrichment Pattern

```csharp
// Enriched metadata storage in CustomProperties
chunk.Metadata.CustomProperties["enriched_topics"] = new[] { "AI", "Machine Learning" };
chunk.Metadata.CustomProperties["enriched_keywords"] = new[] { "neural networks", "deep learning" };
chunk.Metadata.CustomProperties["enriched_description"] = "Introduction to AI concepts";
chunk.Metadata.CustomProperties["enriched_confidence"] = 0.92;
chunk.Metadata.CustomProperties["enriched_extractionMethod"] = "ai";

// Access enriched metadata
var topics = chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_topics") as string[];
var confidence = Convert.ToDouble(chunk.Metadata.CustomProperties.GetValueOrDefault("enriched_confidence", 0.0));
```

### Pipeline Traceability

```
RawContent.Id (Guid)
    ↓
ParsedDocumentContent.RawId → RawContent.Id
    ↓
DocumentChunk.RawId → RawContent.Id
DocumentChunk.ParsedId → ParsedDocumentContent.Id
```

## Design Philosophy

FileFlux focuses on transforming documents into structured chunks optimized for RAG systems.

**Two-Package Strategy**:
- **FileFlux.Core**: Pure extraction, zero AI dependencies
  - Standard document readers
  - Core interfaces and models
  - For users implementing custom pipelines
- **FileFlux**: Full RAG pipeline with LocalAI integration
  - MultiModal readers (AI-enhanced)
  - LocalAI services (embedding, generation, captioning, OCR)
  - FluxCurator and FluxImprover

**LocalAI Integration**: Built-in local AI processing without external API dependencies:
- Embedding generation (LocalAI.Embedder)
- Text generation (LocalAI.Generator)
- Image captioning (LocalAI.Captioner)
- OCR text extraction (LocalAI.Ocr)

**Interface Provider Pattern**: FileFlux defines interfaces while providing both local implementations and allowing custom providers.

**Package Selection Guide**:
```csharp
// Extraction only - implement your own chunking
using FileFlux.Core;
var reader = new PdfDocumentReader();
var rawContent = await reader.ReadAsync("document.pdf");

// Full RAG pipeline with LocalAI
using FileFlux;
services.AddFileFluxWithLocalAI();
var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();
var chunks = await processor.ProcessAsync("document.pdf", options);

// Custom AI providers
services.AddScoped<ITextCompletionService, OpenAIService>();
services.AddFileFlux();
```

## Related Documentation

- [Tutorial](TUTORIAL.md) - Detailed usage guide
- [Building](../BUILDING.md) - Build and test guide
- [Test Results](RESULTS.md) - Real-world API test results
- [GitHub Repository](https://github.com/iyulab/FileFlux)
- [NuGet Package](https://www.nuget.org/packages/FileFlux)
