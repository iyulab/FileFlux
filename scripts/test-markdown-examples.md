# 📝 test-markdown.ps1 사용 예시

Markdown 문서 테스트를 위한 통합 스크립트 사용법입니다.

## 🎯 기본 사용법

### 환경 정리 후 테스트 실행
```powershell
.\scripts\test-markdown.ps1 -CleanFirst
```

### 환경 정리만 수행
```powershell
.\scripts\test-markdown.ps1 -CleanOnly
```

### 테스트만 실행 (정리 안함)
```powershell
.\scripts\test-markdown.ps1 -TestOnly
```

### 기본 실행 (현재 환경에서 테스트)
```powershell
.\scripts\test-markdown.ps1
```

## 🔧 고급 옵션

### 다른 Markdown 파일 테스트
```powershell
.\scripts\test-markdown.ps1 -TestFile "example.md" -CleanFirst
```

### 상세한 출력으로 실행
```powershell
.\scripts\test-markdown.ps1 -Verbose -CleanFirst
```

### Release 빌드로 테스트
```powershell
.\scripts\test-markdown.ps1 -Configuration Release -CleanFirst
```

### 모든 옵션 조합
```powershell
.\scripts\test-markdown.ps1 -CleanFirst -Verbose -Configuration Release -TestFile "custom.md"
```

## 📂 대상 디렉터리

- **테스트 디렉터리**: `D:\data\FileFlux\test\test-b`
- **기본 테스트 파일**: `test.md`
- **결과 저장**: `test-b/chunking-results/`

## 🧹 정리 대상

### 제거되는 항목
- `chunking-results/` 디렉터리
- `extraction-results/` 디렉터리  
- `parsing-results/` 디렉터리
- `logs/` 디렉터리
- Markdown 파일이 아닌 모든 파일

### 보존되는 항목
- `*.md` 파일들

## 📊 출력 정보

실행 후 다음 정보를 확인할 수 있습니다:

- 청크 개수 및 평균 크기
- 최대 청크 크기
- 사용된 청킹 전략
- 결과 파일 위치
- 통계 JSON 파일

## 🚨 문제 해결

### Markdown 파일이 없는 경우
```powershell
# 사용 가능한 .md 파일 확인
Get-ChildItem D:\data\FileFlux\test\test-b -Filter "*.md"

# 특정 파일로 테스트
.\scripts\test-markdown.ps1 -TestFile "your-file.md"
```

### 권한 문제
```powershell
# 실행 정책 확인
Get-ExecutionPolicy

# 필요시 변경
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 빌드 실패
```powershell
# 수동 빌드 시도
dotnet restore src\FileFlux.sln
dotnet build src\FileFlux.sln
```

## 💡 사용 팁

1. **첫 테스트**: `-CleanFirst` 옵션으로 깨끗한 환경에서 시작
2. **반복 테스트**: `-TestOnly`로 빠른 테스트 반복
3. **디버깅**: `-Verbose` 옵션으로 상세 정보 확인
4. **다양한 파일**: `-TestFile` 옵션으로 여러 Markdown 파일 테스트