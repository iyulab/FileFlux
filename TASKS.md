# FileFlux 개발 로드맵 & 작업 계획

> 지능형 문서 구조화를 위한 .NET SDK - RAG 시스템 최적화 청크 생성

## 📋 프로젝트 개요

**목표**: 다양한 문서 형식을 파싱하고 AI 서비스를 활용하여 일관된 구조로 재구성한 텍스트 청크를 생성하는 .NET SDK 구현

**핵심 목적**: `Input: File` → `Output: RAG-Ready 구조화된 텍스트 청크`

### 🎯 FileFlux의 명확한 책임 범위

#### ✅ FileFlux가 담당하는 것 (RAG 준비)
- **문서 파싱**: 8가지 파일 형식 → 구조화된 텍스트 변환
- **청킹 최적화**: RAG 검색 품질 극대화를 위한 의미적 청킹
- **메타데이터 추출**: 구조적 힌트, 품질 점수, 경계 마커 생성
- **품질 보증**: 청크 일관성, 완전성, 검색 가능성 검증
- **성능 최적화**: 대용량 문서 스트리밍, 병렬 처리, 캐싱

#### ❌ FileFlux가 담당하지 않는 것 (소비 앱 책임)
- **임베딩 생성**: 벡터 변환은 소비 애플리케이션 책임
- **벡터 저장**: Pinecone, Qdrant 등 벡터 DB 통합 제외
- **검색 기능**: 유사도 검색, 쿼리 처리 제외
- **그래프 구축**: 지식 그래프, 관계 네트워크는 범위 밖
- **LLM 응답 생성**: RAG 완성은 소비 애플리케이션 책임

**현재 상태**: Phase 10 완료, v0.2.0 준비 (2025-09-08)
**개발 버전**: v0.x (과감한 리팩토링 허용, 하위호환성 불필요)

---

## ✅ 완료된 작업 요약 (Phase 1-9)

### 🎯 Phase 1-7: 기반 구축 및 핵심 완성
- **8가지 문서 형식**: PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV
- **4가지 청킹 전략**: Intelligent, Semantic, Paragraph, FixedSize
- **멀티모달 지원**: Image-to-Text, 경계 마커 시스템
- **품질 검증**: DocumentQualityAnalyzer, ChunkQualityEngine
- **성과**: 202개 테스트 통과, OpenAI 실전 검증 완료

### 🎯 Phase 8: 임베딩 기반 의미적 분석 (완료)
- **IEmbeddingService**: TF-IDF 기반 Mock 구현
- **ISemanticBoundaryDetector**: 코사인 유사도 경계 감지
- **IChunkCoherenceAnalyzer**: 청크 일관성 평가
- **성과**: 221/227 테스트 통과 (97.4%)

### 🎯 Phase 9: 적응형 청킹 시스템 (완료)
- **IStatisticalBoundaryDetector**: 통계적 불확실성 기반 경계 감지
- **IHybridBoundaryDetector**: Statistical + Embedding 하이브리드
- **IDocumentTypeOptimizer**: 문서 타입별 자동 최적화
- **ILLMChunkFilter**: 3단계 LLM 품질 필터링
- **성과**: 228/236 테스트 통과 (96.6%)
- **리팩토링**: 서비스명 제거, 기술 중립적 명명 완료

### 🎯 Phase 10: Production 최적화 & 확장성 (완료)
- **LruMemoryCache**: LRU 캐싱 시스템 (maxsize=1000)
- **MemoryEfficientProcessor**: AsyncEnumerable 스트리밍, 50% 메모리 절감
- **ParallelBatchProcessor**: TPL Dataflow 기반 3-8x 처리량 향상
- **PerformanceBenchmark**: F1 Score, 메모리, 처리속도 벤치마킹
- **성과**: 215/220 테스트 통과 (97.7%)

---

## 🚀 진행 예정 작업 (Phase 11)

### Phase 11: RAG 품질 극대화 & v0.2.0 Release

**목표**: RAG 검색 품질 최적화 및 Production-ready SDK (2025년 9월)

#### 🔴 P0: RAG 품질 개선 (테스트 기반)

**T11-001**: 청킹 품질 검증 시스템
- [ ] RAGQualityBenchmark 테스트 스위트 구축
- [ ] 검색 재현율(Recall) 측정 시스템
- [ ] 청크 크기 분포 최적화 검증
- [ ] 오버랩 효과성 측정

**T11-002**: 경계 감지 정확도 향상
- [ ] SemanticBoundaryDetector 테스트 수정 (5개)
- [ ] 구조적 경계 감지 개선 (헤딩, 섹션, 단락)
- [ ] 코드 블록 경계 보존 강화
- [ ] 테이블/리스트 무결성 보장

**T11-003**: 메타데이터 품질 강화
- [ ] 구조적 역할 자동 분류 (제목, 본문, 캡션, 참조)
- [ ] 청크 중요도 점수 정교화
- [ ] 컨텍스트 보존 메타데이터 추가
- [ ] 계층 구조 정보 포함

#### 🟡 P1: 성능 및 안정성 (테스트 기반)

**T11-004**: 스트리밍 처리 최적화
- [ ] 대용량 파일 처리 테스트 (100MB+)
- [ ] 메모리 프로파일링 및 누수 검사
- [ ] 백프레셔 처리 검증
- [ ] 취소 토큰 전파 테스트

**T11-005**: 병렬 처리 신뢰성
- [ ] 동시성 스트레스 테스트
- [ ] 데드락 감지 및 방지
- [ ] 리소스 경합 최소화
- [ ] 오류 복구 메커니즘

**T11-006**: 캐싱 효과성 검증
- [ ] 캐시 히트율 최적화
- [ ] 캐시 무효화 전략 테스트
- [ ] 메모리 압력 하 동작 검증
- [ ] 캐시 워밍업 시나리오

#### 🟢 P2: v0.2.0 릴리즈 준비

**T11-007**: 문서화 및 샘플
- [ ] RAG 통합 가이드 작성
- [ ] 청킹 전략 선택 가이드
- [ ] 성능 튜닝 가이드
- [ ] 실전 샘플 코드 (OpenAI, Azure, Anthropic)

**T11-008**: NuGet 패키지 배포
- [ ] 패키지 메타데이터 최적화
- [ ] 의존성 최소화 검증
- [ ] Breaking Changes 문서화
- [ ] 마이그레이션 도구 제공

---

## 📁 프로젝트 구조 & 테스트 경로

### 소스 코드 구조
```
/src/
  /FileFlux.Core/           # 인터페이스 및 추상화
  /FileFlux.Domain/         # 도메인 모델
  /FileFlux.Infrastructure/ # 구현체
  /FileFlux.Tests/          # 단위 및 통합 테스트
  /FileFlux.SampleApp/      # 샘플 애플리케이션
```

### 테스트 파일 경로
```
/src/FileFlux.Tests/
  /Readers/                 # 문서 리더 테스트
  /Strategies/              # 청킹 전략 테스트
  /Services/                # 서비스 구현 테스트
  /Integration/             # 통합 테스트
  /Quality/                 # 품질 분석 테스트
  /Manual/                  # 수동 실행 테스트
  /Mocks/                   # Mock 구현체
```

### 테스트 실행 명령
```bash
# 모든 테스트 실행
dotnet test src/FileFlux.sln

# 특정 카테고리 테스트
dotnet test --filter "FullyQualifiedName~Services"

# 성능 테스트
dotnet test --filter "Category=Performance"

# 커버리지 리포트
dotnet test --collect:"XPlat Code Coverage"
```

---

## 🔧 v0.x 개발 원칙 (과감한 리팩토링)

### 🎯 근본적 문제 해결 원칙
- **본질 우선**: 임시방편적 해결책보다 근본 원인 파악과 본질적 해결 추구
- **장기적 관점**: 당장의 문제 해결보다 장기적 관점에서 근본적 개선 지향
- **구조적 개선**: 증상 치료가 아닌 구조적 문제 해결에 집중
- **기술 부채 제거**: Quick fix 대신 올바른 설계와 구현 선택
- **원칙 기반 접근**: 단기 이익보다 아키텍처 원칙과 패턴 준수

### ✅ 허용되는 Breaking Changes
- **인터페이스 변경**: 더 나은 설계를 위한 인터페이스 수정
- **네이밍 변경**: Enhanced, Advanced 등 접두어 제거
- **구조 개편**: 패키지 구조 및 네임스페이스 재구성
- **API 재설계**: 사용성 개선을 위한 API 변경
- **레거시 제거**: 기술 부채가 된 코드의 완전한 재작성

### 🎯 리팩토링 우선순위
1. **근본 원인 해결**: 문제의 증상이 아닌 원인에 집중
2. **기술 중립성**: 특정 서비스명 제거 (완료)
3. **명확한 명명**: 기능을 정확히 표현하는 이름
4. **성능 우선**: 느린 코드는 과감히 재작성
5. **테스트 우선**: 테스트 없는 코드는 삭제 고려
6. **미래 대비**: 확장성과 유지보수성을 고려한 설계

### 📝 리팩토링 체크리스트
- [ ] 근본 원인 분석 완료
- [ ] 장기적 영향 평가
- [ ] 불필요한 복잡도 제거
- [ ] 중복 코드 통합
- [ ] 성능 병목 제거
- [ ] 테스트 가능성 향상
- [ ] 문서화 개선
- [ ] 기술 부채 해결

---

## 📊 성과 지표 & 목표

### 현재 달성 (Phase 1-10)
- ✅ **테스트**: 215/220 통과 (97.7%)
- ✅ **문서 형식**: 8가지 완벽 지원
- ✅ **청킹 전략**: 4가지 + 하이브리드 + 적응형
- ✅ **품질 시스템**: 3단계 평가 + LLM 필터링
- ✅ **성능 최적화**: 메모리 50% 감소, 3-8x 병렬 처리
- ✅ **캐싱 시스템**: LRU 캐시로 10x 반복 처리 개선

### Phase 11 목표 (RAG 준비 품질 집중)
- 🎯 **RAG 품질**: 검색 재현율 85% 이상 달성
- 🎯 **테스트**: 100% 통과율 (220/220) + RAG 품질 테스트
- 🎯 **청킹 정확도**: F1 Score 13.56% 향상 검증
- 🎯 **성능**: 100MB 문서 10초 내 처리
- 🎯 **릴리즈**: v0.2.0 NuGet 패키지 배포

---

## 🎯 즉시 실행 작업 (This Week)

### 1. RAG 품질 테스트 구축
```bash
# RAG 품질 벤치마크 테스트 생성
/src/FileFlux.Tests/RAG/RAGQualityBenchmark.cs

# 검색 재현율 측정
/src/FileFlux.Tests/RAG/RetrievalRecallTests.cs

# 청크 분포 분석
/src/FileFlux.Tests/RAG/ChunkDistributionTests.cs
```

### 2. 실패 테스트 수정 (5개)
```bash
# SemanticBoundaryDetector 테스트 수정
dotnet test --filter "FullyQualifiedName~SemanticBoundaryDetector"

# 테스트 위치
/src/FileFlux.Tests/Services/SemanticBoundaryDetectorTests.cs
```

### 3. 테스트 기반 구현 계획
- [ ] 각 기능별 테스트 먼저 작성 (TDD)
- [ ] 테스트 커버리지 90% 이상 유지
- [ ] 성능 테스트 자동화

---

## 📝 개발 규칙 & 가이드라인

### 코드 작성 원칙
1. **인터페이스 우선**: 구현 전 인터페이스 정의
2. **Mock 제공**: 모든 인터페이스에 Mock 구현
3. **테스트 필수**: 기능당 최소 3개 테스트
4. **성능 고려**: AsyncEnumerable, 병렬 처리 기본

### 커밋 메시지 규칙
```
feat: 새로운 기능 추가
fix: 버그 수정
refactor: 리팩토링 (v0.x 과감히)
perf: 성능 개선
test: 테스트 추가/수정
docs: 문서 업데이트
```

### PR 체크리스트
- [ ] 테스트 통과
- [ ] 문서 업데이트
- [ ] 성능 영향 평가
- [ ] Breaking Change 명시

---

**마지막 업데이트**: 2025-09-08
**현재 버전**: v0.1.5-dev → v0.2.0 준비
**현재 페이즈**: Phase 10 완료, Phase 11 준비
**다음 마일스톤**: v0.2.0 릴리즈 및 NuGet 패키지 배포