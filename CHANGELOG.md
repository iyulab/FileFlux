# Changelog

All notable changes to FileFlux will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
