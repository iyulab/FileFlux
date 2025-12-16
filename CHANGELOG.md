# Changelog

All notable changes to FileFlux will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
- **LocalAI Integration**: Complete local AI processing capabilities without external API dependencies
  - `LocalAIEmbedderService`: Local embedding generation using LocalAI.Embedder
  - `LocalAIGeneratorService`: Local text generation using LocalAI.Generator
  - `LocalAICaptionerService`: Local image captioning using LocalAI.Captioner
  - `LocalAIOcrService`: Local OCR text extraction using LocalAI.Ocr
- **LocalAIServiceFactory**: Thread-safe factory for lazy initialization of LocalAI services
- **LocalAIOptions**: Centralized configuration for all LocalAI services
  - GPU acceleration support (DirectML, CUDA, CoreML)
  - Configurable model selection
  - Warmup and caching options
- **New DI Extension Methods**:
  - `AddFileFluxWithLocalAI()`: Register all LocalAI services
  - `AddLocalAIEmbedder()`: Register embedding service only
  - `AddLocalAIGenerator()`: Register text generation service only
  - `AddLocalAICaptioner()`: Register image captioning service only
  - `AddLocalAIOcr()`: Register OCR service only

### Changed
- **Architecture Refactoring**: Cleaner separation between Core and main package
  - FileFlux.Core: Pure document extraction (zero AI dependencies)
  - FileFlux: LocalAI integrated RAG pipeline
- **Dependency Management**: Debug/Release conditional references
  - Debug: Project references to local `../local-ai` source
  - Release: NuGet package references for distribution

### Removed
- **LocalEmbedder Dependency**: Replaced with LocalAI ecosystem
  - Archived package replaced with actively maintained LocalAI modules

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
