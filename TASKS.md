# FileFlux 개발 로드맵 & 작업 계획

> 지능형 문서 구조화를 위한 .NET SDK - RAG 시스템 최적화 청크 생성

## 📋 프로젝트 개요

**목표**: 다양한 문서 형식을 파싱하고 AI 서비스를 활용하여 일관된 구조로 재구성한 텍스트 청크를 생성하는 .NET SDK 구현

**핵심 목적**: `Input: File` → `Output: 구조화된 텍스트 청크`
- 벡터화나 임베딩은 소비 애플리케이션의 책임
- FileFlux는 순수하게 문서 이해와 구조화에 집중

**현재 상태**: Phase 7 완료, Phase 8 시작 준비 (2025-01-07)

---

## ✅ 완료된 페이즈 요약 (Phase 1-7)

### Phase 1-4: 기반 구축 및 핵심 기능 (100% 완료)
- **아키텍처**: Clean Architecture, DI 지원, 4개 프로젝트 구조
- **문서 리더**: 8가지 형식 지원 (PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV)
- **청킹 전략**: 4가지 전략 (Intelligent, Semantic, Paragraph, FixedSize)
- **성능**: AsyncEnumerable 스트리밍, 메모리 효율적 처리

### Phase 5: RAG 품질 완성 (100% 완료)
- **LLM 통합**: ITextCompletionService 인터페이스 및 Mock 구현
- **품질 메타데이터**: QualityScore, RelevanceScore, StructuralRole
- **실전 검증**: SampleApp SQLite + OpenAI RAG 파이프라인

### Phase 6: 멀티모달 및 경계 마커 (100% 완료)
- **경계 마커**: HEADING/CODE/LIST 완전 구현
- **Image-to-Text**: IImageToTextService 인터페이스 및 Mock
- **테이블 처리**: 무결성 보존 청킹 알고리즘

### Phase 7: 품질 검증 시스템 (100% 완료)
- **품질 분석**: DocumentQualityAnalyzer, ChunkQualityEngine
- **벤치마킹**: 전략별 성능 비교, 최적 전략 추천
- **검증**: 202개 테스트 통과, OpenAI 실전 검증

---

## 🚀 새로운 개발 페이즈 (Phase 8-10)

### Phase 8: Embedding 기반 의미적 분석 시스템

**목표**: 문서 분석용 임베딩 서비스를 통한 청킹 품질 극대화 (2025년 1월)

#### 🔴 P0: 핵심 인터페이스 (1주차)

**T8-001**: IEmbeddingService 인터페이스 설계
- [ ] 인터페이스 정의 (GenerateEmbeddingAsync, CalculateSimilarity)
- [ ] EmbeddingPurpose enum (Analysis, SemanticSearch, Storage)
- [ ] 배치 처리 지원 인터페이스
- [ ] MockEmbeddingService 구현 (TF-IDF 기반)

**T8-002**: 의미적 경계 감지 시스템
- [ ] ISemanticBoundaryDetector 인터페이스
- [ ] Cosine similarity 임계값 시스템 (0.7)
- [ ] 문장/단락 간 유사도 계산
- [ ] 토픽 전환 감지 알고리즘

#### 🟡 P1: 품질 평가 시스템 (2-3주차)

**T8-003**: 청크 일관성 분석
- [ ] IChunkCoherenceAnalyzer 구현
- [ ] 청크 내 문장 평균 유사도 계산
- [ ] 일관성 점수 기반 품질 필터링
- [ ] 품질 개선 제안 생성

**T8-004**: 동적 오버랩 최적화
- [ ] IOverlapOptimizer 인터페이스
- [ ] 의미적 연결성 최대화 알고리즘
- [ ] 문서 타입별 최적 오버랩 계산
- [ ] 성능 vs 품질 트레이드오프 분석

#### 🟢 P2: 고급 분석 기능 (4주차)

**T8-005**: 토픽 클러스터링
- [ ] ITopicClusterAnalyzer 구현
- [ ] 문서 내 주요 토픽 자동 식별
- [ ] 청크별 토픽 할당 시스템
- [ ] 토픽 기반 검색 최적화

**T8-006**: 정보 밀도 계산
- [ ] IInformationDensityCalculator 구현
- [ ] 중복 vs 고유 정보 비율 계산
- [ ] 임베딩 기반 의미적 중복 감지
- [ ] 밀도 기반 청킹 크기 조정

### Success Criteria
- ✅ IEmbeddingService 완전 구현 및 테스트
- ✅ 의미적 경계 감지 정확도 85% 이상
- ✅ 청크 일관성 8-12% F1 Score 개선
- ✅ 토픽 검색 속도 25% 향상

---

### Phase 9: Adaptive Chunking & Advanced Boundaries

**목표**: Perplexity 기반 적응형 청킹과 문서 타입별 최적화 (2025년 2월)

#### 🔴 P0: Perplexity 기반 경계 감지 (1주차)

**T9-001**: Perplexity 계산 시스템
- [ ] IPerplexityBoundaryDetector 인터페이스
- [ ] PPL 계산 알고리즘 구현 (exp(-1/n ∑log P))
- [ ] 0.5B 파라미터 모델 통합
- [ ] 140초 이내 처리 성능 달성

**T9-002**: 하이브리드 경계 감지
- [ ] IHybridBoundaryDetector 구현
- [ ] PPL + Embedding 결합 (α=0.6)
- [ ] 동적 임계값 조정 시스템
- [ ] 13.56 F1 Score 목표

#### 🟡 P1: 문서 타입별 최적화 (2-3주차)

**T9-003**: 문서 타입 옵티마이저
- [ ] IDocumentTypeOptimizer 인터페이스
- [ ] Technical: 500-800 tokens, 20-30% overlap
- [ ] Legal: 300-500 tokens, 15-25% overlap
- [ ] Academic: 200-400 tokens, 25-35% overlap
- [ ] Financial: Element-based dynamic

**T9-004**: LLM 기반 청크 필터링
- [ ] ILLMChunkFilter 인터페이스
- [ ] 3단계 관련성 평가 시스템
- [ ] 자기반성 및 비평 메커니즘
- [ ] 10%p 정확도 향상 목표

#### 🟢 P2: 동적 메타데이터 생성 (4주차)

**T9-005**: 컨텍스트 메타데이터 생성
- [ ] IContextualMetadataGenerator 구현
- [ ] 전체 문서 컨텍스트 기반 설명 생성
- [ ] 67% 검색 실패율 감소 목표
- [ ] 캐싱 시스템 구현

**T9-006**: 쿼리 기반 필터링
- [ ] IQueryBasedFilter 인터페이스
- [ ] 자연어 쿼리 파싱
- [ ] 시간/지역/복잡도 자동 추출
- [ ] 동적 필터 적용 시스템

### Success Criteria
- ✅ Perplexity 경계 감지 구현
- ✅ 문서 타입별 자동 최적화
- ✅ 13.56 F1 Score 달성
- ✅ 67% 검색 실패율 감소

---

### Phase 10: Production 최적화 & 확장성

**목표**: 엔터프라이즈급 성능과 확장성 달성 (2025년 3월)

#### 🔴 P0: 성능 최적화 (1-2주차)

**T10-001**: 메모리 효율성
- [ ] Iterator 기반 처리 완전 전환
- [ ] LRU 캐싱 시스템 (maxsize=1000)
- [ ] 메모리 사용량 50% 감소
- [ ] 대용량 파일 스트리밍 최적화

**T10-002**: 병렬 처리 최적화
- [ ] 배치 크기 최적화 (100 문서)
- [ ] Ray Data 스타일 스트리밍
- [ ] 3-8x 처리량 향상
- [ ] 적응형 리소스 할당

#### 🟡 P1: 멀티모달 고도화 (3주차)

**T10-003**: OCR-free Vision RAG
- [ ] IOCRFreeVisionProcessor 인터페이스
- [ ] ColPali 아키텍처 스타일 구현
- [ ] Multi-vector retrieval
- [ ] 12% 정확도 향상 목표

**T10-004**: 통합 임베딩 생성
- [ ] 텍스트-이미지 통합 임베딩
- [ ] 4-6 페이지 배치 처리
- [ ] 구조 무결성 보존
- [ ] 5x 더 체계적인 청크

#### 🟢 P2: 엔터프라이즈 기능 (4주차)

**T10-005**: 그래프 기반 문서 이해
- [ ] 지식 그래프 구축
- [ ] 교차 참조 매핑
- [ ] 인용 네트워크 분석
- [ ] 정보 계보 추적

**T10-006**: 연합 처리 아키텍처
- [ ] 분산 처리 시스템
- [ ] 엣지-클라우드 하이브리드
- [ ] 프라이버시 보존 처리
- [ ] 실시간 인덱스 업데이트

### Success Criteria
- ✅ 처리 속도 2-3배 향상
- ✅ 메모리 사용량 50% 감소
- ✅ 12% Vision RAG 정확도 향상
- ✅ 엔터프라이즈 확장성 달성

---

## 📊 전체 성과 지표

### 현재까지 달성 (Phase 1-7)
- ✅ 202개 테스트 통과 (97% 성공률)
- ✅ 8가지 파일 형식 완벽 지원
- ✅ 4가지 청킹 전략 구현
- ✅ 실제 RAG 파이프라인 검증

### 목표 성과 (Phase 8-10)
- 🎯 검색 정확도 40-67% 향상
- 🎯 F1 Score 13.56 달성
- 🎯 처리 속도 2-3배 개선
- 🎯 메모리 사용량 50% 감소

---

## 🎯 우선순위 매트릭스

### 즉시 실행 (P0) - 2025년 1월
1. **IEmbeddingService 인터페이스** - Phase 8 기반
2. **의미적 경계 감지** - 청킹 품질 핵심
3. **문서 타입별 파라미터** - 즉시 적용 가능

### 단기 목표 (P1) - 2025년 2월
1. **Perplexity 경계 감지** - 고급 청킹
2. **청크 일관성 평가** - 품질 보증
3. **LLM 청크 필터링** - 정확도 향상

### 중기 목표 (P2) - 2025년 3월
1. **OCR-free Vision** - 멀티모달 확장
2. **그래프 기반 이해** - 고급 분석
3. **연합 처리** - 엔터프라이즈 확장

---

## 📝 개발 원칙

### FileFlux 핵심 역할
- ✅ **Reading**: 다양한 파일 형식 → 구조화된 텍스트
- ✅ **Parsing**: 문서 구조 분석 → 의미적 섹션 추출
- ✅ **Enriching**: RAG 최적화 청킹 → 고품질 메타데이터

### FileFlux가 하지 않는 것
- ❌ AI 서비스 구현 (인터페이스만 제공)
- ❌ 임베딩 생성 (분석용 제외)
- ❌ 벡터 저장소
- ❌ Web API 서비스

### 아키텍처 원칙
- **인터페이스 우선**: 구현체가 아닌 계약 정의
- **DI 기반**: 느슨한 결합, 높은 확장성
- **Mock 제공**: 테스트 가능한 설계
- **성능 우선**: 스트리밍, 병렬 처리 기본

---

**마지막 업데이트**: 2025-01-07
**현재 페이즈**: Phase 8 시작 준비
**다음 마일스톤**: IEmbeddingService 구현 (2025년 1월 2주차)