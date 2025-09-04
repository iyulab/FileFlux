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

### 3. 성능 우선
- 스트리밍 처리로 메모리 효율성
- 비동기 처리와 취소 토큰 지원
- 대용량 파일 처리 최적화

---

## 🏗️ 시스템 아키텍처

```mermaid
graph TB
    A[Client Application] --> B[IDocumentProcessor]
    B --> C[DocumentProcessor]
    C --> D[IDocumentReaderFactory]
    C --> E[IChunkingStrategyFactory]
    
    D --> F[TextReader]
    D --> G[JsonReader]  
    D --> H[CsvReader]
    
    E --> I[FixedSizeStrategy]
    E --> J[SemanticStrategy]
    E --> K[ParagraphStrategy]
    E --> L[IntelligentStrategy]
    
    C --> M[DocumentChunk[]]
    
    style A fill:#e1f5fe
    style B fill:#f3e5f5
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

---

## 🔄 처리 파이프라인

### 기본 처리 흐름

```mermaid
graph LR
    A[Document Input] --> B[Type Detection]
    B --> C[Reader Selection]
    C --> D[Content Extraction]
    D --> E[Strategy Selection]
    E --> F[Chunking Process]
    F --> G[Post Processing]
    G --> H[DocumentChunk[]]
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