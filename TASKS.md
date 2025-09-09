# FileFlux 개발 로드맵 & 작업 계획

> 지능형 문서 구조화를 위한 .NET SDK - RAG 시스템 최적화 청크 생성

## 📋 프로젝트 개요

**목표**: 다양한 문서 형식을 파싱하고 AI 서비스를 활용하여 일관된 구조로 재구성한 텍스트 청크를 생성하는 .NET SDK 구현

**핵심 목적**: `Input: File` → `Output: RAG-Ready 구조화된 텍스트 청크`

**현재 상태**: Phase 8 완료, 엔터프라이즈급 성능 최적화 달성 (2025-09-09)
**개발 버전**: v0.1.6 → v0.2.0 준비

---

## ✅ 완료된 작업 (Phase 1-8 통합)

### 📦 핵심 기능 구현 완료
- **문서 지원**: 8가지 형식 (PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV)
- **청킹 전략**: 4가지 전략 + 하이브리드 + 적응형 시스템
- **품질 시스템**: 3단계 평가, LLM 필터링, 통계적 경계 감지
- **성능 최적화**: AsyncEnumerable 스트리밍, LRU 캐싱, 엔터프라이즈급 병렬 처리
- **테스트 인프라**: 235+ 테스트, OpenAI API 통합, Mock 서비스
- **멀티모달 처리**: PDF 이미지 추출 및 텍스트 변환 파이프라인

### 🚀 Phase 8: 엔터프라이즈급 성능 최적화 (신규)
- **병렬 처리 엔진**: CPU 코어별 동적 스케일링, 메모리 백프레셔 제어
- **스트리밍 최적화**: 실시간 청크 반환, 캐시 우선 검사
- **지능형 캐시**: 파일 해시 기반 LRU 캐시, 자동 만료 관리
- **Threading.Channels**: 고성능 비동기 채널 기반 백프레셔 시스템
- **DI 통합**: 모든 새 서비스가 AddFileFlux()로 자동 등록

### 🎯 주요 성과
- **테스트 커버리지**: 235/235 테스트 통과 (100%)
- **성능**: 3MB PDF → 511청크, 1.3초 처리
- **메모리 효율**: 파일 크기 2배 이하 사용
- **API 통합**: OpenAI gpt-4o-mini 실전 검증
- **병렬 성능**: CPU 코어별 선형 확장, 메모리 백프레셔 제어
- **캐시 효율**: 동일 문서 재처리 시 즉시 반환

---

## 🚀 Phase 9: RAG 품질 극대화 (다음 단계)

### 🔴 P0: 청킹 품질 근본 개선 (즉시 착수)

**T9-001**: 문장 경계 기반 스마트 청킹 [Critical]
- [ ] 문장 경계 인식 청킹 알고리즘 구현
- [ ] 의미적 완결성 보장 로직 (최소 70% 완성도)
- [ ] 단락 무결성 유지 메커니즘
- [ ] 컨텍스트 오버랩 기능 수정 및 검증
- **성공 기준**: 청크 완성도 70% 이상, 문장 중단 0%

**T9-002**: 실제 API 기반 RAG 품질 벤치마크 [Critical]
- [ ] samples/FileFlux.RealWorldBenchmark 프로젝트 확장
- [ ] OpenAI embedding API 통합 (text-embedding-3-small)
- [ ] 검색 정확도 측정 시스템 (Precision/Recall/F1)
- [ ] 다양한 문서 타입별 품질 메트릭
- [ ] A/B 테스트: Mock vs Real API 성능 비교
- **성공 기준**: 검색 재현율 85% 이상

**T9-003**: 청킹 전략별 실전 검증 [High]
- [ ] 각 전략별 RAG 검색 품질 측정
- [ ] 문서 타입별 최적 전략 자동 선택
- [ ] 청크 크기 vs 검색 정확도 상관관계 분석
- [ ] 오버랩 크기 최적화 (0, 64, 128, 256 비교)
- **성공 기준**: 전략별 성능 지표 문서화

### 🟡 P1: 품질 측정 및 개선 시스템

**T11-004**: RAG 품질 자동화 테스트 스위트 [High]
- [ ] End-to-End RAG 파이프라인 테스트
- [ ] 실제 쿼리 기반 검색 테스트 (100+ 쿼리)
- [ ] 청크 품질 점수 자동 계산
- [ ] 회귀 테스트 자동화
- **성공 기준**: CI/CD 통합 가능한 품질 테스트

**T11-005**: 도메인별 청킹 최적화 [Medium]
- [ ] 기술 문서: 코드 블록 무결성 우선
- [ ] 법률 문서: 조항/섹션 구조 보존
- [ ] 의료 문서: 용어 컨텍스트 유지
- [ ] 학술 논문: 참조/인용 연결성
- **성공 기준**: 도메인별 F1 Score 10% 향상

**T11-006**: 실시간 품질 모니터링 [Medium]
- [ ] 청킹 품질 실시간 대시보드
- [ ] 성능 메트릭 수집 (처리 시간, 메모리)
- [ ] 품질 저하 알림 시스템
- [ ] 사용 패턴 분석 및 최적화 제안

### 🟢 P2: 프로덕션 준비 및 배포

**T11-007**: SDK 사용 가이드 [Low]
- [ ] 기본 통합 패턴 문서화
- [ ] 청킹 전략 선택 가이드
- [ ] 성능 튜닝 가이드
- [ ] 품질 메트릭 활용법

**T11-008**: v0.2.0 릴리즈 [Low]
- [ ] Breaking Changes 문서화
- [ ] 마이그레이션 가이드
- [ ] NuGet 패키지 최적화
- [ ] 릴리즈 노트 작성

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

## 📊 성과 지표 & 현재 이슈

### ✅ 현재 달성 (Phase 1-10)
- **테스트**: 224/224 통과 (100%)
- **문서 형식**: 8가지 완벽 지원
- **API 통합**: OpenAI API 실전 검증 완료
- **성능**: 3MB PDF → 511청크, 1.3초

### ⚠️ 핵심 품질 이슈 (해결 필요)
- **청크 완성도**: 현재 20% (목표 70%)
- **문장 중단**: 빈번한 문장 중간 절단
- **오버랩 기능**: 동작하지 않음
- **검색 정확도**: 측정 시스템 부재

### 🎯 Phase 11 목표
- **청크 완성도**: 70% 이상 달성
- **검색 재현율**: 85% 이상
- **문장 무결성**: 100% 보장
- **실제 API 벤치마크**: 완전 구축

---

## 🔥 즉시 실행 작업 (Today)

### 1. 청킹 품질 근본 개선
```csharp
// 문장 경계 인식 청킹 구현
/src/FileFlux.Infrastructure/Strategies/SmartChunkingStrategy.cs

// 기존 IntelligentStrategy 개선
/src/FileFlux.Infrastructure/Strategies/IntelligentChunkingStrategy.cs
```

### 2. 실제 API 벤치마크 구축
```csharp
// RealWorldBenchmark 확장
/samples/FileFlux.RealWorldBenchmark/Benchmarks/RAGQualityBenchmark.cs

// Embedding API 통합
/samples/FileFlux.RealWorldBenchmark/Services/OpenAiEmbeddingService.cs
```

### 3. 품질 메트릭 시스템
```csharp
// 검색 정확도 측정
/samples/FileFlux.RealWorldBenchmark/Metrics/RetrievalMetrics.cs

// 청크 품질 분석
/samples/FileFlux.RealWorldBenchmark/Metrics/ChunkQualityMetrics.cs
```

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

**마지막 업데이트**: 2025-09-09
**현재 버전**: v0.1.5 → v0.2.0 준비
**현재 페이즈**: Phase 11 RAG 품질 개선 진행 중
**다음 마일스톤**: 청킹 품질 70% 달성 → v0.2.0 릴리즈