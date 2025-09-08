# FileFlux RAG Quality Plan

## 🎯 목표: RAG 검색 품질 극대화를 위한 청킹 최적화

FileFlux는 **RAG 준비**에만 집중합니다. 임베딩, 저장, 검색, 그래프는 소비 애플리케이션의 책임입니다.

## 📊 RAG 품질 메트릭 (측정 가능한 목표)

### 1. 검색 재현율 (Retrieval Recall)
- **목표**: 85% 이상
- **측정 방법**: 관련 청크가 상위 K개 결과에 포함되는 비율
- **테스트**: `RAGQualityBenchmark.TestRetrievalRecall()`

### 2. 청크 완전성 (Chunk Completeness)
- **목표**: 90% 이상
- **측정 방법**: 청크가 독립적으로 이해 가능한 정보 포함 비율
- **테스트**: `ChunkCompletenessTests.TestStandaloneReadability()`

### 3. 컨텍스트 보존율 (Context Preservation)
- **목표**: 95% 이상
- **측정 방법**: 원본 문서의 의미가 청크에 보존되는 비율
- **테스트**: `ContextPreservationTests.TestSemanticIntegrity()`

### 4. 경계 정확도 (Boundary Accuracy)
- **목표**: 90% 이상
- **측정 방법**: 의미적 경계가 올바르게 감지되는 비율
- **테스트**: `BoundaryDetectionTests.TestSemanticBoundaries()`

## 🧪 테스트 기반 구현 계획

### Phase 11-A: RAG 품질 테스트 인프라 구축

#### 1. RAGQualityBenchmark 테스트 스위트
```csharp
public class RAGQualityBenchmark
{
    [Fact]
    public async Task TestRetrievalRecall_ShouldAchieve85Percent()
    {
        // Arrange: 실제 QA 쌍과 문서 준비
        var testData = LoadRAGBenchmarkData();
        var processor = new DocumentProcessor();
        
        // Act: 문서 청킹
        var chunks = await processor.ProcessAsync(testData.Document);
        
        // Assert: 검색 재현율 측정
        var recall = CalculateRetrievalRecall(testData.Questions, chunks);
        Assert.True(recall >= 0.85, $"Recall {recall:P} is below 85%");
    }
    
    [Theory]
    [InlineData("technical", 500, 800)]  // 기술 문서: 500-800 토큰
    [InlineData("legal", 300, 500)]      // 법률 문서: 300-500 토큰
    [InlineData("narrative", 800, 1200)] // 서사 문서: 800-1200 토큰
    public async Task TestOptimalChunkSize_ByDocumentType(
        string docType, int minSize, int maxSize)
    {
        // 문서 타입별 최적 청크 크기 검증
    }
}
```

#### 2. 청크 품질 평가 테스트
```csharp
public class ChunkQualityTests
{
    [Fact]
    public void TestChunkCompleteness_ShouldBeStandalone()
    {
        // 각 청크가 독립적으로 읽을 수 있는지 검증
        var chunk = CreateTestChunk();
        
        // 완전성 체크리스트
        Assert.True(HasSubjectContext(chunk));     // 주어 컨텍스트
        Assert.True(HasCompleteInformation(chunk)); // 완전한 정보
        Assert.True(IsReadableAlone(chunk));       // 독립 가독성
    }
    
    [Fact]
    public void TestOverlapEffectiveness()
    {
        // 오버랩이 컨텍스트 연결에 효과적인지 검증
        var overlap = 128;
        var chunks = CreateChunksWithOverlap(overlap);
        
        Assert.True(CanReconstructContext(chunks));
        Assert.True(NoInformationLoss(chunks));
    }
}
```

#### 3. 경계 감지 정확도 테스트
```csharp
public class BoundaryDetectionAccuracyTests
{
    [Theory]
    [InlineData("# Heading\nContent", BoundaryType.Section)]
    [InlineData("```python\ncode\n```", BoundaryType.CodeBlock)]
    [InlineData("| Col1 | Col2 |", BoundaryType.Table)]
    [InlineData("1. Item\n2. Item", BoundaryType.List)]
    public void TestStructuralBoundaryDetection(
        string content, BoundaryType expectedType)
    {
        // 구조적 경계 감지 정확도 검증
        var detector = new SemanticBoundaryDetector();
        var boundary = detector.DetectBoundary(content);
        
        Assert.Equal(expectedType, boundary.Type);
        Assert.True(boundary.Confidence > 0.8);
    }
}
```

### Phase 11-B: RAG 최적화 구현

#### 1. 적응형 청킹 개선
```csharp
public interface IRAGOptimizedChunker
{
    /// <summary>
    /// RAG 검색 품질에 최적화된 청킹
    /// </summary>
    Task<IEnumerable<DocumentChunk>> ChunkForRAGAsync(
        ParsedDocumentContent content,
        RAGOptimizationOptions options);
}

public class RAGOptimizationOptions
{
    /// <summary>
    /// 목표 검색 재현율
    /// </summary>
    public double TargetRecall { get; set; } = 0.85;
    
    /// <summary>
    /// 청크 독립성 수준 (0-1)
    /// </summary>
    public double ChunkIndependence { get; set; } = 0.9;
    
    /// <summary>
    /// 컨텍스트 윈도우 크기
    /// </summary>
    public int ContextWindow { get; set; } = 200;
    
    /// <summary>
    /// 메타데이터 풍부도
    /// </summary>
    public MetadataRichness MetadataLevel { get; set; } = MetadataRichness.Full;
}
```

#### 2. 메타데이터 품질 강화
```csharp
public class EnhancedChunkMetadata : DocumentMetadata
{
    /// <summary>
    /// 구조적 역할 (제목, 본문, 캡션, 참조)
    /// </summary>
    public StructuralRole Role { get; set; }
    
    /// <summary>
    /// 계층 구조 경로 (e.g., "Chapter 1 > Section 2 > Subsection 3")
    /// </summary>
    public string HierarchicalPath { get; set; }
    
    /// <summary>
    /// 청크 중요도 점수 (0-1)
    /// </summary>
    public double ImportanceScore { get; set; }
    
    /// <summary>
    /// 이전/다음 청크 컨텍스트 요약
    /// </summary>
    public string PreviousContext { get; set; }
    public string NextContext { get; set; }
    
    /// <summary>
    /// 검색 힌트 키워드
    /// </summary>
    public List<string> SearchHints { get; set; }
}
```

### Phase 11-C: 성능 검증

#### 1. 대용량 문서 처리 테스트
```csharp
[Fact]
public async Task TestLargeDocumentProcessing_Under10Seconds()
{
    // 100MB 문서를 10초 내 처리
    var largeDoc = GenerateLargeDocument(100_000_000);
    var stopwatch = Stopwatch.StartNew();
    
    await foreach (var chunk in processor.ProcessStreamAsync(largeDoc))
    {
        // 스트리밍 처리
    }
    
    stopwatch.Stop();
    Assert.True(stopwatch.Elapsed.TotalSeconds < 10);
}
```

#### 2. 메모리 효율성 검증
```csharp
[Fact]
public void TestMemoryEfficiency_Under2xFileSize()
{
    var fileSize = 50_000_000; // 50MB
    var memoryBefore = GC.GetTotalMemory(true);
    
    ProcessLargeFile(fileSize);
    
    var memoryUsed = GC.GetTotalMemory(false) - memoryBefore;
    Assert.True(memoryUsed < fileSize * 2);
}
```

## 📈 구현 우선순위

### Week 1: 테스트 인프라 구축
1. ✅ RAGQualityBenchmark 클래스 생성
2. ✅ 테스트 데이터셋 준비 (QA pairs, documents)
3. ✅ 메트릭 측정 유틸리티 구현

### Week 2: 품질 개선 구현
1. ✅ 경계 감지 정확도 향상
2. ✅ 메타데이터 풍부화
3. ✅ 청크 완전성 검증 로직

### Week 3: 성능 최적화
1. ✅ 스트리밍 처리 검증
2. ✅ 병렬 처리 안정성
3. ✅ 캐싱 효과성 측정

### Week 4: 릴리즈 준비
1. ✅ 모든 테스트 통과 (220/220)
2. ✅ 문서화 완성
3. ✅ NuGet 패키지 배포

## 🚫 범위 제외 (소비 앱 책임)

- ❌ 임베딩 생성 (OpenAI, Cohere, etc.)
- ❌ 벡터 저장소 통합 (Pinecone, Qdrant, etc.)
- ❌ 유사도 검색 구현
- ❌ 지식 그래프 구축
- ❌ RAG 응답 생성
- ❌ 검색 결과 리랭킹

## ✅ FileFlux 핵심 가치

1. **청킹 품질**: RAG 검색에 최적화된 의미적 청킹
2. **메타데이터**: 검색 품질 향상을 위한 풍부한 메타데이터
3. **성능**: 대용량 문서 고속 처리
4. **신뢰성**: 100% 테스트 커버리지
5. **간편함**: 단순한 API, 복잡한 내부 처리

## 📝 성공 기준

- [ ] 검색 재현율 85% 이상 달성
- [ ] 청크 완전성 90% 이상 달성
- [ ] 100MB 문서 10초 내 처리
- [ ] 메모리 사용량 파일 크기의 2배 이하
- [ ] 220개 테스트 100% 통과
- [ ] NuGet 다운로드 1000+ 달성