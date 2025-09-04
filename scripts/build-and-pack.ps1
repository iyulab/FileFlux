#!/usr/bin/env pwsh

# FileFlux NuGet Package Build and Publish Script
# 사용법: ./build-and-pack.ps1 [-Version "1.0.0"] [-PublishToNuGet] [-ApiKey "your-api-key"]

param(
    [string]$Version = "",
    [switch]$PublishToNuGet,
    [string]$ApiKey = "",
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [switch]$PackOnly,
    [switch]$CleanFirst,
    [switch]$Help
)

# 도움말 표시
if ($Help) {
    Write-Host @"
FileFlux NuGet Package Build and Publish Script

사용법:
  ./build-and-pack.ps1 [-Version "1.0.0"] [-PublishToNuGet] [-ApiKey "key"] [옵션]

매개변수:
  -Version        패키지 버전 (예: "1.0.0", 미지정시 자동 증가)
  -PublishToNuGet NuGet.org에 발행 (기본: false)  
  -ApiKey         NuGet API 키
  -Source         NuGet 소스 (기본: nuget.org)
  -PackOnly       빌드 없이 패킹만 수행
  -CleanFirst     빌드 전 clean 수행
  -Help           이 도움말 표시

예제:
  ./build-and-pack.ps1 -Version "1.0.0"
  ./build-and-pack.ps1 -Version "1.0.1" -PublishToNuGet -ApiKey "your-key"
  ./build-and-pack.ps1 -PackOnly -CleanFirst
"@
    exit 0
}

# 색상 함수
function Write-Success { param($Message) Write-Host "✅ $Message" -ForegroundColor Green }
function Write-Info { param($Message) Write-Host "ℹ️  $Message" -ForegroundColor Cyan }
function Write-Warning { param($Message) Write-Host "⚠️  $Message" -ForegroundColor Yellow }
function Write-Error { param($Message) Write-Host "❌ $Message" -ForegroundColor Red }

# 스크립트 시작
Write-Host "🚀 FileFlux NuGet 패키지 빌드 스크립트" -ForegroundColor Magenta
Write-Host "=" * 60

# 현재 디렉토리 확인 (스크립트가 scripts/ 폴더에 있으므로 상위 디렉토리가 루트)
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootPath = Split-Path -Parent $ScriptPath
$SrcPath = Join-Path $RootPath "src"
$OutputPath = Join-Path $RootPath "nupkg"

Write-Info "프로젝트 루트: $RootPath"
Write-Info "소스 경로: $SrcPath"
Write-Info "출력 경로: $OutputPath"

if (!(Test-Path $SrcPath)) {
    Write-Error "소스 디렉토리를 찾을 수 없습니다: $SrcPath"
    exit 1
}

# 출력 디렉토리 생성
if (!(Test-Path $OutputPath)) {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    Write-Info "출력 디렉토리 생성: $OutputPath"
}

# 버전 결정
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "1.0.yyMMdd.HHmm"
    Write-Info "자동 버전 생성: $Version"
} else {
    Write-Info "지정된 버전: $Version"
}

# 패키징할 프로젝트 목록 (메인 패키지만)
$Projects = @(
    @{
        Name = "FileFlux"
        Path = "FileFlux.Infrastructure\FileFlux.Infrastructure.csproj"
        Description = "Complete FileFlux SDK for RAG-optimized document processing"
    }
)

try {
    # Clean 수행 (선택적)
    if ($CleanFirst) {
        Write-Info "Clean 수행 중..."
        & dotnet clean $SrcPath --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Clean 실패"
            exit 1
        }
        Write-Success "Clean 완료"
    }

    # 빌드 수행 (PackOnly가 아닌 경우)
    if (-not $PackOnly) {
        Write-Info "솔루션 빌드 중..."
        & dotnet build $SrcPath --configuration Release --verbosity minimal
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "빌드 실패"
            exit 1
        }
        Write-Success "빌드 완료"
    }

    # 각 프로젝트 패키징
    $PackagedFiles = @()
    
    foreach ($Project in $Projects) {
        Write-Info "패키징 중: $($Project.Name)"
        
        $ProjectPath = Join-Path $SrcPath $Project.Path
        
        if (!(Test-Path $ProjectPath)) {
            Write-Warning "프로젝트 파일을 찾을 수 없습니다: $ProjectPath"
            continue
        }

        # 패키지 생성
        $PackArgs = @(
            "pack"
            $ProjectPath
            "--configuration", "Release"
            "--output", $OutputPath
            "--verbosity", "minimal"
            "-p:PackageVersion=$Version"
            "-p:AssemblyVersion=$Version"
            "-p:FileVersion=$Version"
        )

        & dotnet $PackArgs

        if ($LASTEXITCODE -ne 0) {
            Write-Error "$($Project.Name) 패키징 실패"
            continue
        }

        $PackageFile = Join-Path $OutputPath "$($Project.Name).$Version.nupkg"
        if (Test-Path $PackageFile) {
            $PackagedFiles += $PackageFile
            Write-Success "$($Project.Name) 패키징 완료"
        }
    }

    # 패키징 결과 요약
    Write-Host ""
    Write-Host "📦 패키징 결과" -ForegroundColor Magenta
    Write-Host "-" * 40

    if ($PackagedFiles.Count -eq 0) {
        Write-Error "생성된 패키지가 없습니다"
        exit 1
    }

    foreach ($PackageFile in $PackagedFiles) {
        $FileInfo = Get-Item $PackageFile
        Write-Success "✓ $($FileInfo.Name) ($([math]::Round($FileInfo.Length / 1KB, 2)) KB)"
    }

    Write-Info "총 $($PackagedFiles.Count)개 패키지 생성됨"
    Write-Info "출력 경로: $OutputPath"

    # NuGet 발행 (선택적)
    if ($PublishToNuGet) {
        Write-Host ""
        Write-Host "🚀 NuGet 발행" -ForegroundColor Magenta
        Write-Host "-" * 40

        if ([string]::IsNullOrWhiteSpace($ApiKey)) {
            Write-Error "NuGet 발행을 위해 API 키가 필요합니다. -ApiKey 매개변수를 사용하세요"
            exit 1
        }

        foreach ($PackageFile in $PackagedFiles) {
            Write-Info "발행 중: $(Split-Path -Leaf $PackageFile)"
            
            & dotnet nuget push $PackageFile --api-key $ApiKey --source $Source --skip-duplicate
            
            if ($LASTEXITCODE -eq 0) {
                Write-Success "$(Split-Path -Leaf $PackageFile) 발행 완료"
            } else {
                Write-Error "$(Split-Path -Leaf $PackageFile) 발행 실패"
            }
        }
    }

    # 완료 메시지
    Write-Host ""
    Write-Host "🎉 FileFlux 패키지 빌드 완료!" -ForegroundColor Green
    Write-Host "버전: $Version" -ForegroundColor Cyan
    Write-Host "패키지 수: $($PackagedFiles.Count)" -ForegroundColor Cyan
    
    if ($PublishToNuGet) {
        Write-Host "NuGet 발행: 완료" -ForegroundColor Cyan
        Write-Host "설치 명령: dotnet add package FileFlux.Infrastructure --version $Version" -ForegroundColor Yellow
    } else {
        Write-Host "로컬 테스트: dotnet add package FileFlux.Infrastructure --source $OutputPath --version $Version" -ForegroundColor Yellow
    }

} catch {
    Write-Error "스크립트 실행 중 오류 발생: $($_.Exception.Message)"
    exit 1
}