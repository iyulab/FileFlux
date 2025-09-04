# FileFlux
> RAG 시스템을 위한 완전한 문서 처리 SDK

[![NuGet](https://img.shields.io/nuget/v/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![Downloads](https://img.shields.io/nuget/dt/FileFlux.svg)](https://www.nuget.org/packages/FileFlux)
[![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![📦 NuGet Package Build & Publish](https://github.com/iyulab/FileFlux/actions/workflows/nuget-publish.yml/badge.svg)](https://github.com/iyulab/FileFlux/actions/workflows/nuget-publish.yml)

## 🎯 개요

FileFlux는 문서를 RAG(Retrieval-Augmented Generation) 시스템에 최적화된 고품질 청크로 변환하는 **.NET 9 SDK**입니다. 다양한 문서 형식을 지원하며 지능적인 청킹 전략으로 최적의 RAG 성능을 제공합니다.

### ✨ 핵심 기능
- **📦 단일 NuGet 패키지**: `dotnet add package FileFlux`로 간편 설치
- **🤖 LLM 통합**: ITextCompletionService로 지능형 문서 분석
- **📄 광범위한 포맷 지원**: PDF, DOCX, PPTX, XLSX, MD, TXT, JSON, CSV
- **🎛️ 4가지 청킹 전략**: Intelligent, Semantic, Paragraph, FixedSize  
- **🏗️ Clean Architecture**: 인터페이스 중심 확장 가능 설계
- **🚀 Production Ready**: A+ 성능 등급, 자동 CI/CD 배포

---

## 🚀 빠른 시작

### 설치
```bash
dotnet add package FileFlux
```

### 기본 사용법
```csharp
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddFileFlux();

// 고품질 처리를 위한 LLM 서비스 주입
services.AddScoped<ITextCompletionService, YourLLMService>();

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();

// 문서를 청크로 변환
var chunks = await processor.ProcessAsync("document.pdf", new ChunkingOptions
{
    Strategy = "Intelligent",    // 지능형 청킹 (권장)
    MaxChunkSize = 1024,
    OverlapSize = 128
});

Console.WriteLine($"생성된 청크: {chunks.Length}개");
```

### 지원 문서 형식
- **PDF** (.pdf)
- **Word** (.docx)  
- **PowerPoint** (.pptx)
- **Excel** (.xlsx)
- **Markdown** (.md)
- **Text** (.txt), **JSON** (.json), **CSV** (.csv)

### 청킹 전략
- **Intelligent**: LLM 기반 지능형 의미 경계 감지 (권장, ITextCompletionService 필요)
- **Semantic**: 문장 경계 기반 청킹
- **Paragraph**: 단락 단위 분할  
- **FixedSize**: 고정 크기 토큰 기반

---

## 📚 문서 및 고급 사용법

더 자세한 정보는 다음 문서를 참조하세요:

- [📖 **튜토리얼**](docs/TUTORIAL.md) - 단계별 사용법 가이드
- [🏗️ **아키텍처**](docs/ARCHITECTURE.md) - 시스템 설계 및 확장성
- [🎯 **RAG 설계**](docs/RAG-DESIGN.md) - RAG 시스템 통합 가이드
- [📋 **문서 구조 사양**](docs/document-structure-specification.md) - 지원 형식 상세
- [🔧 **설계 원칙**](docs/design-principles.md) - 개발 철학 및 원칙
