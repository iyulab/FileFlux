# FileFlux Architecture Guide

> Architectural overview of the document processing SDK for RAG systems

## Design Principles

### 1. Clean Architecture

- **Domain Layer**: Core models and interface definitions
- **Core Layer**: Business logic and processing orchestration
- **Infrastructure Layer**: Concrete implementations (readers, strategies)

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

    C --> W[DocumentChunk[]]

    style A fill:#e1f5fe
    style B fill:#f3e5f5
    style C fill:#fff3e0
    style W fill:#e8f5e8
```

### Layer Structure

```
┌─────────────────────────────────┐
│         Client Layer            │
│ • Application Code              │
│ • RAG Systems Integration       │
│ • Service Configuration         │
├─────────────────────────────────┤
│       Abstraction Layer         │
│ • IDocumentProcessor            │
│ • IDocumentReader               │
│ • IChunkingStrategy             │
├─────────────────────────────────┤
│          Core Layer             │
│ • DocumentProcessor             │
│ • DocumentReaderFactory         │
│ • ChunkingStrategyFactory       │
├─────────────────────────────────┤
│     Implementation Layer        │
│ • Document Readers              │
│ • Chunking Strategies           │
│ • Text Processing Utilities     │
├─────────────────────────────────┤
│         Model Layer             │
│ • DocumentChunk                 │
│ • RawContent                    │
│ • ParsedDocumentContent         │
│ • ChunkingOptions               │
└─────────────────────────────────┘
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
5. Strategy selection
6. Chunking application
7. Post-processing

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

## Processing Pipeline

```mermaid
graph TB
    A[Document Input] --> B[Type Detection]
    B --> C[Reader Selection]
    C --> D[Content Extraction]
    D --> E[Structure Parsing]
    E --> F[Strategy Selection]
    F --> G[Chunking Process]
    G --> H[Post Processing]
    H --> I[DocumentChunk[]]

    style A fill:#e1f5fe
    style I fill:#e8f5e8
```

### 1. Input Processing

- File path or stream input support
- File existence and access permission validation
- Supported format verification

### 2. Content Extraction

- Dedicated reader for each document type
- Text content and metadata extraction
- Document structure preservation

### 3. Chunking Processing

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

### Dependency Injection Setup

**Basic Registration**: `services.AddFileFlux()`

**Custom Configuration**: Configure defaults with options callback

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

FileFlux focuses on transforming documents into structured chunks, leaving embedding generation and vector storage to user choice.

**Interface Provider Pattern**: FileFlux defines interfaces (ITextCompletionService, IImageToTextService) while implementation is up to consuming applications.

## Related Documentation

- [Tutorial](TUTORIAL.md) - Detailed usage guide
- [RAG Design](RAG-DESIGN.md) - RAG system integration patterns
- [Building](../BUILDING.md) - Build and test guide
- [GitHub Repository](https://github.com/iyulab/FileFlux)
- [NuGet Package](https://www.nuget.org/packages/FileFlux)
