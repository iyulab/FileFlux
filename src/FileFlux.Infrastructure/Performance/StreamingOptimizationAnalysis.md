# FileFlux 스트리밍 최적화 분석 및 개선 방안

## 현재 아키텍처 분석

### 메모리 사용 패턴 분석
FileFlux의 현재 처리 파이프라인에서 메모리 집약적인 지점들:

1. **문서 읽기 단계** (`IDocumentReader`)
   - 전체 파일 내용을 메모리에 로드
   - 큰 파일의 경우 파일 크기만큼 메모리 사용

2. **파싱 단계** (`IDocumentParser`) 
   - 구조화된 콘텐츠 생성 시 추가 메모리 할당
   - LLM 호출 시 프롬프트 생성으로 메모리 중복

3. **청킹 단계** (`IChunkingStrategy`)
   - 전체 텍스트를 청크 배열로 분할 시 메모리 피크
   - 중복 및 메타데이터 생성으로 메모리 증폭

## 식별된 최적화 지점

### 🔴 Critical 최적화 지점

#### 1. 문서 리더 스트리밍 개선
**문제점**: 
- `TextDocumentReader.ReadAsync()` 메서드가 `File.ReadAllTextAsync()` 사용
- 50MB 파일 = 50MB+ 메모리 사용

**해결 방안**:
```csharp
// 현재 (비효율적)
var content = await File.ReadAllTextAsync(filePath, cancellationToken);

// 개선안 (스트리밍)
using var reader = new StreamReader(filePath);
var chunks = new List<string>();
var buffer = new char[8192]; // 8KB 버퍼

while (!reader.EndOfStream)
{
    var bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
    // 청크 단위로 처리...
}
```

#### 2. 청킹 전략 메모리 효율성 개선
**문제점**: 
- `IntelligentChunkingStrategy`가 전체 텍스트를 메모리에 보유
- 청크 생성 시 원본 + 청크 배열 = 2-3배 메모리 사용

**해결 방안**:
- Lazy evaluation을 통한 점진적 청킹
- `yield return` 방식으로 청크를 하나씩 생성
- 원본 텍스트의 부분적 해제

#### 3. 메타데이터 생성 최적화
**문제점**: 
- 각 청크마다 풍부한 메타데이터 생성으로 메모리 오버헤드
- `DocumentChunk` 객체당 평균 1-2KB 메모리 사용

**해결 방안**:
- 필수 메타데이터만 즉시 생성
- 선택적 메타데이터는 지연 로딩
- 메타데이터 풀링을 통한 재사용

### 🟡 High 우선순위 최적화 지점

#### 4. 가비지 컬렉션 압박 완화
**문제점**: 
- 대량의 임시 문자열 생성으로 Gen0/Gen1 GC 빈발
- 큰 객체 (85KB+)의 LOH(Large Object Heap) 할당

**해결 방안**:
```csharp
// StringPool 사용으로 문자열 재사용
private static readonly ObjectPool<StringBuilder> StringBuilderPool;

// ArrayPool 사용으로 배열 재사용  
private static readonly ArrayPool<char> CharArrayPool = ArrayPool<char>.Shared;
```

#### 5. 병렬 처리 메모리 관리
**문제점**: 
- 병렬 청킹 시 메모리 사용량의 스레드 수배 증가
- 동시 처리로 인한 메모리 경합

**해결 방안**:
- 동시성 수준 제한 (`SemaphoreSlim`)
- 스레드당 메모리 할당량 제한
- 백프레셔(Backpressure) 메커니즘 도입

### 🟢 Medium 우선순위 최적화 지점

#### 6. 캐싱 전략 개선
**문제점**: 
- LLM 응답 캐싱이 메모리 기반으로만 구현
- 캐시 만료 정책 부재로 메모리 누수 가능

**해결 방안**:
- LRU 캐시 with 메모리 한계 설정
- 디스크 기반 2차 캐시
- 캐시 압축을 통한 공간 효율성

## 구현 우선순위 및 예상 효과

### Phase 6.1: 즉시 적용 가능한 개선사항 (1-2주)
1. **TextDocumentReader 스트리밍 구현**
   - 예상 효과: 메모리 사용량 80% 감소
   - 구현 난이도: Low

2. **ArrayPool/StringPool 도입**  
   - 예상 효과: GC 압박 50% 감소
   - 구현 난이도: Medium

### Phase 6.2: 아키텍처 개선 (2-3주)  
3. **청킹 전략 yield 방식 전환**
   - 예상 효과: 피크 메모리 60% 감소
   - 구현 난이도: High

4. **메타데이터 지연 로딩**
   - 예상 효과: 청크당 메모리 40% 감소  
   - 구현 난이도: Medium

### Phase 6.3: 고급 최적화 (3-4주)
5. **병렬 처리 메모리 관리**
   - 예상 효과: 안정성 향상, 메모리 예측성 확보
   - 구현 난이도: High

## 성능 목표

### 현재 베이스라인 (추정)
- 50MB 파일: ~150MB 메모리 사용 (3배)
- 처리 속도: ~0.5 MB/초
- GC 압박: 높음

### 최적화 후 목표
- 50MB 파일: ~75MB 메모리 사용 (1.5배) 
- 처리 속도: ~2 MB/초
- GC 압박: 낮음

### 성공 지표
- ✅ 메모리/파일 비율 < 2.0
- ✅ 처리 속도 > 1 MB/초  
- ✅ 성능 등급 B (Good) 이상
- ✅ Gen2 GC < 5회/50MB 파일

## 다음 단계 실행 계획

1. **즉시 시작**: TextDocumentReader 스트리밍 구현
2. **병렬 진행**: ArrayPool/StringPool 적용
3. **검증**: 개선된 성능 벤치마킹
4. **확장**: 다른 DocumentReader에 패턴 적용

이 분석을 바탕으로 구체적인 구현을 시작하겠습니다.