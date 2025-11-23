# FileFlux Scripts

이 폴더에는 FileFlux 개발 및 배포를 위한 유틸리티 스크립트들이 포함되어 있습니다.

## 빠른 시작

```powershell
# CLI 로컬 배포 (올바른 명령어)
.\scripts\deploy-cli-local.ps1

# ❌ 틀린 명령어: .\scripts\deploy-local.ps1
# ✅ 올바른 명령어: .\scripts\deploy-cli-local.ps1
```

## 스크립트 목록

### 🚀 배포 스크립트

#### `deploy-cli-local.ps1`
FileFlux CLI를 로컬 사용자 디렉토리에 배포합니다.

**기본 사용법:**
```powershell
.\scripts\deploy-cli-local.ps1
```

**옵션:**
- `-InstallPath`: 설치 경로 지정 (기본값: `$env:LOCALAPPDATA\FileFlux`)
- `-Configuration`: 빌드 구성 (Debug/Release, 기본값: Release)
- `-AddToPath`: PATH에 자동 추가 (기본값: true)
- `-SkipBuild`: 빌드 건너뛰기

**예제:**
```powershell
# 기본 배포 (권장)
.\scripts\deploy-cli-local.ps1

# 커스텀 경로에 배포
.\scripts\deploy-cli-local.ps1 -InstallPath "C:\Tools\FileFlux"

# Debug 빌드 배포
.\scripts\deploy-cli-local.ps1 -Configuration Debug

# 기존 빌드 사용 (빌드 건너뛰기)
.\scripts\deploy-cli-local.ps1 -SkipBuild
```

**배포 후:**
1. 터미널 재시작
2. `fileflux --help` 실행
3. OpenAI API 키 설정:
   ```powershell
   $env:OPENAI_API_KEY = "your-api-key"
   ```

#### `undeploy-cli-local.ps1`
FileFlux CLI를 제거합니다.

**사용법:**
```powershell
.\scripts\undeploy-cli-local.ps1
```

**옵션:**
- `-InstallPath`: 제거할 설치 경로
- `-RemoveFromPath`: PATH에서 제거 (기본값: true)

### 📝 테스트 스크립트

#### `run-pdf-tests.ps1`
PDF 처리 테스트를 실행합니다.

**사용법:**
```powershell
# 전체 테스트 (빌드 + 실행)
.\scripts\run-pdf-tests.ps1

# 클린 빌드 후 테스트
.\scripts\run-pdf-tests.ps1 -CleanFirst

# 기존 빌드로 테스트만 실행
.\scripts\run-pdf-tests.ps1 -SkipBuild
```

#### `test-markdown.ps1`
마크다운 처리 테스트를 실행합니다.

**사용법:**
```powershell
# 테스트만 실행
.\scripts\test-markdown.ps1 -TestOnly

# 전체 빌드 후 테스트
.\scripts\test-markdown.ps1
```

## 일반적인 워크플로우

### 1. 개발 환경 설정
```powershell
# 저장소 클론
git clone https://github.com/iyulab/FileFlux.git
cd FileFlux

# CLI 로컬 배포
.\scripts\deploy-cli-local.ps1

# 터미널 재시작 후 확인
fileflux --version
```

### 2. 개발 사이클
```powershell
# 코드 수정 후 재배포
.\scripts\deploy-cli-local.ps1

# 또는 Debug 빌드로 테스트
.\scripts\deploy-cli-local.ps1 -Configuration Debug
```

### 3. 테스트
```powershell
# PDF 테스트
.\scripts\run-pdf-tests.ps1

# 마크다운 테스트
.\scripts\test-markdown.ps1 -TestOnly
```

### 4. 정리
```powershell
# CLI 제거
.\scripts\undeploy-cli-local.ps1
```

## 환경 변수

### OpenAI API (Vision 기능용)
```powershell
# PowerShell
$env:OPENAI_API_KEY = "sk-..."
$env:OPENAI_MODEL = "gpt-5-nano"

# 영구 설정 (Windows)
[System.Environment]::SetEnvironmentVariable('OPENAI_API_KEY', 'sk-...', 'User')
[System.Environment]::SetEnvironmentVariable('OPENAI_MODEL', 'gpt-5-nano', 'User')
```

### FileFlux CLI 설정 (권장)
```powershell
# 영구 설정 저장 (config 파일)
fileflux set OPENAI_API_KEY sk-...
fileflux set OPENAI_MODEL gpt-5-nano
fileflux set MODEL_PROVIDER openai

# 설정 확인
fileflux get
fileflux status
```

## 문제 해결

### "실행 정책" 오류
```powershell
# PowerShell 실행 정책 변경
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### "fileflux 명령을 찾을 수 없음"
1. 터미널 재시작
2. PATH 확인:
   ```powershell
   $env:Path -split ';' | Select-String 'FileFlux'
   ```
3. 수동 추가 필요시:
   ```powershell
   $env:Path += ";$env:LOCALAPPDATA\FileFlux"
   ```

### 권한 오류
- 관리자 권한으로 PowerShell 실행
- 또는 사용자 디렉토리에 설치

### .NET SDK 없음
- [.NET 10 SDK 다운로드](https://dotnet.microsoft.com/download/dotnet/10.0)
- 설치 후 터미널 재시작

## 추가 정보

- **CLI 사용법**: [docs/CLI_VISION.md](../docs/CLI_VISION.md)
- **아키텍처**: [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)
- **튜토리얼**: [docs/TUTORIAL.md](../docs/TUTORIAL.md)
- **이슈 보고**: [GitHub Issues](https://github.com/iyulab/FileFlux/issues)

## 라이선스

MIT License - 자세한 내용은 [LICENSE](../LICENSE) 참조
