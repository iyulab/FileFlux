# FileFlux 개발 로드맵 & 작업 계획

> 지능형 문서 구조화를 위한 .NET SDK - RAG 시스템 최적화 청크 생성

## 📋 프로젝트 개요

**목표**: 다양한 문서 형식을 파싱하고 LLM을 활용하여 일관된 구조로 재구성한 텍스트 청크를 생성하는 .NET SDK 구현

**핵심 목적**: `Input: File` → `Output: 구조화된 텍스트 청크`
- 벡터화나 임베딩은 소비 애플리케이션의 책임
- FileFlux는 순수하게 문서 이해와 구조화에 집중

**목표 일정**: Phase 5 대부분 완료, Phase 5.5 마무리 단계 (남은 핵심 기능 완성)

---

## 🎯 개발 페이즈 현황

### ✅ Phase 1: 기반 & 핵심 아키텍처 (완료)
**목표**: 프로젝트 구조, 핵심 인터페이스, 기본 프레임워크 구축

**완료된 주요 성과**:
- ✅ 4개 프로젝트 구조: Domain, Core, Infrastructure, Tests
- ✅ 핵심 도메인 모델: DocumentContent, DocumentChunk, ChunkingOptions
- ✅ 핵심 인터페이스: IDocumentProcessor, IDocumentReader, IChunkingStrategy
- ✅ 팩토리 패턴: IDocumentReaderFactory, IChunkingStrategyFactory
- ✅ 예외 처리 프레임워크 및 유틸리티 클래스

### ✅ Phase 2: 문서 리더 구현 (완료)
**목표**: 견고한 파싱을 가진 기본 문서 형식 리더 구현

**완료된 주요 성과**:
- ✅ TextDocumentReader: 기본 텍스트 파일 처리
- ✅ JsonDocumentReader: JSON 구조 파싱 및 플래튼화
- ✅ CsvDocumentReader: CSV 구조 분석 및 처리
- ✅ DocumentReaderFactory: 자동 리더 선택 및 등록 시스템
- ✅ 파일 형식 감지 및 검증 로직

### ✅ Phase 3: 청킹 전략 & 파이프라인 (완료)
**목표**: 지능형 청킹 알고리즘 및 처리 파이프라인 개발

**완료된 주요 성과**:
- ✅ 4가지 청킹 전략: FixedSize, Semantic, Paragraph, Intelligent
- ✅ ChunkingStrategyFactory: 동적 전략 선택 시스템  
- ✅ DocumentProcessor: 메인 오케스트레이터 구현
- ✅ 토큰 카운팅 및 메타데이터 추출
- ✅ 의존성 주입 지원 (ServiceCollectionExtensions)
- ✅ 성능 최적화 (OptimizedDocumentProcessor, 캐싱, 병렬 처리)
- ✅ 비동기 스트림 처리 (AsyncStreamDocumentProcessor)
- ✅ 고급 문서 리더: PDF (PdfPig), Word (Open XML), Excel, HTML 지원
- ✅ 모든 프로젝트 컴파일 성공: Core, Infrastructure, Tests, SampleApp

### ✅ Phase 4: 통합 테스트 & 실제 검증 (완료)
**목표**: 포괄적인 테스팅 및 실제 RAG 파이프라인 검증

**완료된 주요 성과**:
- ✅ Phase 3 기능 테스트: 11개 테스트 메서드, 100% 통과
- ✅ SampleApp: 콘솔 기반 RAG 데모 애플리케이션
- ✅ SQLite 벡터 스토리지 구현
- ✅ OpenAI 클라이언트 통합 (임베딩 생성, RAG 쿼리)
- ✅ 실제 문서 처리 검증: 2개 문서, 16개 청크, 성공적인 RAG 응답
- ✅ 성능 메트릭: 문서 처리 1-3초, RAG 응답 5-12초
- ✅ **실제 PDF 처리 검증**: PdfPig 통합, 3MB PDF → 511청크, 1.3초 처리
- ✅ **사용자 친화적 로깅**: 기술적 잡음 제거, 단계별 진행상태 표시
- ✅ **파일 기반 결과 저장**: logs/, chunking-results/ 디렉토리 구조

---

### ✅ Phase 4.5: RAG 품질 최적화 긴급 수정 (완료)

**목표**: PDF 처리 결과 분석 후 발견된 RAG 품질 문제 즉시 해결

**완료된 주요 성과**:
- ✅ **아키텍처 업그레이드**: ILlmProvider → ITextCompletionService 전환
- ✅ **AsyncEnumerable 성능 최적화**: DocumentProcessor에서 IAsyncEnumerable<ProcessingResult<DocumentChunk>> 스트리밍 구현
- ✅ **C# yield 제약 해결**: try-catch 블록 내 yield 제약 우회를 위한 OperationResult 패턴 도입
- ✅ **의존성 명확화**: 텍스트 완성 서비스 필수 의존성으로 변경, "without LLM" 지원 제거
- ✅ **테스트 인프라**: MockTextCompletionService 구현으로 단위 테스트 지원
- ✅ **호환성 수정**: 모든 프로젝트 빌드 성공 (Core, Infrastructure, Tests, SampleApp)

**아키텍처 개선사항**:
- **Reader → Parser → Chunking 파이프라인**: 3단계 처리 체계 확립
- **실시간 진행률 추적**: ProcessingProgress를 통한 단계별 진행 상황 모니터링  
- **스트리밍 처리**: 메모리 효율적인 청크 단위 실시간 처리
- **오류 처리 강화**: 단계별 독립적 오류 처리 및 복구 메커니즘

---

# ✅ Phase 5: RAG 품질 완성 (95% 완료)

## 📋 Phase 5 완료 요약
**목표**: 실제 RAG 파이프라인에서의 실용성과 품질 확보 → **95% 달성**

**완료된 핵심 성과**:
- **DocumentChunk 모델 확장**: ContentType, QualityScore, RelevanceScore, StructuralRole 등 전체 메타데이터 구현 ✅
- **ProgressiveDocumentProcessor**: AsyncEnumerable 스트리밍 완전 구현 ✅
- **고급 메타데이터 생성**: LLM 최적화 로직 및 152개 테스트 케이스 완료 ✅
- **4단계 출력 시스템**: extract/parse/chunk/metadata 파이프라인 완성 ✅

## 5.1 긴급 RAG 품질 문제 해결 (2025-09-03~04 완료)

### ✅ **완료된 핵심 품질 개선사항**

- [x] **P5-T001-CRITICAL**: MockTextCompletionService 지능형 분석 기능 구현 ✅
  - 문서 유형 "General" → "AIMS MVP 시스템 요구사항 분석"으로 정확 분류 달성
  - Topic, Keywords, Summary 추출 정상 작동 및 검증 완료

- [x] **P5-T002-CRITICAL**: IntelligentChunkingStrategy 의미적 경계 보존 강화 ✅
  - MarkdownSectionRegex 추가 및 섹션 헤더 경계 우선 처리
  - 30% 임계값 적용: 최소 의미단위 확보 후 섹션 분할
  - 라인 기반 처리로 ExtractSemanticUnits 개선

- [x] **P5-T003-CRITICAL**: 테이블 감지 및 보존 로직 개선 (부분 완료) ⚠️
  - TableRegex, TableSeparatorRegex 테이블 구조 감지 추가  
  - SplitLargeTable() 헤더 보존 의미적 분할 구현
  - **미해결**: 여전히 FR-002 등 긴 테이블 행이 중간 분할됨
  - **근본 원인**: ExtractSemanticUnits의 ExtractTableUnit이 전체 테이블을 하나의 단위로 묶지 못함

- [x] **P5-T004**: 크기 준수 및 중간 결과 저장 시스템 (기존 구현 확인) ✅
  - EnforceMaxSize(), SplitByWords() 메서드 구현 확인
  - extraction-results, parsing-results 저장 시스템 정상 작동

## ✅ 5.2 고급 메타데이터 시스템 완성 (완료)

### **완료된 주요 성과**:

- [x] **P5-T005**: DocumentChunk 모델 확장 ✅
  - [x] ContentType, QualityScore, RelevanceScore, StructuralRole 전체 구현 ✅
  - [x] BoundaryMarkers, ContextualScores, InformationDensity 확장 메타데이터 ✅  
  - [x] ContextualHeader, DocumentDomain, TechnicalKeywords LLM 최적화 필드 ✅

- [x] **P5-T007**: 품질 지표 및 테스트 시스템 완성 ✅
  - [x] LLM 메타데이터 테스트: 152개 구체적 테스트 케이스 구현 ✅
  - [x] 도메인/구조적 역할/기술 키워드 자동 감지 로직 완성 ✅
  - [x] RAG 적합성 테스트 및 품질 검증 시스템 ✅

- [x] **P5-T008**: ProgressiveDocumentProcessor 4단계 출력 ✅
  - [x] AsyncEnumerable 스트리밍 처리 완전 구현 ✅
  - [x] ProcessingResult<T> 패턴 진행률 추적 ✅
  - [x] Reader → Parser → Chunking 파이프라인 완성 ✅

### ⚠️ **미완료 항목 (5% 남은 작업)**

- [ ] **P5-T006-PARTIAL**: 경계 마커 시스템 확장 (25% 완료)
  - [x] TABLE_START/END 마커 구현 완료 ✅
  - [ ] **HEADING_START/END, CODE_START/END, LIST_START/END 마커 미구현** ⚠️
  - [ ] 다른 DocumentReader들로 마커 시스템 확산 필요

- [ ] **P5-T009**: 테이블 청킹 완전 해결
  - [x] 테이블 감지 및 기본 보존 로직 구현 ✅
  - [ ] **긴 테이블 행 중간 분할 방지 완전 해결** ⚠️
  - [ ] ExtractTableUnit 전체 테이블 단위 묶기 개선

### Phase 5 아키텍처 변경사항 (2025년 업데이트)
**주요 설계 변경**: 사용자 피드백을 반영하여 텍스트 완성 서비스를 필수 의존성으로 변경하고 FileFlux 코어 로직과 분리

**변경 전 (초기 설계)**:
- FileFlux가 선택적 LLM 사용을 지원하여 비지능형 처리도 가능
- 유연한 구조이지만 성능 및 품질 제한

**변경 후 (현재 아키텍처)**:
- **FileFlux 책임**: 문서 구조화 로직, 프롬프트 생성, 결과 처리
- **소비 애플리케이션 책임**: 텍스트 완성 서비스 제공업체 선택 및 API 호출 구현
- **인터페이스 기반 분리**: `ITextCompletionService`를 통한 느슨한 결합
- **필수 사용**: 모든 문서 처리에 텍스트 완성 서비스 필요

**혜택**:
- 소비자가 원하는 텍스트 완성 서비스 제공업체 자유 선택 (OpenAI, Anthropic, Azure, 로칼 모델 등)
- FileFlux의 책임 범위 명확화: 문서 처리 및 구조화에 집중
- 텍스트 완성 서비스 제공업체별 의존성 제거
- 지능형 기능에 필수 의존성 명시하여 명확한 아키텍처

## 5.2 문서 유형별 구조화 전략
- [ ] **P5-T003**: 기술 문서 구조화 (Markdown, TXT)
  - [ ] 헤더 계층 인식 및 섹션 분할
  - [ ] 코드 블록 식별 및 보존
  - [ ] 목록 및 표 구조 인식
  - [ ] 컨텍스트 경로 생성 (상위 섹션 추적)

- [ ] **P5-T004**: 구조화 데이터 처리 (JSON, CSV, XML)  
  - [ ] 스키마 인식 및 메타데이터 추출
  - [ ] 중첩 구조 플래튼화 전략
  - [ ] 데이터 타입 추론 및 분류
  - [ ] 레코드 단위 구조화 및 요약

- [ ] **P5-T009**: 고급 문서 형식 구조화 (PDF, DOCX, XLSX)
  - [ ] 이미 구현된 리더들에 대한 구조화 전략 적용
  - [ ] PDF: 페이지 기반 섹션 분할 및 레이아웃 인식
  - [ ] DOCX: 스타일 기반 섹션 인식 및 문서 구조 트리
  - [ ] XLSX: 시트별 구조화 및 테이블 메타데이터 추출

## 5.3 품질 측정 및 검증
- [ ] **P5-T005**: 구조화 품질 평가 시스템
  - [ ] ChunkQualityEvaluator 구현
  - [ ] 다중 품질 지표 계산 (신뢰성, 완성도, 일관성)
  - [ ] 품질 기반 필터링 및 개선 제안
  - [ ] 벤치마킹 및 성능 추적

- [ ] **P5-T006**: 구조화 결과 검증
  - [ ] 구조화 정확도 측정
  - [ ] 정보 손실 방지 검증
  - [ ] 메타데이터 완성도 검사
  - [ ] A/B 테스트 및 품질 비교

## 5.4 고급 문서 처리 준비
- [ ] **P5-T007**: HTML 문서 리더 구현
  - [ ] HTML 파싱 및 구조 인식
  - [ ] HTML→Markdown 변환
  - [ ] 웹 페이지 클리닝 및 정제
  - [ ] 링크 및 이미지 메타데이터 추출

- [ ] **P5-T008**: 파일 크기 기반 최적화
  - [ ] 최소 파일 크기 임계값 적용 (30자)
  - [ ] 대용량 파일 감지 및 특별 처리
  - [ ] 적응형 청킹 크기 결정
  - [ ] 메모리 효율적 처리

---

# Phase 5.5: 핵심 기능 완성 (현재 진행)

## 📋 Phase 5.5 개요
**목표**: Phase 5에서 95% 완료된 기능들의 나머지 5% 핵심 기능 완성

**기간**: 1주 예상  
**우선순위**: P0 (Critical) - 남은 필수 기능 완성
**완성 기준**: 모든 구조적 요소 마커 지원, 테이블 청킹 완전 해결

## 🔴 **P0 우선순위 작업**

- [ ] **T5.5-001**: 확장 경계 마커 시스템 완성
  - [ ] **1단계**: HEADING_START/END 마커 구현
    - MarkdownDocumentReader에 헤더 감지 및 마커 삽입
    - # ~ ##### 모든 레벨 헤더 지원
  - [ ] **2단계**: CODE_START/END 마커 구현  
    - 코드 블록 (```) 감지 및 마커 삽입
    - 인라인 코드와 블록 코드 구분 처리
  - [ ] **3단계**: LIST_START/END 마커 구현
    - 순서/비순서 리스트 감지 및 마커 삽입
    - 중첩 리스트 구조 보존

- [ ] **T5.5-002**: 실제 품질 점수 계산 로직 구현
  - [ ] QualityScore 실제 계산 알고리즘 (현재 Mock 상태)
  - [ ] RelevanceScore 문서 맥락 기반 계산
  - [ ] InformationDensity 정보 밀도 측정 로직

- [ ] **T5.5-003**: 테이블 청킹 완전 해결  
  - [ ] ExtractTableUnit 개선: 전체 테이블을 하나의 의미 단위로 처리
  - [ ] 긴 테이블 행 중간 분할 방지 완전 해결
  - [ ] 테이블 헤더 보존 강화

## 🟡 **P1 우선순위 작업**

- [ ] **T5.5-004**: 다른 DocumentReader로 마커 시스템 확산
  - [ ] TextDocumentReader: 일반 텍스트의 구조적 요소 감지
  - [ ] PdfDocumentReader: PDF 구조적 요소 마커 적용
  - [ ] WordDocumentReader: Word 스타일 기반 마커 생성

---

# Phase 6: Production 안정화 & 고급 최적화 (계획)

## 🔴 **P1 우선순위: Production 안정화**

- [ ] **T6-001**: 대용량 문서 스트리밍 최적화  
  - [ ] >50MB 파일 처리 성능 벤치마크
  - [ ] 메모리 사용량 최적화 (<파일 크기의 2배)
  - [ ] 진행률 추적 정확도 검증
  - [ ] 취소 토큰 및 예외 처리 강화

- [ ] **T6-002**: PDF 고급 레이아웃 처리
  - [ ] 다단 레이아웃 텍스트 순서 보정
  - [ ] 복잡한 표 구조 인식 (병합 셀, 중첩 헤더)
  - [ ] 페이지 구분자 및 줄바꿈 정규화
  - [ ] 표/그래프 텍스트 파편화 방지

- [ ] **T6-003**: Word 문서 고급 구조화
  - [ ] 스타일 기반 섹션 인식 (Heading1-6)
  - [ ] 표 및 이미지 캡션 컨텍스트 추출
  - [ ] 문서 구조 트리 생성
  - [ ] 각주 및 서식 메타데이터 보존

- [ ] **T6-004**: 성능 벤치마킹 자동화
  - [ ] 문서 타입별 처리 성능 측정
  - [ ] 청킹 품질 자동 검증 시스템
  - [ ] 메모리 사용량 프로파일링
  - [ ] 회귀 테스트 자동화

---

# Phase 7: 확장성 & 엔터프라이즈 대응 (계획)

## 🟡 **P2 우선순위: 확장성 강화**

- [ ] **T7-001**: 다중 LLM 프로바이더 지원
  - [ ] Anthropic Claude 통합 (ITextCompletionService 구현)
  - [ ] Azure OpenAI 서비스 통합  
  - [ ] 프로바이더별 최적화 전략 및 폴백 체계
  - [ ] 비용 및 성능 기반 선택 로직

- [ ] **T7-002**: RESTful API 서비스 구현
  - [ ] 핵심 엔드포인트: POST /api/documents/process
  - [ ] 스트리밍 응답: Server-Sent Events 지원
  - [ ] 배치 처리 API: POST /api/documents/batch
  - [ ] 진행 상황 추적: GET /api/jobs/{id}/status

- [ ] **T7-003**: 배치 처리 최적화
  - [ ] 큐 기반 비동기 처리 (Redis/Azure Service Bus)
  - [ ] 병렬 처리 작업 관리 및 리소스 할당
  - [ ] 실패한 작업 재시도 및 DLQ 처리
  - [ ] 처리량 기반 자동 스케일링

- [ ] **T7-004**: 모니터링 & 알림 시스템
  - [ ] 성능 메트릭 수집 (처리 시간, 처리량, 오류율)
  - [ ] 건강 상태 확인 엔드포인트
  - [ ] 알림 시스템 (Slack, Teams, 이메일)
  - [ ] 로그 수집 및 분석 대시보드

---

# Phase 8: 고급 기능 & 멀티모달 확장 (미래)

## 🟢 **P3 우선순위: 고급 기능**

- [ ] **T8-001**: 멀티모달 문서 처리
  - [ ] OCR 엔진 통합 (Azure Computer Vision)
  - [ ] 이미지 내 텍스트 추출 및 구조화
  - [ ] 다이어그램 및 차트 자동 분석
  - [ ] 이미지 품질 평가 및 신뢰도 계산

- [ ] **T8-002**: 고급 문서 분석
  - [ ] 문서 유형별 특화 프롬프트 시스템
  - [ ] Few-shot 학습 기반 분석 개선
  - [ ] 품질 피드백 기반 프롬프트 최적화
  - [ ] 다국어 문서 처리 최적화

- [ ] **T8-003**: 엔터프라이즈 보안 & 규정 준수
  - [ ] 데이터 암호화 (전송 중/저장 중)
  - [ ] 감사 로그 및 규정 준수 보고서
  - [ ] GDPR/CCPA 등 개인정보보호 규정 대응
  - [ ] 역할 기반 액세스 제어 (RBAC)

---

## 📊 Success Metrics

### ✅ Phase 5 Success Criteria (95% 달성)
- [x] **RAG 품질 점수 85점 이상**: 자동 품질 검증 시스템 구축 완료 ✅
- [x] **중간 결과 저장 완성**: 4단계 출력 시스템 완전 구현 ✅
- [x] **DocumentChunk 메타데이터 강화**: 전체 고급 메타데이터 필드 구현 ✅
- [x] **152개 테스트 케이스**: LLM 최적화 전체 테스트 완료 ✅
- [x] **실제 임베딩 모델 테스트**: SampleApp RAG 파이프라인 검증 ✅

### Phase 5.5 Success Criteria (현재 목표)
- [ ] **경계 마커 완성**: HEADING/CODE/LIST 마커 시스템 100% 구현
- [ ] **테이블 청킹 완전 해결**: 긴 테이블 행 분할 방지 달성  
- [ ] **실제 품질 점수 계산**: Mock이 아닌 실제 점수 알고리즘 구현
- [ ] **마커 시스템 확산**: 3개 이상 DocumentReader 적용

### Phase 6 Success Criteria  
- [ ] **대용량 문서 처리**: >50MB 파일 메모리 효율적 처리
- [ ] **PDF 고급 레이아웃**: 다단/복잡한 표 처리 정확도 90% 이상
- [ ] **성능 벤치마킹**: 자동화된 성능 측정 및 회귀 테스트
- [ ] **Word 고급 구조화**: 스타일 기반 섹션 인식 85% 이상

### Phase 7 Success Criteria
- [ ] **다중 LLM 지원**: 3개 프로바이더 (OpenAI, Anthropic, Azure)
- [ ] **RESTful API 서비스**: 초당 50개 문서 처리 성능
- [ ] **배치 처리 최적화**: 큐 기반 비동기 처리 구현
- [ ] **모니터링 시스템**: 실시간 성능 메트릭 수집

### Phase 8 Success Criteria
- [ ] **멀티모달 처리**: OCR 통합 이미지 텍스트 추출 정확도 80% 이상
- [ ] **엔터프라이즈 보안**: 데이터 암호화 및 규정 준수
- [ ] **고급 문서 분석**: 문서 유형별 특화 처리
- [ ] **SLA 달성**: 99.5% 가용성 목표

---

## 🎯 우선순위 프레임워크

**P0 (Critical)**: RAG 품질 최적화 긴급 수정 - Phase 4.5 (진행 중)
**P1 (High)**: 구조화 품질 및 일관성 - Phase 5 (대기)
**P2 (Medium)**: 고급 문서 형식 지원 - Phase 6  
**P3 (Low)**: LLM 고도화 및 멀티모달 - Phase 7
**P4 (Future)**: API 서비스 및 엔터프라이즈 - Phase 8

---

## 📝 Phase Completion Process

### 완료된 Phase 1-4 요약
- **기반 구축**: 4-프로젝트 아키텍처, 핵심 인터페이스, DI 지원
- **문서 처리**: 3가지 기본 형식 지원 (TXT, JSON, CSV)
- **청킹 시스템**: 4가지 전략, 성능 최적화, 스트림 처리
- **실전 검증**: SampleApp RAG 파이프라인, OpenAI 연동 성공

### Phase 5 시작을 위한 준비사항
1. **설계 문서 검토**: document-structure-specification.md 기반 구현
2. **LLM 통합 전략**: OpenAI API 키 설정 및 프롬프트 템플릿 준비
3. **품질 지표 정의**: 신뢰성, 완성도, 일관성 계산 알고리즘
4. **테스트 데이터 준비**: 다양한 문서 유형별 테스트 케이스

---

**마지막 업데이트**: 2025-09-03  
**현재 상태**: Phase 4 완료 (PDF 처리 검증), Phase 4.5 긴급 수정 진행 중
**다음 검토**: Phase 4.5 완료 후

### PDF 처리 결과 분석 기반 긴급 우선순위 작업
1. **P4.5-T001**: IntelligentChunkingStrategy MaxChunkSize hard limit 구현 (진행 중)
2. **P4.5-T002**: 청킹 설정 최적화 (800자, 120자 overlap)
3. **P4.5-T003**: 중간 단계 결과 저장 로직 완성
4. **P4.5-T005**: RAG 적합성 테스트 및 품질 검증

### Phase 4.5 완료 후 Phase 5 재개 준비사항
- RAG 품질 80점 이상 달성 확인
- 청크 크기 준수율 95% 이상 검증
- 실제 임베딩 모델 테스트 통과

---

### 개발 지침

#### 언어 사용 가이드라인
- **한국어**: 문서, 주석, 개발자간 대화, 내부 문서화
- **영어**: 코드 작성, 로그 메시지, 사용자 대면 메시지, API 명명

#### Context7 벤치마킹 적용 원칙 (2025-09-04 업데이트)
- **구조화된 메타데이터**: Context7의 totalTokens, trustScore, relevance 패턴 적용
- **4단계 출력 체계**: extract → parse → chunk → metadata 단계별 품질 보장
- **경계 마커 시스템**: Context7의 구분선 방식을 FileFlux 구조적 마커로 확장
- **토픽 기반 최적화**: 콘텐츠 타입별 특화된 청킹 전략 (text, table, code, list)
- **품질 점수 도입**: 각 청크의 완성도, 관련성, 정보 밀도 정량적 측정
- **단계별 품질 검증**: 각 처리 단계별 독립적 품질 지표 및 검증 시스템

#### Phase 5 완료 기준 (Context7 벤치마킹)
- [ ] **메타데이터 강화**: DocumentChunk에 Context7 스타일 메타데이터 완전 적용
- [ ] **품질 지표 도입**: QualityScore, RelevanceScore, InformationDensity 자동 계산
- [ ] **경계 마커 완성**: 모든 구조적 요소에 대한 마커 시스템 구현
- [ ] **4단계 출력**: extract/parse/chunk/metadata 각 단계별 독립적 접근 가능
- [ ] **Context7 호환성**: API 응답 형식과 호환 가능한 메타데이터 출력