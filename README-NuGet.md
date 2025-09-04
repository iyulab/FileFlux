# FileFlux NuGet 패키지 배포 가이드

FileFlux를 NuGet 패키지로 빌드하고 배포하기 위한 완전한 가이드입니다.

## 🚀 빠른 시작

### 1. 로컬 패키지 빌드
```powershell
# 기본 빌드 및 패키징 (자동 버전)
./scripts/build-and-pack.ps1

# 특정 버전으로 빌드
./scripts/build-and-pack.ps1 -Version "1.0.0"

# Clean 후 빌드
./scripts/build-and-pack.ps1 -Version "1.0.0" -CleanFirst
```

### 2. NuGet.org에 배포
```powershell
# API 키와 함께 배포
./scripts/build-and-pack.ps1 -Version "1.0.0" -PublishToNuGet -ApiKey "your-nuget-api-key"

# 사설 NuGet 서버에 배포
./scripts/build-and-pack.ps1 -Version "1.0.0" -PublishToNuGet -ApiKey "your-api-key" -Source "https://your-nuget-server/v3/index.json"
```

## 📦 단일 패키지 전략

**FileFlux**는 이제 **단일 메인 패키지**로 배포됩니다:

### FileFlux (메인 패키지)
- **패키지 ID**: `FileFlux`
- **설명**: RAG 최적화된 완전한 문서 처리 SDK
- **포함 내용**: Domain, Core, Infrastructure 모든 기능
- **의존성**: 외부 라이브러리들만 (DocumentFormat.OpenXml, PdfPig, etc.)
- **사용자 경험**: 한 번의 설치로 모든 기능 사용 가능

### 내부 구조 (사용자에게 노출되지 않음)
- **FileFlux.Domain**: 내부 도메인 모델 (`IsPackable=false`)
- **FileFlux.Core**: 내부 추상화 (`IsPackable=false`)
- **FileFlux.Infrastructure**: 메인 패키지로 빌드됨

## 🔧 스크립트 매개변수

### 기본 매개변수
- `-Version`: 패키지 버전 (예: "1.0.0")
- `-PublishToNuGet`: NuGet에 발행 여부
- `-ApiKey`: NuGet API 키
- `-Source`: NuGet 소스 URL (기본: nuget.org)

### 옵션 매개변수
- `-PackOnly`: 빌드 없이 패킹만 수행
- `-CleanFirst`: 빌드 전 clean 수행
- `-Help`: 도움말 표시

## 📋 사전 준비사항

### 1. NuGet API 키 설정
```powershell
# 환경변수로 설정 (권장)
$env:NUGET_API_KEY = "your-api-key-here"

# 또는 매개변수로 직접 전달
./build-and-pack.ps1 -ApiKey "your-api-key-here"
```

### 2. 프로젝트 메타데이터 업데이트
각 프로젝트의 `.csproj` 파일에서 다음 항목들을 업데이트하세요:

- `<Authors>`: 작성자명
- `<Company>`: 회사명  
- `<RepositoryUrl>`: GitHub 저장소 URL
- `<PackageProjectUrl>`: 프로젝트 웹사이트 URL
- `<PackageReleaseNotes>`: 릴리즈 노트

## 🎯 사용 예제

### 로컬 개발 및 테스트
```powershell
# 개발 빌드 (타임스탬프 버전)
./scripts/build-and-pack.ps1

# 생성된 패키지를 로컬에서 테스트
dotnet add package FileFlux --source ./nupkg --version 1.0.240904.1030
```

### 운영 배포
```powershell
# 안정적인 버전으로 빌드 후 배포
./scripts/build-and-pack.ps1 -Version "1.0.0" -CleanFirst -PublishToNuGet -ApiKey $env:NUGET_API_KEY
```

### CI/CD 파이프라인
```powershell
# GitHub Actions 또는 Azure DevOps에서 사용
./scripts/build-and-pack.ps1 -Version $env:BUILD_VERSION -PublishToNuGet -ApiKey $env:NUGET_API_KEY
```

## 📊 패키지 정보

### FileFlux (메인 패키지)
```xml
<PackageId>FileFlux</PackageId>
<Description>Complete document processing SDK optimized for RAG systems</Description>
<PackageTags>rag;document;processing;chunking;ai;llm;pdf;docx;excel;powerpoint;markdown;complete;sdk</PackageTags>
```

**지원 문서 형식:**
- PDF (.pdf)
- Microsoft Word (.docx)
- Microsoft Excel (.xlsx, .xls)
- Microsoft PowerPoint (.pptx)
- Markdown (.md)
- HTML (.html, .htm)
- Plain Text (.txt)
- CSV (.csv)
- JSON (.json)

## 🔍 버전 관리

### 자동 버전 생성
스크립트는 버전이 지정되지 않으면 다음 형식으로 자동 생성합니다:
```
1.0.YYMMDD.HHMM
예: 1.0.240904.1530
```

### 의미적 버전 관리 (권장)
운영 배포 시에는 [Semantic Versioning](https://semver.org/)을 따르세요:
```
MAJOR.MINOR.PATCH
예: 1.0.0, 1.1.0, 1.1.1
```

## ⚠️ 주의사항

### 1. API 키 보안
- API 키를 스크립트에 하드코딩하지 마세요
- 환경변수나 보안 저장소를 사용하세요
- CI/CD 파이프라인에서는 보안 변수를 사용하세요

### 2. 버전 충돌
- NuGet에 이미 존재하는 버전은 재업로드할 수 없습니다
- 테스트용 버전은 prerelease 태그를 사용하세요 (예: 1.0.0-beta)

### 3. 의존성 관리
- 외부 패키지 버전이 최신인지 확인하세요
- 호환성 문제가 없는지 테스트하세요

## 📞 지원

문제가 발생하면 다음을 확인하세요:

1. **빌드 오류**: `dotnet build src/ --verbosity normal`로 상세 로그 확인
2. **패키징 오류**: 프로젝트 메타데이터 확인
3. **업로드 오류**: API 키와 권한 확인

## 🎉 배포 완료 후

패키지가 성공적으로 배포되면:

1. **NuGet.org에서 확인**: 패키지 페이지에서 메타데이터 확인
2. **설치 테스트**: 새 프로젝트에서 패키지 설치 테스트
3. **문서 업데이트**: README와 문서에 설치 방법 추가

### 설치 명령
```bash
# 메인 패키지 설치
dotnet add package FileFlux

# 특정 버전 설치  
dotnet add package FileFlux --version 1.0.0
```