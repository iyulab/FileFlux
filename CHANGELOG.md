# Changelog

All notable changes to FileFlux will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Auto Chunking Strategy Selection**: Document structure-based strategy selection
  - `SelectChunkingStrategy()`: Analyzes document for headings and numbered sections
  - `AnalyzeDocumentStructure()`: Detects Markdown headings, numbered patterns
  - Documents with 5+ numbered sections use Paragraph strategy (prevents mid-step breaks)
  - Documents with 3+ Markdown headings use Hierarchical strategy
- **Document-Level Analysis in info.json**: Aggregated metadata from all chunks
  - `AggregateDocumentAnalysis()`: Collects keywords, topics, summary across chunks
  - `documentAnalysis` field in info.json with document-wide insights
- **Enhanced Image Processing**: Improved caption and placeholder handling
  - Fallback captions with image dimensions when AI unavailable
  - External image placeholder cleanup for embedded references
- **Numbered Section to Heading Conversion**: Automatic Markdown structure improvement
  - `ConvertNumberedSectionsToHeadings()`: Converts numbered markers to headings
  - Patterns: `1.` → H2, `3-1.` → H3, `4-2-3.` → H4, `①` → H3, `(1)` → H3
- **Metadata JSON Files**: Per-stage metadata output
  - `extract.json`: Extraction stage metadata (file info, timing)
  - `refine.json`: Refinement metrics (quality scores, structure info)
- **Enrichment Output Files**: Dedicated enrich/ folder output
  - Individual chunk enrichment JSONs
  - `index.json` with document-level analysis summary
- **CJK Text Detection**: Automatic detection and chunk size adjustment for Korean/Chinese/Japanese content
  - `GetCjkRatio()`: Calculates ratio of CJK characters in content
  - `GetCjkChunkSizeMultiplier()`: Calculates chunk size multiplier based on CJK ratio
  - Detects Hangul, CJK Unified Ideographs, Hiragana, Katakana
  - Adjusts chunk size proportionally to CJK content ratio (up to 85% reduction for pure CJK)
- **Model-Aware Chunking**: Automatic chunk size adjustment based on AI model context limits
  - Detects local model constraints (e.g., Phi-4-mini: 512 tokens)
  - Reduces chunk size from default 512 to model-appropriate size
  - Displays adjustment notice in CLI output

### Fixed
- **OGA Memory Leak**: Proper disposal of ONNX GenAI resources
  - `FluxImproverResult`: Wrapper with `IAsyncDisposable` for proper cleanup
  - Ensures GPU/memory resources are released after processing
- **BasicDocumentParser Hierarchical Section Truncation**: Fixed 98.7% content loss in HWP files
  - `FormatStructuredText()` now recursively processes `Children` sections
  - Added `FormatSectionsRecursively()` helper method
  - HWP output increased from 445 to 33,737 chars after fix
- **SemanticBoundaryDetector TopicChange Detection**: Fixed incorrect boundary type classification
  - Low similarity (< 0.5) now correctly returns `TopicChange` regardless of punctuation
  - Sentence/Paragraph distinction only applies when similarity ≥ 0.5

### Known Issues
- **LMSupply Integration Hang**: Process may hang when AI enrichment is enabled with local models
  - Workaround: Use `--no-ai` or `--no-enrich` flags
  - Issue occurs during model warmup or progress display interaction

## [0.9.0] - 2025-12-17

### Added
- **Stateful Document Processor**: 4-stage pipeline with explicit state management
  - `IDocumentProcessor`: Stage-by-stage execution with auto-dependency resolution
  - `StatefulDocumentProcessor`: Stateful implementation holding per-document state
  - `IDocumentProcessorFactory`: Factory pattern for creating processors
  - `ProcessorState`: State tracking (Created → Extracted → Refined → Chunked → Enriched)
  - `ProcessingResult`: Accumulated results across all stages
- **IDocumentRefiner (Stage 2)**: Content refinement and structure extraction
  - `IDocumentRefiner`: Interface for text cleaning, normalization, structure analysis
  - `DocumentRefiner`: Implementation with markdown conversion support
  - `RefinedContent`: Structured output with sections, metadata, quality scores
  - `StructuredElement`: Code blocks, tables, images with source locations
  - `RefineOptions`: Configuration for cleaning, markdown conversion, structure extraction
- **IDocumentEnricher (Stage 4)**: LLM-powered chunk enrichment
  - `IDocumentEnricher`: Interface for chunk enrichment and graph building
  - `DocumentEnricher`: FluxImprover integration for summaries, keywords, contextual text
  - `EnrichedDocumentChunk`: Chunks with LLM-generated metadata
  - `EnrichmentResult`: Enrichment output with stats and optional graph
  - `DocumentGraph`: Inter-chunk relationship graph with nodes and edges
  - `GraphBuildOptions`: Configuration for sequential, hierarchical, semantic edges
- **Pipeline Tests**: Comprehensive test suite for new pipeline components
  - `StatefulDocumentProcessorTests`: Full pipeline execution tests
  - `DocumentRefinerTests`: Refinement and structure extraction tests
  - `DocumentEnricherTests`: Enrichment and graph building tests

### Changed
- `DocumentProcessorFactory`: Updated to inject `IDocumentRefiner` and `IDocumentEnricher`
- `ServiceCollectionExtensions`: Registered new interfaces with DI container
- Test count increased from 302 to 343 (41 new tests)

### Architecture
- **4-Stage Pipeline**: Extract → Refine → Chunk → Enrich
- **Delegation Pattern**: StatefulDocumentProcessor delegates to specialized interfaces
- **Factory Pattern**: Processors created via factory for proper dependency injection
- **Idempotent Stages**: Each stage checks and skips if already completed

## [0.8.7] - 2025-12-16

### Added
- **Refine Stage Pipeline Integration**: 5단계 파이프라인 완성
  - Extract → Parse → **Refine** → Chunk → Enhance
  - `ChunkingOptions.RefiningOptions`: 기본 활성화 (Markdown 변환 포함)
  - `RefiningOptions.ConvertToMarkdown`: 기본값 `true` (RAG 품질 향상)
  - `RefiningOptions.ProcessImagesToText`: opt-in 방식 (비용 고려)
- **IMarkdownConverter Integration**: Refine 단계에서 Markdown 변환 자동 적용
- **IImageToTextService Integration**: 이미지 텍스트 추출 파이프라인 통합
- **Factory Methods**: `RefiningOptions.ForRAG`, `RefiningOptions.ForRAGWithImages`

### Changed
- `ChunkingOptions.RefiningOptions` 기본값: `null` → `new RefiningOptions()` (자동 활성화)
- `RefiningOptions.ConvertToMarkdown` 기본값: `false` → `true` (RAG 최적화)
- `FluxDocumentProcessor` 생성자: `IMarkdownConverter?`, `IImageToTextService?` 선택적 의존성 추가

## [0.8.6] - 2025-12-16

### Added
- **IMarkdownConverter Interface**: RAG 청킹 품질 향상을 위한 Markdown 변환 기능
  - `IMarkdownConverter`: 구조화된 Markdown 변환 인터페이스
  - `MarkdownConverter`: 휴리스틱 기반 변환 구현체
  - `MarkdownConversionOptions`: 변환 옵션 설정
  - `MarkdownConversionResult`: 변환 결과 및 통계
- **헤딩 감지 및 변환**
  - 기존 Markdown 헤딩 보존 (`#`, `##`, `###`)
  - ALL CAPS 텍스트 → 헤딩 변환
  - 번호 섹션 → 헤딩 변환
  - 헤딩 레벨 제약 옵션 (MinHeadingLevel, MaxHeadingLevel)
- **리스트 변환**
  - Unicode 글머리 기호 변환 (`•`, `●`, `○`, `■` → `-`)
  - 대체 형식 변환 (`1)`, `a)`, `(1)` → 표준 Markdown)
- **테이블 처리**: 헤더 구분선 자동 추가
- **이미지 플레이스홀더 변환**: 표준 Markdown 이미지 형식
- **공백 정규화**: Windows/Unix 줄바꿈 호환
- **선택적 LLM 향상**: `ITextCompletionService` DI를 통한 LLM 통합 지원

### Changed
- MultiModal readers에서 디버그용 Console.WriteLine 제거

## [0.8.0] - 2025-12-15

### Added
- **LMSupply Integration**: Complete Locally running AI processing capabilities without external API dependencies
  - `LMSupplyEmbedderService`: Local embedding generation using LMSupply.Embedder
  - `LMSupplyGeneratorService`: Local text generation using LMSupply.Generator
  - `LMSupplyCaptionerService`: Local image captioning using LMSupply.Captioner
  - `LMSupplyOcrService`: Local OCR text extraction using LMSupply.Ocr
- **LMSupplyServiceFactory**: Thread-safe factory for lazy initialization of LMSupply services
- **LMSupplyOptions**: Centralized configuration for all LMSupply services
  - GPU acceleration support (DirectML, CUDA, CoreML)
  - Configurable model selection
  - Warmup and caching options
- **New DI Extension Methods**:
  - `AddFileFluxWithLMSupply()`: Register all LMSupply services
  - `AddLMSupplyEmbedder()`: Register embedding service only
  - `AddLMSupplyGenerator()`: Register text generation service only
  - `AddLMSupplyCaptioner()`: Register image captioning service only
  - `AddLMSupplyOcr()`: Register OCR service only

### Changed
- **Architecture Refactoring**: Cleaner separation between Core and main package
  - FileFlux.Core: Pure document extraction (zero AI dependencies)
  - FileFlux: LMSupply integrated RAG pipeline
- **Dependency Management**: Debug/Release conditional references
  - Debug: Project references to local `../lm-supply` source
  - Release: NuGet package references for distribution

### Removed
- **LocalEmbedder Dependency**: Replaced with LMSupply ecosystem
  - Archived package replaced with actively maintained LMSupply modules

## [0.7.3] - 2025-12-13

### Added
- **PDF Table Confidence Scoring**: New algorithm evaluates table extraction quality (0.0-1.0)
  - Considers column consistency, content density, and column count reasonableness
  - Tables below threshold (0.5) automatically fall back to plain text
- **StructuralHints for Tables**: New hints exposed for consumer applications
  - `TablesDetected`: Total number of tables found in document
  - `LowConfidenceTables`: Count of tables using plain text fallback
  - `MinTableConfidence`: Lowest confidence score among all tables
- **Known Limitations Section**: Added to README documenting PDF processing constraints

### Fixed
- **PDF Table Extraction Quality**: Major improvements to table detection and formatting
  - Adaptive column detection using character-width-based bucket sizing
  - Column merging to prevent over-fragmentation from whitespace
  - Empty cell handling with deduplication and normalization
  - Maximum column limit (10) prevents false positives
- **Garbled Table Output**: Low-confidence tables now render as clean plain text instead of malformed markdown

### Changed
- **PdfDocumentReader**: Enhanced table extraction with confidence-based fallback mechanism

## [0.7.0] - 2025-12-10

### Added
- **Base64 Image Extraction**: HtmlDocumentReader now extracts embedded base64 images
  - Replaces base64 data in text with placeholder: `![alt](embedded:img_000)`
  - Stores binary image data in `RawContent.Images` collection
  - Dramatically reduces text output size (4.4MB → ~50KB for documents with embedded images)
  - Improves RAG embedding quality by removing non-semantic base64 noise
- **ImageInfo Extended**: Enhanced image metadata model
  - `MimeType`: Image MIME type (e.g., "image/png", "image/jpeg")
  - `Data`: Optional binary data for embedded images
  - `SourceUrl`: External URL or "embedded:{id}" reference
  - `OriginalSize`: Original base64 data size in bytes
- **RawContent.Images**: New property to access extracted images immediately after extraction
- **Structural Hints**: `embedded_image_count` and `embedded_images_extracted` hints added

### Changed
- **HtmlDocumentReader**: ProcessImage method now detects and extracts base64 images
- External image URLs are preserved as-is with metadata in Images collection

## [0.6.2] - 2025-12-08

### Added
- **TextSanitizer Utility**: New utility class for text sanitization in FileFlux.Core
  - `RemoveNullBytes()` - Removes null bytes (0x00) from text
  - `Sanitize()` - Comprehensive text sanitization with optional control character removal
  - `ContainsNullBytes()` - Check for null byte presence
  - `IsValidUtf8()` - Validate UTF-8 compatibility

### Fixed
- **UTF-8 Null Byte Issue**: PDF and DOCX text extraction now removes null bytes
  - Prevents PostgreSQL and other database UTF-8 encoding errors
  - Handles embedded binary objects, form field data, and legacy encoding artifacts
  - Applied in PdfDocumentReader.NormalizeText() and WordDocumentReader.CleanupMarkdown()

## [0.5.0] - 2025-12-01

### Added
- **FileFlux.Core Package**: Standalone document extraction with zero AI dependencies
  - All 12 document readers (PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV, HTML, MultiModal variants)
  - Core interfaces (IDocumentReader, IDocumentParser, IChunkingStrategy)
  - AI service interfaces (ITextCompletionService, IImageToTextService)
- **ChunkPropsKeys Enhancement**: Standardized property keys with typed accessors
  - `DocumentTopic`, `DocumentKeywords`, `QualityRelevanceScore`, `QualityCompleteness`
  - `ContentType`, `StructuralRole` for content classification
  - Helper methods: `TryGetValue<T>()`, `GetValueOrDefault<T>()`
- **DocumentChunk Typed Accessors**: `EnrichedSummary`, `EnrichedKeywords`, `EnrichedContextualText`, `HasEnrichment`
- **ChunkMetadataEnricher**: Generic metadata enricher (renamed from Context7MetadataEnricher)
- **DocumentContext**: Separate domain class for document-level context
- **Google Gemini AI Support**: Fourth AI provider alongside OpenAI, Anthropic, and local embedder
- **FluxCurator v0.4.0 Integration**: TextRefiner with presets (Light, Standard, ForKorean, ForWebContent, ForPdfContent)

### Changed
- **Architecture**: Split into FileFlux.Core (extraction) and FileFlux (full RAG pipeline)
- **RefiningOptions**: Added `TextRefinementPreset` for FluxCurator integration
- **DocumentProcessor**: Delegated text refinement to FluxCurator TextRefiner

### Removed
- Legacy string literals for Props keys (replaced with ChunkPropsKeys constants)
- Duplicate refinement logic (now handled by FluxCurator)

### Fixed
- Props vs CustomProperties confusion in enrichment counting
- Inconsistent property key naming across codebase

## [0.4.8] - 2025-11-29

### Added
- **ILanguageProfile Enhancement**: Extended multilingual text segmentation support
  - WritingDirection enum (LeftToRight, RightToLeft, TopToBottom)
  - NumberFormat struct with presets (Standard, European, SpaceSeparated, NoGrouping)
  - QuotationMarks struct with language-specific conventions
  - CategorizedAbbreviations with Prepositive/Postpositive/General types
  - ScriptCode property (ISO 15924: Latn, Hang, Hans, Arab, Deva, Cyrl, Jpan)
- **11 Language Profiles**: Complete language support with extended properties
  - English, Korean, Chinese, Japanese, Spanish, French, German, Portuguese, Russian, Hindi
  - Arabic (RTL writing direction support)
- **DefaultLanguageProfileProvider**: Unicode script-based language auto-detection

### Improved
- Sentence boundary detection with language-aware abbreviation handling
- RTL text processing support for Arabic language

## [0.4.0] - 2025-11-23

### Added
- **IEnrichedChunk Interface**: Standardized interface for RAG system integration with structural metadata
- **LanguageDetector Service**: Automatic language detection using NTextCat library
  - Supports 30+ languages with confidence scoring
  - Document-level and chunk-level detection
  - Auto-detection integrated into chunking pipeline
- **ContextDependencyAnalyzer**: Calculates context dependency scores (0-1) for chunks
  - Pronoun analysis (it, this, that, they, etc.)
  - Reference detection (above, below, previous, following, etc.)
  - Conjunction analysis for cross-chunk dependencies
- **ChunkingHelper**: Common utilities for all chunking strategies
  - HeadingPath resolution from document sections
  - Page number tracking with PageRanges
  - Unified chunk enrichment across strategies
- **PageRanges Support**: Dictionary mapping page numbers to character positions in PDF documents
- **SourceMetadataInfo**: Rich source metadata including title, page count, word count, language
- **SourceLocation**: Precise location tracking with HeadingPath, page numbers, character positions

### Changed
- **DocumentChunk**: Added ContextDependency, SourceInfo, and Location properties
- **DocumentContent**: Added PageRanges and Sections for structural metadata
- **SemanticChunkingStrategy**: Integrated ChunkingHelper.EnrichChunk for consistent metadata
- Upgraded to .NET 10.0 target framework

### Improved
- All chunking strategies now provide consistent structural metadata
- Better RAG integration with IEnrichedChunk standardization
- Enhanced test coverage with 329 passing tests

## [0.3.18] - 2025-11-20

### Changed
- Updated package references to remove version numbers for central package management
- Updated System.CommandLine to version 2.0.0
- Refactored command argument handling in CLI

## [0.3.17] - 2025-11-15

### Added
- Comprehensive quality analysis features
- ITextCompletionService integration guide
- Q&A benchmark generation for RAG testing

### Improved
- Documentation organization and clarity

## [0.3.16] - 2025-11-10

### Added
- ZIP archive processing with automatic extraction
- Security validation (path traversal, zip bomb detection)
- Configurable resource limits for archive processing

### Security
- Path traversal protection
- Compression ratio validation
- Safe extraction with automatic cleanup

## [0.3.15] - 2025-11-05

### Added
- Metadata enrichment with AI-powered extraction
- MetadataSchema support (General, Academic, Technical, Legal, Medical)
- Caching and fallback for metadata enrichment

## [0.3.14] - 2025-11-01

### Added
- HierarchicalChunkingStrategy for nested document structures
- PageLevelChunkingStrategy for page-based processing

### Improved
- Chunking strategy selection in Auto mode

## [0.3.13] - 2025-10-25

### Added
- HTML document reader with structure preservation
- CSV document reader with header support

### Fixed
- Memory optimization in large document processing

## [0.3.12] - 2025-10-20

### Added
- IImageToTextService interface for multimodal processing
- Vision API support in CLI

### Improved
- PDF image extraction quality

## [0.3.11] - 2025-10-15

### Added
- ITextCompletionService interface for AI integration
- Mock implementations for testing

### Changed
- Removed direct AI provider dependencies
- Interface-based design for consumer flexibility

## [0.3.10] - 2025-10-10

### Added
- MemoryOptimizedIntelligentStrategy for large documents
- Streaming processing with ProcessStreamAsync

### Improved
- Memory efficiency in document processing
- Progress reporting for long operations

## [0.3.0] - 2025-10-01

### Added
- IntelligentChunkingStrategy with AI-powered boundary detection
- SmartChunkingStrategy for legal/medical/academic documents
- SemanticChunkingStrategy for general documents

### Changed
- Unified project structure (merged Core, Domain, Infrastructure)
- Simplified API with single entry point

## [0.2.0] - 2025-09-15

### Added
- PowerPoint (.pptx) document reader
- Excel (.xlsx) document reader
- JSON document reader
- Auto strategy for automatic chunking selection

### Improved
- Document structure preservation
- Metadata extraction accuracy

## [0.1.0] - 2025-09-01

### Added
- Initial release
- PDF document reader
- Word (.docx) document reader
- Markdown document reader
- Text document reader
- Basic chunking strategies (FixedSize, Paragraph)
- Dependency injection support with AddFileFlux()

[0.5.0]: https://github.com/iyulab/FileFlux/compare/v0.4.8...v0.5.0
[0.4.8]: https://github.com/iyulab/FileFlux/compare/v0.4.0...v0.4.8
[0.4.0]: https://github.com/iyulab/FileFlux/compare/v0.3.18...v0.4.0
[0.3.18]: https://github.com/iyulab/FileFlux/compare/v0.3.17...v0.3.18
[0.3.17]: https://github.com/iyulab/FileFlux/compare/v0.3.16...v0.3.17
[0.3.16]: https://github.com/iyulab/FileFlux/compare/v0.3.15...v0.3.16
[0.3.15]: https://github.com/iyulab/FileFlux/compare/v0.3.14...v0.3.15
[0.3.14]: https://github.com/iyulab/FileFlux/compare/v0.3.13...v0.3.14
[0.3.13]: https://github.com/iyulab/FileFlux/compare/v0.3.12...v0.3.13
[0.3.12]: https://github.com/iyulab/FileFlux/compare/v0.3.11...v0.3.12
[0.3.11]: https://github.com/iyulab/FileFlux/compare/v0.3.10...v0.3.11
[0.3.10]: https://github.com/iyulab/FileFlux/compare/v0.3.0...v0.3.10
[0.3.0]: https://github.com/iyulab/FileFlux/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/iyulab/FileFlux/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/iyulab/FileFlux/releases/tag/v0.1.0
