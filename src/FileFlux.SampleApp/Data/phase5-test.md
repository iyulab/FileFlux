# FileFlux Phase 5 테스트 문서

이것은 FileFlux Phase 5 지능형 문서 구조화 기능을 테스트하기 위한 샘플 문서입니다.

## 개요

Phase 5에서는 다음과 같은 기능을 제공합니다:

- LLM 기반 문서 구조 분석
- 지능형 청크 생성
- 품질 평가 및 메타데이터 추출

## 기술적 세부사항

### 아키텍처 설계

FileFlux는 다음과 같은 아키텍처를 따릅니다:

```csharp
public interface ILlmProvider
{
    Task<StructureAnalysisResult> AnalyzeStructureAsync(string prompt, DocumentType documentType);
    Task<ContentSummary> SummarizeContentAsync(string prompt, int maxLength);
}
```

### 구현 특징

1. **책임 분리**: FileFlux는 프롬프트 생성만 담당
2. **LLM 독립성**: 소비 애플리케이션에서 LLM 제공업체 선택
3. **선택적 지능형 처리**: LLM 없이도 기본 구조화 가능

## 사용 예제

다음은 Phase 5 기능을 사용하는 예제입니다:

```csharp
var processor = serviceProvider.GetService<IIntelligentDocumentProcessor>();
var result = await processor.ProcessAsync("document.md", options);

Console.WriteLine($"문서 타입: {result.DocumentType}");
Console.WriteLine($"청크 수: {result.Chunks.Count}");
```

## 결론

Phase 5는 FileFlux에 지능형 문서 처리 기능을 추가하여 더 정확하고 구조화된 청크를 생성할 수 있게 합니다.
