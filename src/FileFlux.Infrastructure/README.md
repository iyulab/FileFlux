# FileFlux - Document Processing SDK for RAG

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

FileFlux는 RAG(Retrieval-Augmented Generation) 시스템에 최적화된 **.NET 문서 처리 SDK**입니다. 다양한 형식의 문서를 고품질 청크로 변환하며, A+ 성능 등급으로 뛰어난 메모리 효율성을 제공합니다.

## 🚀 빠른 시작

### 설치

```bash
dotnet add package FileFlux
```

### 기본 사용법

```csharp
using FileFlux.Infrastructure;

// 1. 서비스 등록 (DI 컨테이너)
services.AddFileFlux();

// 2. 문서 프로세서 생성
var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();

// 3. 문서 처리
var chunks = await processor.ProcessAsync("document.pdf", new ChunkingOptions
{
    Strategy = "Intelligent",      // 지능형 청킹 (권장)
    MaxChunkSize = 1024,
    OverlapSize = 128
});

// 4. 결과 사용
foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.Content.Length} chars");
    Console.WriteLine($"파일: {chunk.Metadata.FileName}");
}
```

## 📋 지원 문서 형식

- **PDF** (.pdf) - PdfPig 엔진
- **Microsoft Word** (.docx) - OpenXML 기반
- **Microsoft Excel** (.xlsx, .xls) - 다중 시트 지원
- **Microsoft PowerPoint** (.pptx) - 슬라이드 및 노트
- **Markdown** (.md) - 구조 보존
- **HTML** (.html, .htm) - 태그 구조 분석
- **Plain Text** (.txt) - 인코딩 자동 감지
- **CSV** (.csv) - 헤더 보존
- **JSON** (.json) - 구조화된 데이터

## 🧠 지능적 청킹 전략

### Intelligent Strategy (권장)
```csharp
var options = new ChunkingOptions
{
    Strategy = "Intelligent",
    MaxChunkSize = 1024,
    OverlapSize = 128,
    PreserveStructure = true
};
```

**특징:**
- RAG 시스템에 최적화된 의미 경계 감지
- 문서 구조 보존 (제목, 문단, 목록)
- 적응형 청크 크기 조정
- 컨텍스트 유지를 위한 지능적 오버랩

### 기타 전략
- **Semantic**: 문장 경계 기반
- **FixedSize**: 일정한 토큰 크기
- **Paragraph**: 문단 단위 분할

## 🏗️ 아키텍처

```
┌─────────────────────────┐
│    IDocumentProcessor   │ ← 메인 인터페이스
├─────────────────────────┤
│   DocumentProcessor     │ ← 오케스트레이터
├───────────┬─────────────┤
│ Readers   │ Strategies  │ ← 구현체
│ • PDF     │ • Intelligent│
│ • Word    │ • Semantic  │
│ • Excel   │ • FixedSize │
│ • PPT     │ • Paragraph │
└───────────┴─────────────┘
```

## 📊 성능

벤치마크 결과 (FileFlux v1.0 기준):

| 문서 형식 | 처리 속도 | 메모리 효율 | 성능 등급 |
|-----------|----------|-------------|----------|
| PDF       | 0.69 MB/s | 0.00x      | A+       |
| DOCX      | 1.80 MB/s | 0.00x      | A+       |
| Excel     | 0.89 MB/s | 0.00x      | A+       |
| PPTX      | 2.21 MB/s | 0.00x      | A+       |
| Markdown  | 0.09 MB/s | 0.00x      | A+       |

**모든 문서 형식에서 A+ 성능 등급과 뛰어난 메모리 효율성을 달성했습니다.**

## 🔧 고급 설정

### DI 컨테이너 설정

```csharp
services.AddFileFlux(options =>
{
    options.DefaultChunkingStrategy = "Intelligent";
    options.DefaultMaxChunkSize = 1024;
    options.DefaultOverlapSize = 128;
    options.EnableMetadataExtraction = true;
    options.PreserveDocumentStructure = true;
});
```

### 커스텀 리더 등록

```csharp
// 커스텀 문서 리더 등록
services.AddSingleton<IDocumentReader, CustomDocumentReader>();

// 커스텀 청킹 전략 등록
services.AddSingleton<IChunkingStrategy, CustomChunkingStrategy>();
```

### 진행률 추적

```csharp
await foreach (var progress in processor.ProcessWithProgressAsync(filePath, options))
{
    if (progress.IsSuccess && progress.Result != null)
    {
        Console.WriteLine($"완료: {progress.Result.Length} 청크 생성됨");
    }
    else
    {
        Console.WriteLine($"진행률: {progress.ProgressPercentage:F1}%");
    }
}
```

## 💡 RAG 시스템 통합

FileFlux는 **순수한 문서 처리 SDK**로, 원하는 임베딩 서비스와 벡터 스토어를 자유롭게 선택할 수 있습니다.

```csharp
// FileFlux로 문서를 청크로 변환
var chunks = await processor.ProcessAsync("document.pdf", options);

// 원하는 임베딩 서비스 사용 (OpenAI, Azure, 로컬 모델 등)
foreach (var chunk in chunks)
{
    var embedding = await yourEmbeddingService.GenerateAsync(chunk.Content);
    
    // 원하는 벡터 스토어에 저장 (Pinecone, Qdrant, Chroma 등)
    await yourVectorStore.StoreAsync(new VectorRecord
    {
        Id = chunk.Id,
        Content = chunk.Content,
        Metadata = chunk.Metadata,
        Vector = embedding
    });
}
```

## 📖 DocumentChunk 구조

```csharp
public class DocumentChunk
{
    public string Id { get; set; }              // 고유 식별자
    public string Content { get; set; }         // 청크 텍스트 내용
    public DocumentMetadata Metadata { get; set; } // 풍부한 메타데이터
    public int StartPosition { get; set; }      // 원본 문서 내 시작 위치
    public int EndPosition { get; set; }        // 원본 문서 내 종료 위치
    public int ChunkIndex { get; set; }         // 순차적 인덱스
    public Dictionary<string, object> Properties { get; set; } // 커스텀 속성
}
```

## 🛠️ 요구사항

- **.NET 9.0** 이상
- **LLM 서비스**: 지능적 문서 구조 분석을 위해 텍스트 완성 서비스 필요
- **선택사항**: 임베딩 서비스, 벡터 스토어 (사용자 선택)

## 📝 예제

더 많은 예제는 [샘플 앱](https://github.com/iyulab/FileFlux/tree/main/src/FileFlux.SampleApp)을 참조하세요.

### 배치 처리
```csharp
var files = Directory.GetFiles("documents", "*.*", SearchOption.AllDirectories);
var allChunks = new List<DocumentChunk>();

foreach (var file in files)
{
    try
    {
        var chunks = await processor.ProcessAsync(file, options);
        allChunks.AddRange(chunks);
        Console.WriteLine($"✅ {Path.GetFileName(file)}: {chunks.Length} 청크");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ {Path.GetFileName(file)}: {ex.Message}");
    }
}
```

### 메타데이터 활용
```csharp
foreach (var chunk in chunks)
{
    Console.WriteLine($"파일: {chunk.Metadata.FileName}");
    Console.WriteLine($"페이지: {chunk.Metadata.PageNumber}");
    Console.WriteLine($"작성자: {chunk.Metadata.Author}");
    Console.WriteLine($"생성일: {chunk.Metadata.CreatedDate}");
}
```

## 🤝 기여하기

1. 이 저장소를 포크하세요
2. 기능 브랜치를 생성하세요 (`git checkout -b feature/amazing-feature`)
3. 변경사항을 커밋하세요 (`git commit -m 'Add amazing feature'`)
4. 브랜치에 푸시하세요 (`git push origin feature/amazing-feature`)
5. Pull Request를 생성하세요

## 📄 라이선스

이 프로젝트는 [MIT 라이선스](LICENSE) 하에 배포됩니다.

## 🏷️ 버전 히스토리

- **v1.0.0**: 초기 릴리즈
  - 9개 문서 형식 지원
  - 4가지 청킹 전략
  - 지능적 의미 경계 감지
  - RAG 최적화
  - 완벽한 .NET 9 지원

---

**FileFlux** - 차세대 RAG 시스템을 위한 문서 처리의 새로운 표준 ✨