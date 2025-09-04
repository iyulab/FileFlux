# FileFlux
> RAG 시스템을 위한 지능형 문서 처리 SDK

[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Tests](https://img.shields.io/badge/tests-75%20passing-brightgreen)](#)

## 🎯 개요

FileFlux는 문서를 RAG(Retrieval-Augmented Generation) 시스템에 최적화된 고품질 청크로 변환하는 **.NET 9 SDK**입니다. LLM 기반 지능형 분석으로 문서 구조를 이해하고 의미적 경계를 보존하여 최적의 RAG 성능을 제공합니다.

### ✨ 핵심 기능
- **🤖 LLM 통합 지능형 처리**: 문서 도메인 자동 인식 및 구조적 청킹
- **📄 광범위한 포맷 지원**: PDF, DOCX, PPTX, XLSX, MD, TXT, JSON, CSV
- **🎛️ 4가지 청킹 전략**: Intelligent, Semantic, Paragraph, FixedSize
- **🏗️ Clean Architecture**: 인터페이스 중심 확장 가능 설계
- **📊 풍부한 메타데이터**: 문서 구조, 품질 지표, 도메인 정보
- **🚀 Production Ready**: OpenXML & PDF 처리 엔진 내장

---

## 🚀 빠른 시작

### 1. 프로젝트 설정
```bash
git clone https://github.com/iyulab/FileFlux.git
cd FileFlux
dotnet restore
dotnet build
```

### 2. 기본 문서 처리
```csharp
using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// DI 컨테이너 설정
var services = new ServiceCollection();
services.AddFileFlux();
var provider = services.BuildServiceProvider();

var processor = provider.GetRequiredService<IDocumentProcessor>();

// 문서 처리
var chunks = await processor.ProcessAsync("document.md", new ChunkingOptions
{
    Strategy = "Intelligent",    // 지능형 청킹
    MaxChunkSize = 512,
    OverlapSize = 64,
    PreserveStructure = true
});

// 결과 사용
foreach (var chunk in chunks)
{
    Console.WriteLine($"청크 {chunk.ChunkIndex}:");
    Console.WriteLine($"내용: {chunk.Content}");
    Console.WriteLine($"메타데이터: {chunk.Metadata.FileName}");
    Console.WriteLine("---");
}
```

### 3. LLM 통합 지능형 처리
```csharp
// OpenAI API 키 설정
Environment.SetEnvironmentVariable("OPENAI_API_KEY", "your-api-key");

// 서비스 설정 (SampleApp 참조)
services.AddFileFlux();
services.AddScoped<ITextCompletionService, OpenAiTextCompletionService>();

// 진행률 추적과 함께 처리
var progressiveProcessor = provider.GetRequiredService<ProgressiveDocumentProcessor>();

await foreach (var result in progressiveProcessor.ProcessWithProgressAsync(
    "technical-doc.md", 
    new ChunkingOptions { Strategy = "Intelligent" },
    new DocumentParsingOptions { UseLlm = true },
    CancellationToken.None))
{
    if (result.IsSuccess)
    {
        Console.WriteLine($"✅ 처리 완료: {result.Result?.Length}개 청크");
    }
    else
    {
        Console.WriteLine($"❌ 오류: {result.Error}");
    }
}
```

---

## 🏗️ 아키텍처

FileFlux는 Clean Architecture 원칙을 따르는 확장 가능한 설계입니다:

```
┌───────────────────────────────────────────────────────────┐
│                    LLM 통합 계층                            │
│  ITextCompletionService  │  ProgressiveDocumentProcessor  │ ← AI Integration
├─────────────────────────┼─────────────────────────────────┤
│  DocumentParserFactory  │    ChunkingStrategyFactory     │ ← Factory Pattern
├─────────────────────────┴─────────────────────────────────┤
│                 IDocumentProcessor                        │ ← Main Interface
├───────────────────────────────────────────────────────────┤
│                  DocumentProcessor                        │ ← Core Orchestrator
├─────────────────┬─────────────────────┬───────────────────┤
│  IDocumentReader │  IChunkingStrategy  │  IDocumentParser  │ ← Core Interfaces
├─────────────────┼─────────────────────┼───────────────────┤
│    Readers      │     Strategies      │     Parsers       │ ← Implementations
│ • PdfReader     │  • Intelligent     │ • BasicParser     │
│ • WordReader    │  • Semantic        │ • LlmParser       │
│ • ExcelReader   │  • Paragraph       │                   │
│ • PowerPoint    │  • FixedSize       │                   │
│ • TextReader    │                    │                   │
│ • JsonReader    │                    │                   │
│ • CsvReader     │                    │                   │
│ • MarkdownReader│                    │                   │
└─────────────────┴─────────────────────┴───────────────────┘
```

### 핵심 인터페이스
- **`IDocumentProcessor`**: 문서 처리 메인 인터페이스
- **`IDocumentReader`**: 8가지 파일 형식 전용 리더
  - Office 문서: `PdfReader`, `WordReader`, `ExcelReader`, `PowerPointReader`
  - 텍스트 문서: `TextReader`, `MarkdownReader`, `JsonReader`, `CsvReader`
- **`IChunkingStrategy`**: 4가지 지능형 청킹 전략
- **`IDocumentParser`**: LLM 기반 문서 구조 분석
- **`ITextCompletionService`**: LLM 서비스 추상화 (OpenAI 등)

---

## 📚 지원 형식 & 청킹 전략

### 지원 파일 형식
| Format | Extension | Reader | LLM 지능형 처리 | 특징 |
|--------|-----------|---------|----------------|------|
| **PDF** | `.pdf` | PdfDocumentReader | ✅ | 텍스트 추출, 구조 분석, 메타데이터 보존 |
| **Word** | `.docx` | WordDocumentReader | ✅ | 스타일, 헤더, 테이블, 이미지 캡션 추출 |
| **PowerPoint** | `.pptx` | PowerPointReader | ✅ | 슬라이드 콘텐츠, 노트, 제목 구조 분석 |
| **Excel** | `.xlsx` | ExcelDocumentReader | ✅ | 워크시트, 셀 데이터, 수식, 차트 정보 |
| **Markdown** | `.md` | TextDocumentReader | ✅ | 헤더, 코드 블록, 테이블 구조 보존 |
| **Text** | `.txt` | TextDocumentReader | ✅ | 일반 텍스트, 자동 인코딩 감지 |
| **JSON** | `.json` | JsonDocumentReader | ✅ | 구조화된 데이터, 스키마 추출 |
| **CSV** | `.csv` | CsvDocumentReader | ✅ | 테이블 데이터, 헤더 보존 |

### 청킹 전략
| 전략 | 특징 | 권장 사용처 |
|------|------|-------------|
| **Intelligent** | LLM 기반 의미적 경계 인식 | 기술 문서, 구조화된 콘텐츠 |
| **Semantic** | 문장 경계 기반 의미적 청킹 | 일반 문서, 에세이 |
| **Paragraph** | 단락 경계 보존 | Markdown, 구조적 문서 |
| **FixedSize** | 고정 크기 토큰 기반 | 균등한 처리가 필요한 경우 |

### LLM 최적화 기능
- **도메인 자동 인식**: Technical, Business, Academic, General
- **구조적 역할 분석**: Header, Table, Code, List, Content
- **OpenXML 구조 분석**: Word 스타일, Excel 셀 구조, PowerPoint 슬라이드 레이아웃
- **PDF 텍스트 추출**: 레이아웃 보존, 폰트 정보, 페이지 구조
- **품질 메트릭**: 신뢰도, 완성도, 일관성 점수
- **컨텍스트 헤더**: 청크별 구조화된 메타데이터

---

## 💡 사용 예제

### 다양한 문서 형식 처리
```csharp
var options = new ChunkingOptions
{
    Strategy = "Intelligent",      // 권장: LLM 기반 지능형
    MaxChunkSize = 512,           // 토큰 수 제한
    OverlapSize = 64,             // 청크 간 겹침
    PreserveStructure = true      // 문서 구조 보존
};

// PDF 문서 처리
var pdfChunks = await processor.ProcessAsync("report.pdf", options);

// Word 문서 처리 
var wordChunks = await processor.ProcessAsync("document.docx", options);

// Excel 파일 처리
var excelChunks = await processor.ProcessAsync("data.xlsx", options);

// PowerPoint 처리
var pptChunks = await processor.ProcessAsync("presentation.pptx", options);
```

### 파서 옵션 (LLM 사용)
```csharp
var parsingOptions = new DocumentParsingOptions
{
    UseLlm = true,                        // LLM 기반 구조 분석 활성화
    StructuringLevel = StructuringLevel.Medium  // 구조화 수준 설정
};

await foreach (var result in progressiveProcessor.ProcessWithProgressAsync(
    filePath, chunkingOptions, parsingOptions, CancellationToken.None))
{
    // 진행률 추적하며 처리
}
```

### RAG 시스템 통합
```csharp
public class RagService
{
    private readonly IDocumentProcessor _processor;
    private readonly IVectorStore _vectorStore;
    
    public async Task IndexDocumentAsync(string filePath)
    {
        // 1. FileFlux로 문서 처리
        var chunks = await _processor.ProcessAsync(filePath, new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 512,
            PreserveStructure = true
        });
        
        // 2. 벡터 DB에 저장
        foreach (var chunk in chunks)
        {
            await _vectorStore.StoreAsync(new VectorRecord
            {
                Id = chunk.Id,
                Content = chunk.Content,
                Metadata = chunk.Metadata
            });
        }
    }
}
```

## 🔧 확장성 및 개발

### 커스텀 리더 구현
```csharp
public class XmlDocumentReader : IDocumentReader
{
    public IEnumerable<string> SupportedExtensions => [".xml"];
    public bool CanRead(string fileName) => 
        Path.GetExtension(fileName).Equals(".xml", StringComparison.OrdinalIgnoreCase);
        
    public async Task<RawDocumentContent> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var xmlContent = await File.ReadAllTextAsync(filePath, cancellationToken);
        return new RawDocumentContent 
        { 
            Content = xmlContent,
            Metadata = new DocumentMetadata { FileName = Path.GetFileName(filePath) }
        };
    }
}

// 등록
services.AddSingleton<IDocumentReader, XmlDocumentReader>();
```

### 배치 처리 (다양한 형식)
```csharp
var documentPaths = Directory.GetFiles(@"C:\documents", "*.*")
    .Where(f => f.EndsWith(".pdf") || f.EndsWith(".docx") || 
                f.EndsWith(".pptx") || f.EndsWith(".xlsx") ||
                f.EndsWith(".md") || f.EndsWith(".txt"));

foreach (var path in documentPaths)
{
    var chunks = await processor.ProcessAsync(path, new ChunkingOptions
    {
        Strategy = "Intelligent",
        MaxChunkSize = 512
    });
    
    // 문서별 처리 결과
    Console.WriteLine($"{Path.GetFileName(path)}: {chunks.Length}개 청크");
    Console.WriteLine($"  형식: {Path.GetExtension(path).ToUpper()}");
    Console.WriteLine($"  총 텍스트: {chunks.Sum(c => c.Content.Length):N0}자");
}
```

## 🚀 로드맵

### 현재 버전 (v1.0)
- ✅ **8가지 파일 형식 완벽 지원**
  - **Office 문서**: PDF, DOCX, PPTX, XLSX
  - **텍스트 문서**: MD, TXT, JSON, CSV
- ✅ **4가지 청킹 전략 구현**
  - Intelligent (LLM 기반), Semantic, Paragraph, FixedSize
- ✅ **OpenXML & PDF 처리 엔진**
  - 네이티브 .NET 구현, 외부 의존성 최소화
- ✅ **LLM 통합 지능형 처리**
  - 문서 구조 분석, 도메인 인식, 품질 평가
- ✅ **Production Ready**
  - Clean Architecture, 확장 가능한 설계, 종합 테스트

### 계획된 기능 (v1.1)
- 📋 **고급 OCR 통합** - 이미지 기반 PDF 처리
- 📋 **테이블 추출 고도화** - Excel/Word 복잡한 표 구조
- 📋 **다국어 최적화** - 언어별 청킹 전략
- 📋 **스트리밍 처리** - 대용량 파일 메모리 효율화

## 🤝 기여하기

개발 환경 설정:
```bash
git clone https://github.com/iyulab/FileFlux.git
cd FileFlux
dotnet restore
dotnet build
dotnet test
```

## 📄 라이선스

MIT License - [LICENSE](LICENSE) 파일 참조

---

**FileFlux** - 문서를 RAG에 최적화된 청크로! 🚀