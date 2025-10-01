# Building FileFlux

## Quick Start

### Fast Build & Test (권장)
```powershell
# Windows PowerShell
.\scripts\quick-build.ps1

# 또는 직접 실행
cd src
dotnet build                    # 빌드만 (5-6초)
```

### 빌드만 수행
```bash
cd src
dotnet build
```
- **소요 시간**: ~5초
- **용도**: 빌드 검증, 코드 오류 확인

### 단위 테스트만 실행
```bash
cd src
dotnet test --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~Benchmark"
```
- **소요 시간**: ~30초
- **용도**: 빠른 로직 검증
- **제외**: 통합 테스트, 벤치마크 테스트

### 전체 테스트 (통합 테스트 포함)
```powershell
# Windows PowerShell
.\scripts\full-test.ps1

# 또는 직접 실행
cd src
dotnet test
```
- **소요 시간**: ~10-15분
- **용도**: 완전한 품질 검증
- **포함**: 통합 테스트, 벤치마크, RAG 품질 테스트

## 느린 테스트 이유

### 통합 테스트 (Integration)
- 실제 3MB PDF 문서 처리
- 여러 문서 형식 동시 테스트
- 전체 파이프라인 검증

### 벤치마크 테스트 (Benchmark)
- 성능 측정 및 통계
- 반복 실행으로 정확도 확보

### RAG 품질 테스트
- Context7 메타데이터 검증
- 청킹 품질 평가
- 여러 전략 비교

## CI/CD

GitHub Actions는 빌드만 수행하여 빠른 피드백을 제공합니다.
- 테스트는 로컬에서 수동 실행
- 비용과 시간 절약

## 프로젝트 구조

```
FileFlux/
├── src/
│   ├── FileFlux.Domain/          # 도메인 모델
│   ├── FileFlux.Core/             # 인터페이스
│   ├── FileFlux.Contracts/        # 외부 계약
│   ├── FileFlux.Infrastructure/   # 구현체 (NuGet 패키지)
│   └── FileFlux.sln
├── test/
│   └── FileFlux.Tests/            # 테스트 (단위, 통합, 벤치마크)
├── samples/
│   ├── FileFlux.SampleApp/        # 사용 예제
│   └── ...
└── scripts/
    ├── quick-build.ps1            # 빠른 빌드 & 단위 테스트
    └── full-test.ps1              # 전체 테스트 스위트
```

## 개발 워크플로

### 일상적인 개발
```bash
cd src
dotnet build        # 변경사항 빌드 확인
```

### 기능 완성 후
```powershell
.\scripts\quick-build.ps1    # 단위 테스트 검증
```

### PR 제출 전
```powershell
.\scripts\full-test.ps1      # 전체 테스트 통과 확인
```

## 요구사항

- **.NET SDK**: 9.0 이상
- **OS**: Windows, Linux, macOS
- **IDE**: Visual Studio 2022 17.8+ 또는 VS Code

## 문제 해결

### 빌드 실패
```bash
# 클린 빌드
dotnet clean
dotnet build --no-incremental
```

### 테스트 실패
```bash
# 특정 테스트만 실행
dotnet test --filter "FullyQualifiedName~YourTestName"

# 상세 로그
dotnet test --verbosity detailed
```

### 솔루션 문제
```bash
# 솔루션 재생성
cd src
rm FileFlux.sln
dotnet new sln -n FileFlux
dotnet sln add **/*.csproj
```
