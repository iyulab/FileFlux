# FileFlux 튜토리얼

**FileFlux**는 문서를 RAG 최적화 청크로 변환하는 .NET 9 SDK입니다.

## 🚀 빠른 시작

### 1. 설치 및 설정

```bash
# 프로젝트 클론
git clone https://github.com/your-org/FileFlux.git
cd FileFlux
dotnet build
```

### 2. 기본 사용법

```csharp
using FileFlux.Core;
using FileFlux.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

// DI 설정
var services = new ServiceCollection();
services.AddFileFlux();
var provider = services.BuildServiceProvider();

var processor = provider.GetRequiredService<IDocumentProcessor>();

// 문서 처리
var chunks = await processor.ProcessAsync("document.md", new ChunkingOptions
{
    Strategy = "Intelligent",    // LLM 기반 지능형
    MaxChunkSize = 512,         // 토큰 제한
    OverlapSize = 64,          // 청크 간 겹침
    PreserveStructure = true   // 구조 보존
});

foreach (var chunk in chunks)
{
    Console.WriteLine($"청크 {chunk.ChunkIndex}: {chunk.Content.Length}자");
}
```

### 3. LLM 통합 지능형 처리

```csharp
// OpenAI 설정 (환경변수 필요)
Environment.SetEnvironmentVariable("OPENAI_API_KEY", "your-api-key");

services.AddScoped<ITextCompletionService, OpenAiTextCompletionService>();

// 진행률 추적 처리
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
        Console.WriteLine($"❌ 처리 오류: {result.Error}");
    }
}
```

### 4. CLI 도구 사용

```bash
# SampleApp을 사용한 CLI 작업
cd src/FileFlux.SampleApp

# 문서 처리
dotnet run -- process "test.md" --strategy Intelligent

# 진행률 추적 처리
dotnet run -- process-progress "test.md" --strategy Intelligent

# RAG 검색
dotnet run -- query "기술 문서 관련 질문" --top-k 5

# 저장된 문서 목록
dotnet run -- list

# 쿼리 히스토리
dotnet run -- history --limit 10
```

## 🎛️ 청킹 전략

### Intelligent (권장)
```csharp
var options = new ChunkingOptions
{
    Strategy = "Intelligent",     // LLM 기반 의미적 분석
    MaxChunkSize = 512,          // RAG 최적화 크기
    OverlapSize = 64,           // 15% 겹침 비율
    PreserveStructure = true    // 문서 구조 보존
};
```

### 기타 전략들
```csharp
// 단락 기반 (Markdown 최적화)
new ChunkingOptions { Strategy = "Paragraph", PreserveStructure = true };

// 문장 기반 의미적
new ChunkingOptions { Strategy = "Semantic", MaxChunkSize = 800 };

// 고정 크기 균등 분할
new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 512 };
```

## 📊 지원 형식

| 형식 | 확장자 | 리더 | LLM 처리 |
|------|--------|------|---------|
| Markdown | `.md` | ✅ | ✅ |
| Text | `.txt` | ✅ | ✅ |
| JSON | `.json` | ✅ | ✅ |
| CSV | `.csv` | ✅ | ✅ |

## 🔧 고급 기능

### RAG 시스템 통합
```csharp
public class RagService
{
    private readonly IDocumentProcessor _processor;
    
    public async Task IndexDocumentAsync(string filePath)
    {
        var chunks = await _processor.ProcessAsync(filePath, new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 512,
            PreserveStructure = true
        });
        
        foreach (var chunk in chunks)
        {
            // 임베딩 생성 후 벡터 DB 저장
            var embedding = await GenerateEmbedding(chunk.Content);
            await StoreInVectorDB(chunk, embedding);
        }
    }
}
```

## 🎯 RAG 통합

```csharp
await foreach (var result in processor.ProcessAsync("document.pdf", options))
{
    if (result.Result != null)
    {
        var chunk = result.Result;
        
        // 임베딩 생성 + 벡터 저장
        var embedding = await embeddingService.GenerateAsync(chunk.Content);
        await vectorStore.StoreAsync(chunk.Id, chunk.Content, embedding);
    }
}
```

## 📁 지원 형식

| 형식 | 확장자 | 
|------|--------|
| PDF | `.pdf` | 
| 텍스트 | `.txt`, `.md` |
| JSON | `.json` |
| CSV | `.csv` |

## ⚙️ 청킹 전략

| 전략 | 특징 |
|------|------|
| **Intelligent** (권장) | RAG 최적화된 의미 단위 청킹 |
| **Semantic** | 문장 경계 기준 청킹 |
| **Paragraph** | 단락 단위 청킹 |
| **FixedSize** | 고정 크기 청킹 |

## 📄 단계별 처리

```csharp
// 텍스트 추출만
var rawContent = await processor.ExtractTextAsync("document.pdf");

// 구조화 처리
var parsedContent = await processor.ParseAsync(rawContent);

// 청킹만 실행
var chunks = await processor.ChunkAsync(parsedContent, options);
```

## ❌ 오류 처리

```csharp
try
{
    var chunks = await processor.ProcessToArrayAsync("document.pdf");
}
catch (UnsupportedFileFormatException)
{
    // 지원되지 않는 형식
}
catch (DocumentProcessingException)
{
    // 처리 오류
}
```

## 🎨 사용자 정의

```csharp
public class CustomStrategy : IChunkingStrategy
{
    public string StrategyName => "Custom";
    
    public async Task<DocumentChunk[]> ChunkAsync(
        ParsedDocumentContent content, 
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        // 구현
        return chunks.ToArray();
    }
}

// 등록
services.AddSingleton<IChunkingStrategy, CustomStrategy>();
```

---

**📚 추가 정보**: [GitHub Repository](https://github.com/iyulab/FileFlux) | [API 문서](ARCHITECTURE.md)