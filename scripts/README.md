# FileFlux Test Scripts

이 디렉터리에는 FileFlux 테스트 환경 관리를 위한 PowerShell 스크립트들이 포함되어 있습니다.

## Scripts

### 🧹 cleanup-test-env.ps1
테스트 환경을 정리하여 PDF 파일만 남기고 모든 처리 결과를 제거합니다.

**사용법:**
```powershell
# 기본 실행
.\scripts\cleanup-test-env.ps1

# PowerShell에서 직접 실행
PowerShell -File .\scripts\cleanup-test-env.ps1
```

**제거되는 항목:**
- `chunking-results/` 디렉터리
- `extraction-results/` 디렉터리  
- `parsing-results/` 디렉터리
- `logs/` 디렉터리
- PDF가 아닌 모든 파일

**보존되는 항목:**
- `*.pdf` 파일들

### 🚀 run-pdf-tests.ps1
PDF 처리 통합 테스트와 RAG 적합성 테스트를 실행합니다.

**사용법:**
```powershell
# 기본 실행
.\scripts\run-pdf-tests.ps1

# 먼저 환경 정리 후 실행
.\scripts\run-pdf-tests.ps1 -CleanFirst

# 상세한 출력으로 실행
.\scripts\run-pdf-tests.ps1 -Verbose

# Release 빌드로 실행
.\scripts\run-pdf-tests.ps1 -Configuration Release

# 모든 옵션 조합
.\scripts\run-pdf-tests.ps1 -CleanFirst -Verbose -Configuration Release
```

**매개변수:**
- `-CleanFirst`: 테스트 실행 전 환경 정리
- `-Verbose`: 상세한 테스트 출력
- `-Configuration`: 빌드 구성 (Debug/Release, 기본값: Debug)

**실행하는 테스트:**
- `PdfProcessingIntegrationTests`: PDF 처리 통합 테스트
- `RagSuitabilityTests`: RAG 시스템 적합성 검증 테스트

## 일반적인 워크플로

1. **환경 정리**:
   ```powershell
   .\scripts\cleanup-test-env.ps1
   ```

2. **테스트 실행**:
   ```powershell
   .\scripts\run-pdf-tests.ps1
   ```

3. **결과 확인**:
   - 콘솔 출력에서 테스트 결과 확인
   - `test/test-a/chunking-results/` 에서 상세 결과 파일 확인

## 예상 결과

성공적인 테스트 실행 후:
- `chunking-results/` 디렉터리에 타임스탬프가 포함된 결과 폴더 생성
- 청크 통계, 개별 청크 파일, 전체 청크 파일 생성
- RAG 품질 점수 84.5/100 이상 달성
- 100% 크기 규정 준수율

## 문제 해결

### 권한 오류
```powershell
# PowerShell 실행 정책 확인
Get-ExecutionPolicy

# 필요시 실행 정책 변경 (관리자 권한)
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 경로 오류
- 스크립트는 FileFlux 프로젝트 루트에서 실행해야 합니다
- 상대 경로가 올바른지 확인하세요

### 빌드 실패
```powershell
# 수동 빌드 시도
dotnet restore src\FileFlux.sln
dotnet build src\FileFlux.sln
```