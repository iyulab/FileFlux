# FileFlux 튜토리얼

**FileFlux**는 문서를 RAG 최적화 청크로 변환하는 .NET 9 SDK입니다.

## 📊 성능 및 품질

### 테스트 커버리지
- **235개 테스트 통과** (Release/Debug 모두)
- **8가지 파일 형식** 완벽 지원
- **6가지 청킹 전략** 검증 완료 (Phase 10 확장)
- **멀티모달 처리** (PDF 이미지 추출 → 텍스트 변환)

### 엔터프라이즈급 성능 (Phase 10 최적화)
- **3MB PDF**: 179개 청크, 1.0초 처리 (Smart 전략)
- **메모리 효율**: MemoryOptimizedIntelligent 전략으로 84% 메모리 절감
- **품질 향상**: Smart 전략 208% 품질 점수, 경계 품질 81% 달성
- **자동 최적화**: Auto 전략으로 문서별 최적 전략 자동 선택
- **병렬 처리 엔진**: CPU 코어별 동적 스케일링, 메모리 백프레셔 제어
- **스트리밍 최적화**: 실시간 청크 반환, LRU 캐시 시스템
- **Threading.Channels**: 고성능 비동기 채널 기반 백프레셔 시스템

## 🎛️ 청킹 전략 (Phase 10 확장)

### 전략 개요
- **Auto**: 문서 분석 후 최적 전략 자동 선택 (✨ Phase 10 신규, 권장)
- **Smart**: 문장 경계 기반 70% 완성도 보장 청킹 (✨ Phase 10 신규)
- **MemoryOptimizedIntelligent**: 메모리 최적화 지능형 청킹 (✨ Phase 10 신규, 84% 메모리 절감)
- **Intelligent**: LLM 기반 지능형 의미 경계 감지 (ITextCompletionService 필요)
- **Semantic**: 문장 경계 기반 청킹
- **Paragraph**: 단락 단위 분할  
- **FixedSize**: 고정 크기 토큰 기반

## 🚀 빠른 시작

### 1. 설치 및 설정

```bash
dotnet add package FileFlux
```

### 2. 기본 사용법

```csharp
using FileFlux; // 🎯 단일 네임스페이스로 모든 핵심 인터페이스 및 AddFileFlux 접근
using Microsoft.Extensions.DependencyInjection;

// DI 설정
var services = new ServiceCollection();

// 필수 LLM 서비스 등록 (소비 애플리케이션에서 구현)
services.AddScoped<ITextCompletionService, YourLLMService>();

// 선택사항: 이미지-텍스트 서비스 (멀티모달 처리용)
services.AddScoped<IImageToTextService, YourVisionService>();

// FileFlux 서비스 등록 (병렬 처리 및 스트리밍 엔진 포함)
services.AddFileFlux();
var provider = services.BuildServiceProvider();

var processor = provider.GetRequiredService<IDocumentProcessor>();

// 방법 1: 스트리밍 처리 (권장 - 메모리 효율적, 병렬 최적화)
await foreach (var result in processor.ProcessWithProgressAsync("document.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"📄 청크 {chunk.ChunkIndex}: {chunk.Content.Length}자");
            Console.WriteLine($"   품질점수: {chunk.Properties.GetValueOrDefault("QualityScore", "N/A")}");
        }
    }
}

// 방법 2: 기본 처리 (Phase 10 개선)
var chunks = await processor.ProcessAsync("document.pdf", new ChunkingOptions
{
    Strategy = "Auto",  // 자동 최적 전략 선택 (권장)
    MaxChunkSize = 512,
    OverlapSize = 64
});

foreach (var chunk in chunks)
{
    Console.WriteLine($"청크: {chunk.Content[..50]}...");
}
```

### 3. 멀티모달 처리 (텍스트 + 이미지)

```csharp
// OpenAI Vision 서비스 구현 예시 (소비 애플리케이션에서 구현)
public class OpenAiImageToTextService : IImageToTextService
{
    private readonly OpenAIClient _client;
    
    public OpenAiImageToTextService(string apiKey)
    {
        _client = new OpenAIClient(apiKey);
    }
    
    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient("gpt-4o-mini");
        
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("이미지에서 모든 텍스트를 정확히 추출하세요."),
            new UserChatMessage(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(imageData), "image/jpeg"))
        };
        
        var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            MaxOutputTokenCount = 1000,
            Temperature = 0.1f
        }, cancellationToken);
        
        return new ImageToTextResult
        {
            ExtractedText = response.Value.Content[0].Text,
            Confidence = 0.95,
            IsSuccess = true
        };
    }
}

// 서비스 등록 및 사용
services.AddScoped<IImageToTextService, OpenAiImageToTextService>();

// 이미지 포함 PDF 처리
await foreach (var result in processor.ProcessWithProgressAsync("document-with-images.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"📄 청크 {chunk.ChunkIndex}: {chunk.Content.Length}자");
            if (chunk.Properties.ContainsKey("HasImages"))
            {
                Console.WriteLine($"🖼️ 이미지 텍스트 추출 포함");
            }
        }
    }
}
```

### 4. LLM 통합 지능형 처리

```csharp
// LLM 서비스 주입 (고품질 처리를 위해 필수)
services.AddScoped<ITextCompletionService, YourLlmService>();

var processor = provider.GetRequiredService<IDocumentProcessor>();

// 방법 1: 직접 처리 (권장)
await foreach (var result in processor.ProcessWithProgressAsync("technical-doc.md", new ChunkingOptions 
{ 
    Strategy = "Intelligent" 
}))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"청크 {chunk.ChunkIndex}: {chunk.Content[..50]}...");
        }
    }
}

// 방법 2: 추출 후 처리 (캐싱/재사용 시)
var extractResult = await processor.ExtractAsync("technical-doc.md");
var parsedContent = await processor.ParseAsync(extractResult);
var chunks = await processor.ChunkAsync(parsedContent, new ChunkingOptions 
{ 
    Strategy = "Intelligent" 
});

foreach (var chunk in chunks)
{
    Console.WriteLine($"청크 {chunk.ChunkIndex}: {chunk.Content[..50]}...");
}
```

### Auto (권장, Phase 10 신규)
```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",          // 문서별 최적 전략 자동 선택
    MaxChunkSize = 512,         // RAG 최적화 크기
    OverlapSize = 64,           // 적응형 오버랩
};
```

### Smart (Phase 10 신규)
```csharp
var options = new ChunkingOptions
{
    Strategy = "Smart",         // 문장 경계 기반 70% 완성도 보장
    MaxChunkSize = 512,         // 경계 품질 81% 달성
    OverlapSize = 128,          // 컨텍스트 보존 강화
};
```

### MemoryOptimizedIntelligent (Phase 10 신규)
```csharp
var options = new ChunkingOptions
{
    Strategy = "MemoryOptimizedIntelligent",  // 84% 메모리 절감
    MaxChunkSize = 512,                       // 오브젝트 풀링 최적화
    OverlapSize = 64,                        // 스트림 처리
};
```

### 기타 전략들
```csharp
// LLM 기반 지능형 (기존)
new ChunkingOptions { Strategy = "Intelligent", MaxChunkSize = 512 };

// 단락 기반 (Markdown 최적화)
new ChunkingOptions { Strategy = "Paragraph", PreserveStructure = true };

// 문장 기반 의미적
new ChunkingOptions { Strategy = "Semantic", MaxChunkSize = 800 };

// 고정 크기 균등 분할
new ChunkingOptions { Strategy = "FixedSize", MaxChunkSize = 512 };
```

## 📊 지원 형식

| 형식 | 확장자 | 텍스트 추출 | 이미지 처리 | LLM 분석 | 품질 보증 |
|------|--------|------------|------------|----------|-----------|
| PDF | `.pdf` | ✅ | ✅ | ✅ | ✅ |
| Word | `.docx` | ✅ | 🔄 | ✅ | ✅ |
| Excel | `.xlsx` | ✅ | ❌ | ✅ | ✅ |
| PowerPoint | `.pptx` | ✅ | 🔄 | ✅ | ✅ |
| Markdown | `.md` | ✅ | ❌ | ✅ | ✅ |
| Text | `.txt` | ✅ | ❌ | ✅ | ✅ |
| JSON | `.json` | ✅ | ❌ | ✅ | ✅ |
| CSV | `.csv` | ✅ | ❌ | ✅ | ✅ |
| HTML | `.html` | ✅ | ✅ | ✅ | ✅ |

**범례**:
- ✅ 완전 지원 (테스트 검증 완료)
- 🔄 개발 예정
- ❌ 지원하지 않음

## 🧪 품질 검증 기능

### 청크 품질 분석
```csharp
// ChunkQualityEngine를 사용한 품질 메트릭 계산
var qualityEngine = provider.GetRequiredService<ChunkQualityEngine>();
var chunks = await processor.ProcessAsync("document.pdf");

var qualityMetrics = await qualityEngine.CalculateQualityMetricsAsync(chunks);
Console.WriteLine($"평균 완성도: {qualityMetrics.AverageCompleteness:P}");
Console.WriteLine($"콘텐츠 일관성: {qualityMetrics.ContentConsistency:P}");
Console.WriteLine($"경계 품질: {qualityMetrics.BoundaryQuality:P}");
Console.WriteLine($"크기 분포: {qualityMetrics.SizeDistribution:P}");
```

### 질문 생성 및 검증
```csharp
// RAG 시스템 품질 테스트를 위한 질문 생성
var parsedContent = await processor.ParseAsync(rawContent);
var questions = await qualityEngine.GenerateQuestionsAsync(parsedContent, 10);

foreach (var question in questions)
{
    Console.WriteLine($"Q: {question.Question}");
    Console.WriteLine($"   타입: {question.Type}");
    Console.WriteLine($"   난이도: {question.DifficultyScore:P}");
}

// 답변 가능성 검증
var validation = await qualityEngine.ValidateAnswerabilityAsync(questions, chunks);
Console.WriteLine($"답변 가능한 질문: {validation.AnswerableQuestions}/{validation.TotalQuestions}");
Console.WriteLine($"평균 신뢰도: {validation.AverageConfidence:P}");
```

## 🔧 고급 기능

### RAG 시스템 통합
```csharp
public class RagService
{
    private readonly IDocumentProcessor _processor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    
    public async Task IndexDocumentAsync(string filePath)
    {
        await foreach (var result in _processor.ProcessWithProgressAsync(filePath, new ChunkingOptions
        {
            Strategy = "Intelligent",
            MaxChunkSize = 512
        }))
        {
            if (result.IsSuccess && result.Result != null)
            {
                foreach (var chunk in result.Result)
                {
                    // RAG 파이프라인: 임베딩 생성 → 벡터 저장소 저장
                    var embedding = await _embeddingService.GenerateAsync(chunk.Content);
                    await _vectorStore.StoreAsync(new {
                        Id = chunk.Id,
                        Content = chunk.Content,
                        Metadata = chunk.Metadata,
                        Vector = embedding
                    });
                }
            }
        }
    }
}
```

## 🎯 RAG 통합 예시

```csharp
// 완전한 RAG 파이프라인 예시
var options = new ChunkingOptions
{
    Strategy = "Intelligent",
    MaxChunkSize = 512,
    OverlapSize = 64,
    PreserveStructure = true
};

await foreach (var result in processor.ProcessWithProgressAsync("document.pdf", options))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
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
    
    // 진행률 표시
    if (result.Progress != null)
    {
        Console.WriteLine($"진행률: {result.Progress.PercentComplete:F1}%");
    }
}
```

## 📁 상세 지원 형식

### Office 문서
- **PDF** (`.pdf`): 텍스트 + 이미지 처리, 구조 인식, 메타데이터 보존
- **Word** (`.docx`): 스타일 인식, 헤더/표/이미지 캡션 추출
- **Excel** (`.xlsx`): 다중 시트 지원, 수식 추출, 테이블 구조 분석
- **PowerPoint** (`.pptx`): 슬라이드 콘텐츠, 노트, 제목 구조 추출

### 텍스트 문서
- **Markdown** (`.md`): Markdig 기반 헤더/코드블록/테이블 구조 보존
- **Text** (`.txt`): 일반 텍스트, 자동 인코딩 감지
- **JSON** (`.json`): 구조화된 데이터 플래튼화, 스키마 추출
- **CSV** (`.csv`): CsvHelper 기반 테이블 데이터, 헤더 보존

## ⚙️ 청킹 전략 (Phase 10 확장)

| 전략 | 특징 | 최적 사용 케이스 | 품질 점수 | Phase 10 |
|------|------|-----------------|----------|----------|
| **Auto** (권장) | 문서별 최적 전략 자동 선택 | 모든 문서 형식 | ⭐⭐⭐⭐⭐ | ✨ 신규 |
| **Smart** | 70% 완성도 보장, 81% 경계 품질 | 법률, 의료, 학술 문서 | ⭐⭐⭐⭐⭐ | ✨ 신규 |
| **MemoryOptimizedIntelligent** | 84% 메모리 절감, 오브젝트 풀링 | 대용량 문서, 서버 환경 | ⭐⭐⭐⭐⭐ | ✨ 신규 |
| **Intelligent** | LLM 기반 의미 단위 청킹 | 기술 문서, API 문서 | ⭐⭐⭐⭐⭐ | 기존 |
| **Semantic** | 문장 경계 기준 청킹 | 일반 문서, 논문 | ⭐⭐⭐⭐ | 기존 |
| **Paragraph** | 단락 단위 청킹 | Markdown, 블로그 | ⭐⭐⭐⭐ | 기존 |
| **FixedSize** | 고정 크기 청킹 | 균일한 처리 필요 | ⭐⭐⭐ | 기존 |

## 📄 단계별 처리

```csharp
// 1단계: 텍스트 추출만 (Reader 단계)
var rawContent = await processor.ExtractAsync("document.pdf");
Console.WriteLine($"원본 텍스트: {rawContent.Content.Length}자");

// 2단계: 구조화 처리 (Parser 단계 - LLM 사용)
var parsedContent = await processor.ParseAsync(rawContent);
Console.WriteLine($"구조화된 섹션: {parsedContent.Sections?.Count ?? 0}개");

// 3단계: 청킹만 실행 (Chunking 단계) - Phase 10 개선
var chunks = await processor.ChunkAsync(parsedContent, new ChunkingOptions
{
    Strategy = "Auto",  // 자동 최적 전략 선택
    MaxChunkSize = 512,
    OverlapSize = 64
});
Console.WriteLine($"생성된 청크: {chunks.Count()}개");

// 통합 처리 (권장)
await foreach (var result in processor.ProcessWithProgressAsync("document.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        Console.WriteLine($"처리 완료: {result.Result.Length}개 청크");
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"  청크 {chunk.ChunkIndex}: {chunk.Content.Length}자");
        }
    }
}
```

## ❌ 오류 처리

```csharp
try
{
    var chunks = new List<DocumentChunk>();
    await foreach (var result in processor.ProcessWithProgressAsync("document.pdf"))
    {
        if (result.IsSuccess && result.Result != null)
        {
            chunks.AddRange(result.Result);
        }
        else if (!string.IsNullOrEmpty(result.Error))
        {
            Console.WriteLine($"오류: {result.Error}");
        }
    }
}
catch (UnsupportedFileFormatException ex)
{
    Console.WriteLine($"지원되지 않는 파일 형식: {ex.FileName}");
}
catch (DocumentProcessingException ex)
{
    Console.WriteLine($"문서 처리 오류: {ex.Message}");
    Console.WriteLine($"파일: {ex.FileName}");
}
catch (FileNotFoundException)
{
    Console.WriteLine("파일을 찾을 수 없습니다.");
}

// 스트리밍에서 오류 처리
await foreach (var result in processor.ProcessWithProgressAsync("document.pdf"))
{
    if (!result.IsSuccess)
    {
        Console.WriteLine($"처리 실패: {result.Error}");
        continue; // 다음 청크 처리 계속
    }
    
    // 성공한 결과 처리
    if (result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"청크 {chunk.ChunkIndex} 처리 완료");
        }
    }
}
```

## 🎨 사용자 정의

### 커스텀 청킹 전략
```csharp
public class CustomChunkingStrategy : IChunkingStrategy
{
    public string StrategyName => "Custom";
    
    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        ParsedDocumentContent content, 
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        
        // 커스텀 청킹 로직 구현
        var sentences = content.Content.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var chunkIndex = 0;
        
        foreach (var sentence in sentences)
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = sentence.Trim(),
                ChunkIndex = chunkIndex++,
                Metadata = content.Metadata,
                StartPosition = 0, // 실제 구현에서는 정확한 위치 계산
                EndPosition = sentence.Length,
                Properties = new Dictionary<string, object>
                {
                    ["CustomScore"] = CalculateCustomScore(sentence)
                }
            });
        }
        
        return chunks;
    }
    
    private double CalculateCustomScore(string text)
    {
        // 커스텀 품질 점수 계산 로직
        return text.Length > 50 ? 0.8 : 0.5;
    }
}

// 등록
services.AddTransient<IChunkingStrategy, CustomChunkingStrategy>();
```

### 커스텀 Document Reader
```csharp
public class CustomDocumentReader : IDocumentReader
{
    public string ReaderType => "CustomReader";
    public IEnumerable<string> SupportedExtensions => [".custom"];
    
    public bool CanRead(string fileName) => 
        Path.GetExtension(fileName).Equals(".custom", StringComparison.OrdinalIgnoreCase);
    
    public async Task<RawDocumentContent> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        
        return new RawDocumentContent
        {
            Content = content,
            Metadata = new DocumentMetadata
            {
                FileName = Path.GetFileName(filePath),
                FileType = "Custom",
                ProcessedAt = DateTime.UtcNow,
                Properties = new Dictionary<string, object>
                {
                    ["CustomProperty"] = "CustomValue"
                }
            }
        };
    }
}

// 등록
services.AddTransient<IDocumentReader, CustomDocumentReader>();
```

### 커스텀 이미지-텍스트 서비스
```csharp
public class CustomImageToTextService : IImageToTextService
{
    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData, 
        ImageToTextOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        // 커스텀 이미지 텍스트 추출 로직
        // 예: Tesseract OCR, Azure Computer Vision, Google Cloud Vision 등
        
        await Task.Delay(100, cancellationToken); // 모의 처리 시간
        
        return new ImageToTextResult
        {
            ExtractedText = "커스텀 이미지에서 추출된 텍스트",
            Confidence = 0.85,
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["ProcessingTime"] = 100,
                ["ImageSize"] = imageData.Length
            }
        };
    }
}

// 등록
services.AddScoped<IImageToTextService, CustomImageToTextService>();
```

---

## 📚 관련 문서

- [✨ **Phase 10 기능**](PHASE_10_FEATURES.md) - 최신 지능형 기능 상세 가이드
- [🏗️ **아키텍처**](ARCHITECTURE.md) - 시스템 설계 및 확장성
- [🎯 **RAG 설계**](RAG-DESIGN.md) - RAG 시스템 통합 가이드
- [📋 **GitHub Repository**](https://github.com/iyulab/FileFlux) - 소스 코드 및 이슈 트래킹