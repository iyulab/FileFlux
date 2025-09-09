# FileFlux
> RAG 시스템을 위한 완전한 문서 처리 SDK

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![📦 NuGet Package Build & Publish](https://github.com/iyulab/FileFlux/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/iyulab/FileFlux/actions/workflows/nuget-publish.yml)

## 🎯 개요

**FileFlux는 순수 RAG 전처리 SDK입니다** - 문서를 RAG 시스템에 최적화된 구조화된 청크로 변환하는 **.NET 9 SDK**입니다.

### 🏗️ 아키텍처 원칙: 인터페이스 제공자

FileFlux는 **인터페이스를 정의하고, 소비 애플리케이션이 구현체를 선택**하는 명확한 책임 분리를 따릅니다:

#### ✅ FileFlux가 제공하는 것:
- **📄 문서 파싱**: PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV → 구조화된 텍스트
- **🔌 AI 인터페이스**: ITextCompletionService, IImageToTextService 계약 정의
- **🎛️ 처리 파이프라인**: Reader → Parser → Chunking 오케스트레이션
- **🧪 Mock 서비스**: 테스트용 MockTextCompletionService, MockImageToTextService

#### ❌ FileFlux가 제공하지 않는 것:
- **AI 서비스 구현**: OpenAI, Anthropic, Azure 등 특정 공급자 구현 없음
- **벡터 생성**: 임베딩 생성은 소비 앱의 책임  
- **데이터 저장**: Pinecone, Qdrant 등 벡터 DB 구현 없음

### ✨ 핵심 특징
- **📦 단일 NuGet 패키지**: `dotnet add package FileFlux`로 간편 설치
- **🎯 Clean Interface**: AI 공급자에 종속되지 않는 순수한 인터페이스 설계
- **🖼️ 멀티모달 처리**: 텍스트 + 이미지 → 통합 텍스트 변환
- **🎛️ 6가지 청킹 전략**: Auto, Smart, Intelligent, MemoryOptimizedIntelligent, Semantic, Paragraph, FixedSize  
- **🏗️ Clean Architecture**: 의존성 역전으로 확장성 보장
- **⚡ 병렬 처리 엔진**: CPU 코어별 동적 스케일링, 메모리 백프레셔 제어
- **📊 스트리밍 최적화**: 실시간 청크 반환, 지능형 LRU 캐시
- **🧠 Phase 10 개선**: Auto 전략 선택, Smart 경계 품질, 메모리 최적화 (84% 절감)
- **🚀 Production Ready**: 235개 테스트 통과, 엔터프라이즈급 성능

---

## 🚀 빠른 시작

### 설치
```bash
dotnet add package FileFlux
```

### 기본 사용법 (RAG 통합)
```csharp
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// 필수 서비스 등록 (소비 애플리케이션에서 구현)
services.AddScoped<ITextCompletionService, YourLLMService>();        // LLM 서비스
services.AddScoped<IEmbeddingService, YourEmbeddingService>();      // 임베딩 서비스
services.AddScoped<IVectorStore, YourVectorStore>();                // 벡터 저장소

// 선택사항: 이미지-텍스트 서비스 (멀티모달 처리용)
services.AddScoped<IImageToTextService, YourVisionService>();

// FileFlux 서비스 등록 (병렬 처리 및 스트리밍 엔진 포함)
services.AddFileFlux();

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();
var embeddingService = provider.GetRequiredService<IEmbeddingService>();
var vectorStore = provider.GetRequiredService<IVectorStore>();

// 스트리밍 처리 (권장 - 메모리 효율적, 병렬 최적화)
await foreach (var result in processor.ProcessWithProgressAsync("document.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"📄 청크 {chunk.ChunkIndex}: {chunk.Content.Length}자");
            
            // RAG 파이프라인: 임베딩 생성 → 벡터 저장소 저장
            var embedding = await embeddingService.GenerateAsync(chunk.Content);
            await vectorStore.StoreAsync(new {
                Id = chunk.Id,
                Content = chunk.Content,
                Metadata = chunk.Metadata,
                Vector = embedding
            });
        }
    }
}
```

### 단계별 처리 (고급 사용법)
```csharp
// 각 단계를 개별적으로 제어하고 싶을 때 사용

// 1단계: 텍스트 추출 (Reader)
var rawContent = await processor.ExtractAsync("document.pdf");
Console.WriteLine($"추출된 텍스트: {rawContent.Content.Length}자");

// 2단계: 구조 분석 (Parser with LLM)
var parsedContent = await processor.ParseAsync(rawContent);
Console.WriteLine($"구조화된 섹션: {parsedContent.Sections?.Count ?? 0}개");

// 3단계: 청킹 (Chunking Strategy) - Phase 10 개선
var chunks = await processor.ChunkAsync(parsedContent, new ChunkingOptions
{
    Strategy = "Auto",  // 자동 최적 전략 선택 (권장)
    MaxChunkSize = 512,
    OverlapSize = 64
});

Console.WriteLine($"생성된 청크: {chunks.Length}개");

// 4단계: RAG 파이프라인 (임베딩 → 저장)
foreach (var chunk in chunks)
{
    var embedding = await embeddingService.GenerateAsync(chunk.Content);
    await vectorStore.StoreAsync(new {
        Id = chunk.Id,
        Content = chunk.Content,
        Metadata = chunk.Metadata,
        Vector = embedding
    });
}
```

### 지원 문서 형식
- **PDF** (.pdf)
- **Word** (.docx)  
- **PowerPoint** (.pptx)
- **Excel** (.xlsx)
- **Markdown** (.md)
- **Text** (.txt), **JSON** (.json), **CSV** (.csv)

---

## ⚡ 엔터프라이즈급 성능 최적화

### 🚀 병렬 처리 엔진
- **CPU 코어별 동적 스케일링**: 시스템 리소스에 맞춘 자동 확장
- **메모리 백프레셔 제어**: Threading.Channels 기반 고성능 비동기 처리
- **지능형 작업 분산**: 파일 크기와 복잡도에 따른 최적 분배

### 📊 스트리밍 최적화  
- **실시간 청크 반환**: AsyncEnumerable 기반 즉시 결과 제공
- **LRU 캐시 시스템**: 파일 해시 기반 자동 캐싱 및 만료 관리
- **캐시 우선 검사**: 동일 문서 재처리 시 즉시 반환

### 📈 검증된 성능 지표 (Phase 10 개선)
- **처리 속도**: 3MB PDF → 179청크, 1.0초 처리 (Smart 전략)
- **메모리 효율**: 파일 크기 2배 이하 메모리 사용 (MemoryOptimizedIntelligent: 84% 절감)
- **품질 향상**: Smart 전략 208% 품질 점수, 경계 품질 81% 달성
- **자동 최적화**: Auto 전략으로 문서별 최적 전략 자동 선택
- **병렬 확장**: CPU 코어 수에 따른 선형 성능 향상
- **캐시 효율**: 재처리 시 95% 이상 응답 시간 단축

---

## 📚 문서 및 고급 사용법

더 자세한 정보는 다음 문서를 참조하세요:

- [📖 **튜토리얼**](docs/TUTORIAL.md) - 단계별 사용법 가이드
- [✨ **Phase 10 기능**](docs/PHASE_10_FEATURES.md) - 최신 지능형 기능 상세 가이드
- [🏗️ **아키텍처**](docs/ARCHITECTURE.md) - 시스템 설계 및 확장성
- [🎯 **RAG 설계**](docs/RAG-DESIGN.md) - RAG 시스템 통합 가이드
- [📋 **문서 구조 사양**](docs/document-structure-specification.md) - 지원 형식 상세
- [🔧 **설계 원칙**](docs/design-principles.md) - 개발 철학 및 원칙
