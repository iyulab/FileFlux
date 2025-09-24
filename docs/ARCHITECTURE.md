# FileFlux 아키텍처 가이드

> RAG 시스템을 위한 문서 처리 SDK의 아키텍처 개요

## 🎯 설계 원칙

### 1. 클린 아키텍처
- **도메인 계층**: 핵심 모델과 인터페이스 정의
- **코어 계층**: 비즈니스 로직과 처리 오케스트레이션
- **인프라 계층**: 구체적인 구현체 (리더, 전략)

### 2. 인터페이스 중심 설계
- 확장 가능한 플러그인 아키텍처
- 의존성 주입을 통한 느슨한 결합
- 전략 패턴과 팩토리 패턴 적용

### 3. 유연한 AI 서비스 통합 (Phase 7)
- **선택적 AI 서비스**: ITextCompletionService, IEmbeddingService, IImageToTextService 선택적 구현
- **우아한 폴백**: AI 서비스 미제공 시 비AI 대안 사용
- **Context7 스타일 메타데이터**: 도메인 분류, 품질 평가, 구조 분석
- **생산성 최적화**: AI 없이도 완전 동작, AI 있으면 최고 품질

---

## 🏗️ 시스템 아키텍처

```mermaid
graph TB
    A[Client Application] --> B[IDocumentProcessor]
    B --> C[DocumentProcessor]
    B --> P[ParallelDocumentProcessor]
    B --> S[StreamingDocumentProcessor]
    
    C --> D[IDocumentReaderFactory]
    C --> E[IChunkingStrategyFactory]
    P --> D
    P --> E
    S --> C
    S --> CACHE[DocumentCacheService]
    
    D --> F[PdfReader]
    D --> G[WordReader]  
    D --> H[ExcelReader]
    D --> I[PowerPointReader]
    D --> J[MarkdownReader]
    D --> K[TextReader]
    D --> L[JsonReader]
    D --> N[CsvReader]
    
    E --> O[FixedSizeStrategy]
    E --> Q[SemanticStrategy]
    E --> R[ParagraphStrategy]
    E --> T[IntelligentStrategy]
    
    P --> U[Parallel Processing Engine]
    S --> V[Streaming Pipeline]
    U --> W[CPU Core Scaling]
    V --> X[AsyncEnumerable Output]
    CACHE --> Y[LRU Cache System]
    
    C --> M[DocumentChunk[]]
    
    style A fill:#e1f5fe
    style B fill:#f3e5f5
    style P fill:#fff3e0
    style S fill:#fff3e0
    style CACHE fill:#f1f8e9
    style M fill:#e8f5e8
```

### 계층 구조

```
┌─────────────────────────────────┐
│         Client Layer            │ 
│ • Application Code              │
│ • RAG Systems Integration       │
│ • Service Configuration         │
├─────────────────────────────────┤
│       Abstraction Layer         │
│ • IDocumentProcessor            │
│ • IDocumentReader               │ 
│ • IChunkingStrategy             │
├─────────────────────────────────┤
│          Core Layer             │
│ • DocumentProcessor             │
│ • DocumentReaderFactory         │
│ • ChunkingStrategyFactory       │
├─────────────────────────────────┤
│     Implementation Layer        │
│ • Document Readers              │
│ • Chunking Strategies           │
│ • Text Processing Utilities     │
├─────────────────────────────────┤
│         Model Layer             │
│ • DocumentChunk                 │
│ • DocumentMetadata              │ 
│ • ChunkingOptions               │
└─────────────────────────────────┘
```

---

## 🔧 핵심 컴포넌트

### 1. IDocumentProcessor (주 인터페이스)
**역할**: 모든 문서 처리의 단일 진입점
**주요 메소드**: ProcessAsync(파일경로/스트림)
**책임**: 처리 파이프라인 조정, 오류 처리, 결과 검증

### 2. DocumentProcessor (오케스트레이터)
**처리 파이프라인**:
1. 입력 검증 → 2. 문서 타입 감지 → 3. 리더 선택 → 4. 콘텐츠 추출 → 5. 전략 선택 → 6. 청킹 적용 → 7. 후처리

### 3. IDocumentReader (콘텐츠 추출)
**현재 구현체**:
- **TextDocumentReader**: .txt, .md 파일 처리
- **JsonDocumentReader**: .json 파일 구조화 처리  
- **CsvDocumentReader**: .csv 파일 테이블 데이터 처리

### 4. IChunkingStrategy (콘텐츠 분할)
**전략 종류**:
- **FixedSizeStrategy**: 고정 크기 토큰 기반 분할
- **SemanticStrategy**: 문장 경계 기반 분할
- **ParagraphStrategy**: 단락 단위 분할
- **IntelligentStrategy**: AI 기반 의미 단위 분할

### 5. ParallelDocumentProcessor (Phase 8 - 병렬 처리 엔진)
**핵심 기능**:
- **CPU 코어별 동적 스케일링**: 시스템 리소스에 맞춘 작업 분산
- **메모리 백프레셔 제어**: Threading.Channels 기반 백프레셔 시스템
- **지능형 작업 분산**: 파일 크기와 복잡도에 따른 최적 분배
- **Task.WhenAll 최적화**: 병렬 처리 결과 통합

### 6. StreamingDocumentProcessor (Phase 8 - 스트리밍 최적화)
**핵심 기능**:
- **실시간 청크 반환**: AsyncEnumerable 기반 즉시 결과 제공
- **캐시 우선 검사**: 파일 해시 기반 중복 처리 방지
- **LRU 캐시 시스템**: 자동 만료 및 메모리 최적화
- **백프레셔 제어**: 채널 기반 메모리 압력 조절

### 7. DocumentCacheService (Phase 8 - 지능형 캐시)
**캐시 전략**:
- **파일 해시 기반 키**: 파일 내용 + 옵션 조합으로 고유 키 생성
- **LRU 교체 정책**: 메모리 제한 시 최근 미사용 항목 자동 제거
- **자동 만료**: 설정 가능한 TTL로 캐시 무효화
- **통계 수집**: 히트율, 메모리 사용률 등 성능 메트릭 제공

---

## 🔄 처리 파이프라인

### Phase 8 향상된 처리 흐름

```mermaid
graph TB
    A[Document Input] --> B{Processing Mode}
    
    B -->|Parallel| C[ParallelDocumentProcessor]
    B -->|Streaming| D[StreamingDocumentProcessor]
    B -->|Standard| E[DocumentProcessor]
    
    C --> F[CPU Core Scaling]
    C --> G[Task Distribution]
    
    D --> H[Cache Check]
    H -->|Hit| I[Cached Results]
    H -->|Miss| J[Live Processing]
    
    E --> K[Type Detection]
    J --> K
    G --> K
    
    K --> L[Reader Selection]
    L --> M[Content Extraction]
    M --> N[Strategy Selection]
    N --> O[Chunking Process]
    O --> P[Post Processing]
    
    P --> Q[DocumentChunk[]]
    I --> Q
    
    D --> R[LRU Cache Update]
    P --> R
    
    style C fill:#fff3e0
    style D fill:#fff3e0
    style H fill:#f1f8e9
    style R fill:#f1f8e9
```

### 1. 입력 처리
- 파일 경로 또는 스트림 입력 지원
- 파일 존재성 및 접근 권한 검증
- 지원 형식 확인

### 2. 콘텐츠 추출
- 문서 타입별 전용 리더 사용
- 텍스트 콘텐츠와 메타데이터 추출
- 문서 구조 정보 보존

### 3. 청킹 처리
- 선택된 전략에 따른 콘텐츠 분할
- 청크 간 겹침(overlap) 적용
- 메타데이터 전파 및 인덱스 부여

---

## 🏭 팩토리 패턴

### DocumentReaderFactory
- 파일 확장자 기반 리더 선택
- 새로운 리더 등록 및 관리
- 지원되지 않는 형식 예외 처리

### ChunkingStrategyFactory  
- 전략 이름 기반 선택 시스템
- 기본 전략 및 대체 전략 관리
- 동적 전략 등록 지원

---

## 🎛️ 설정 및 옵션

### ChunkingOptions
**주요 설정**:
- **Strategy**: 청킹 전략 이름 ("Intelligent", "Semantic" 등)
- **MaxChunkSize**: 최대 청크 크기 (기본: 1024 토큰)
- **OverlapSize**: 청크 간 겹침 크기 (기본: 128 토큰)  
- **PreserveStructure**: 문서 구조 보존 여부
- **StrategyOptions**: 전략별 세부 옵션

### 의존성 주입 설정
**기본 등록**: `services.AddFileFlux()`
**커스텀 설정**: 옵션 콜백으로 기본값 구성
**확장 등록**: 커스텀 리더/전략 추가 등록

---

## 🚀 성능 고려사항

### 메모리 관리
- 스트림 기반 처리로 대용량 파일 지원
- IDisposable 패턴으로 리소스 정리
- ConfigureAwait(false)로 컨텍스트 스위칭 최소화

### 동시성
- 모든 공개 인터페이스는 스레드 안전
- 팩토리는 불변 컬렉션 사용
- 공유 가변 상태 없음

### 확장성
- 최소한의 메모리 할당
- 효율적인 문자열 처리
- 재사용 가능한 컴포넌트 설계

---

## 🔌 확장 지점

### 커스텀 리더 추가
1. IDocumentReader 인터페이스 구현
2. DI 컨테이너에 등록
3. SupportedExtensions와 CanRead 메소드 구현

### 커스텀 청킹 전략 추가
1. IChunkingStrategy 인터페이스 구현  
2. StrategyName과 DefaultOptions 정의
3. ChunkAsync 메소드에 분할 로직 구현

### 플러그인 아키텍처
- IFileFluxPlugin 인터페이스로 플러그인 정의
- 런타임 어셈블리 로딩 지원
- 플러그인별 서비스 등록 관리

---

## 🔍 오류 처리

### 예외 계층구조
- **FileFluxException**: 모든 예외의 기반 클래스
- **UnsupportedFileFormatException**: 지원되지 않는 파일 형식
- **DocumentProcessingException**: 문서 처리 중 오류
- **ChunkingException**: 청킹 과정 중 오류

### 오류 처리 패턴
- 입력 검증을 통한 조기 오류 감지
- 의미있는 오류 메시지와 컨텍스트 제공
- 내부 예외 보존으로 원인 추적 가능
- 파일명과 전략명 등 디버깅 정보 포함

---

## 📊 RAG 시스템 통합

### 처리 결과
**DocumentChunk**: 청크 ID, 콘텐츠, 메타데이터, 위치 정보, 인덱스 포함
**DocumentMetadata**: 파일명, 파일 타입, 처리 시간, 페이지 번호 등

### 통합 패턴
1. **스트리밍 처리**: ProcessAsync로 청크별 순차 처리
2. **배치 처리**: 전체 청크 수집 후 일괄 처리  
3. **파이프라인 처리**: 청크 생성과 임베딩 생성 동시 진행

FileFlux는 문서를 구조화된 청크로 변환하는 역할에 집중하며, 임베딩 생성과 벡터 저장은 사용자 선택에 맡깁니다.