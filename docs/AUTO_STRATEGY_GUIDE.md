# Auto 전략 가이드 - 자동 적응형 청킹

## 📌 개요

FileFlux v0.2.0부터 **Auto 전략**이 기본값으로 설정됩니다. Auto 전략은 LLM이 문서를 분석하여 자동으로 최적의 청킹 전략을 선택하는 지능형 시스템입니다.

## 🎯 문제 해결

### 기존 문제점
- **선택 부담**: 6가지 전략 중 어떤 것을 선택해야 할지 모호함
- **전문 지식 필요**: 각 전략의 특성을 이해해야만 적절한 선택 가능
- **문서별 최적화 부재**: 다양한 문서 타입에 대한 수동 조정 필요

### Auto 전략의 해결책
- **자동 선택**: 문서 분석 후 최적 전략 자동 결정
- **적응형**: 문서 타입, 구조, 내용에 따라 동적 선택
- **간편함**: 별도 설정 없이 기본값으로 최적 성능

## 🚀 사용 방법

### 기본 사용 (설정 불필요)
```csharp
// Auto가 기본값이므로 별도 설정 불필요
var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();
var chunks = await processor.ProcessAsync("document.pdf");
```

### 명시적 Auto 사용
```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",  // 명시적 지정 (선택사항)
    MaxChunkSize = 512,
    OverlapSize = 64
};

var chunks = await processor.ProcessAsync("document.pdf", options);
```

### 특정 전략 강제 (테스트/디버깅)
```csharp
var options = new ChunkingOptions
{
    Strategy = "Smart",  // Auto 대신 특정 전략 강제
    MaxChunkSize = 512
};
```

## 🔍 Auto 전략의 작동 방식

### 1단계: 문서 분석
```
파일 확장자 → 샘플 추출 (2000토큰) → 특성 분석
```

### 2단계: 특성 감지
- **구조적 특징**: Markdown 헤더, 코드 블록, 테이블, 리스트
- **콘텐츠 타입**: 기술문서, 법률문서, 의료문서, 일반문서
- **텍스트 특성**: 평균 문장 길이, 단락 수, 구조 복잡도

### 3단계: LLM 분석
```
문서 특성 + 전략 메타데이터 → LLM 프롬프트 → 최적 전략 추천
```

### 4단계: 전략 선택
```
신뢰도 확인 → 검증 → 최종 선택 → 청킹 수행
```

## 📊 전략 선택 매트릭스

| 문서 타입 | 선택되는 전략 | 이유 |
|----------|--------------|------|
| 법률/의료 문서 | **Smart** | 문장 완성도 critical |
| 기술 문서 (코드 포함) | **Intelligent** | 구조 보존 중요 |
| 블로그/기사 | **Semantic** | 의미적 흐름 유지 |
| 소설/책 | **Paragraph** | 자연스러운 단락 |
| 로그 파일 | **FixedSize** | 균일한 크기 필요 |
| 알 수 없음 | **Smart** | 안전한 기본값 |

## 🎨 커스텀 전략 등록

### 커스텀 전략 생성
```csharp
public class MyCustomStrategy : IChunkingStrategy
{
    public string StrategyName => "MyCustom";
    
    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken)
    {
        // 커스텀 청킹 로직
    }
}
```

### 메타데이터와 함께 등록
```csharp
services.AddFileFlux();

// 커스텀 전략 등록
services.AddSingleton<IChunkingStrategy, MyCustomStrategy>();

// 팩토리에 메타데이터 추가
services.Configure<ChunkingStrategyOptions>(options =>
{
    options.RegisterMetadata("MyCustom", new ChunkingStrategyMetadata
    {
        Description = "특수 목적 청킹 전략",
        OptimalForDocumentTypes = new[] { "Special" },
        Strengths = new[] { "특수 처리 최적화" },
        PriorityScore = 80
    });
});
```

## 📈 성능 최적화 옵션

### 속도 우선 모드
```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",
    StrategyOptions = new Dictionary<string, object>
    {
        ["PreferSpeed"] = true  // FixedSize, Paragraph 선호
    }
};
```

### 품질 우선 모드
```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",
    StrategyOptions = new Dictionary<string, object>
    {
        ["PreferQuality"] = true  // Smart, Intelligent 선호
    }
};
```

### 분석 시간 제한
```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",
    StrategyOptions = new Dictionary<string, object>
    {
        ["MaxAnalysisTime"] = 5  // 5초 제한
    }
};
```

## 🔧 디버깅 및 모니터링

### 선택된 전략 확인
```csharp
var chunks = await processor.ProcessAsync("document.pdf");
var firstChunk = chunks.First();

// Auto가 선택한 전략 확인
var selectedStrategy = firstChunk.Properties["AutoSelectedStrategy"];  // "Smart"
var reasoning = firstChunk.Properties["SelectionReasoning"];  // 선택 이유
var confidence = firstChunk.Properties["SelectionConfidence"];  // 0.85
```

### 로깅 활성화
```csharp
services.AddLogging(builder =>
{
    builder.AddFilter("FileFlux.Infrastructure.Strategies.AdaptiveStrategySelector", LogLevel.Debug);
});
```

## 📋 전략별 특징 요약

### Auto (기본값)
- **장점**: 자동 최적화, 설정 불필요, 적응형
- **단점**: 초기 분석 시간 (1-2초), LLM 필요
- **사용**: 모든 상황 (기본값)

### Smart
- **장점**: 70% 완성도 보장, 문장 무결성
- **단점**: 약간 느림
- **사용**: 법률/의료/Q&A

### Intelligent
- **장점**: 구조 보존, 코드 블록 유지
- **단점**: 문장 중단 가능
- **사용**: 기술 문서

### Semantic
- **장점**: 의미적 일관성
- **단점**: 가변 크기
- **사용**: 내러티브 콘텐츠

### Paragraph
- **장점**: 빠름, 자연스러움
- **단점**: 단순함
- **사용**: 소설/책

### FixedSize
- **장점**: 예측 가능, 매우 빠름
- **단점**: 의미 단위 무시
- **사용**: 로그/데이터

## ❓ FAQ

### Q: Auto 전략이 실패하면?
A: Smart 전략으로 자동 폴백됩니다.

### Q: LLM 없이 사용 가능한가요?
A: 가능합니다. 규칙 기반 폴백이 작동합니다.

### Q: 특정 전략을 강제하려면?
A: `Strategy = "Smart"` 등으로 직접 지정하세요.

### Q: 성능 오버헤드는?
A: 초기 분석에 1-2초 추가, 이후 동일합니다.

### Q: 커스텀 전략도 Auto가 선택하나요?
A: 네, 메타데이터를 등록하면 선택 대상이 됩니다.

## 🚦 마이그레이션 가이드

### v0.1.x → v0.2.0
```csharp
// 이전 (명시적 전략 지정)
var options = new ChunkingOptions
{
    Strategy = "Intelligent"  // 수동 선택
};

// 이후 (Auto 기본값)
var options = new ChunkingOptions();  // Auto가 자동 선택
```

### 기존 코드 호환성
- 모든 기존 전략 이름 그대로 사용 가능
- Auto는 추가 옵션, 기존 코드 영향 없음
- 명시적 전략 지정 시 Auto 무시

## 📚 참고 자료

- [전략별 상세 가이드](CHUNKING_STRATEGIES.md)
- [Phase 9 리포트](PHASE_9_REPORT.md)
- [RAG 품질 벤치마크](../samples/FileFlux.RealWorldBenchmark/README.md)