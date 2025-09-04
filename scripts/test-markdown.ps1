# FileFlux Markdown Test Environment Manager
# 환경 초기화 및 테스트 실행을 하나의 스크립트로 통합

param(
    [switch]$CleanFirst = $false,
    [switch]$CleanOnly = $false,
    [switch]$TestOnly = $false,
    [switch]$Verbose = $false,
    [string]$Configuration = "Debug",
    [string]$TestFile = "test.md"
)

$projectRoot = "D:\data\FileFlux"
$testDir = "$projectRoot\test\test-markdown"
$testProject = "$projectRoot\src\FileFlux.Tests\FileFlux.Tests.csproj"

Write-Host "📝 FileFlux Markdown Test Environment Manager" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Gray
Write-Host "Target Directory: $testDir" -ForegroundColor Yellow
Write-Host "Test File: $TestFile" -ForegroundColor Yellow

# 환경 정리 함수
function Clean-TestEnvironment {
    Write-Host "`n🧹 Cleaning test environment..." -ForegroundColor Yellow
    
    if (!(Test-Path $testDir)) {
        Write-Host "❌ Test directory not found: $testDir" -ForegroundColor Red
        return $false
    }
    
    # 보존할 파일 확인
    $preserveFiles = Get-ChildItem -Path $testDir -Filter "*.md" | Select-Object -ExpandProperty Name
    
    if ($preserveFiles.Count -eq 0) {
        Write-Host "⚠️  No .md files found to preserve!" -ForegroundColor Yellow
        $confirm = Read-Host "Continue with cleanup anyway? (y/N)"
        if ($confirm -ne "y" -and $confirm -ne "Y") {
            Write-Host "❌ Cleanup cancelled" -ForegroundColor Red
            return $false
        }
    }
    else {
        Write-Host "📄 Markdown files to preserve:" -ForegroundColor Green
        $preserveFiles | ForEach-Object { Write-Host "  • $_" -ForegroundColor White }
    }
    
    # 제거할 디렉터리
    $dirsToRemove = @(
        "chunking-results",
        "extraction-results", 
        "parsing-results",
        "logs"
    )
    
    # 제거할 파일 (Markdown 파일 제외)
    $filesToRemove = Get-ChildItem -Path $testDir -File | Where-Object { $_.Extension -ne ".md" }
    
    Write-Host "`n🗑️  Removing directories and files..." -ForegroundColor Gray
    
    # 디렉터리 제거
    foreach ($dir in $dirsToRemove) {
        $dirPath = Join-Path $testDir $dir
        if (Test-Path $dirPath) {
            Write-Host "  Removing directory: $dir" -ForegroundColor Gray
            Remove-Item -Path $dirPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    
    # 비-Markdown 파일 제거
    foreach ($file in $filesToRemove) {
        Write-Host "  Removing file: $($file.Name)" -ForegroundColor Gray
        Remove-Item -Path $file.FullName -Force -ErrorAction SilentlyContinue
    }
    
    Write-Host "`n✅ Cleanup completed!" -ForegroundColor Green
    Write-Host "📁 Clean test environment ready at: $testDir" -ForegroundColor Cyan
    
    # 최종 파일 목록 표시
    Write-Host "`n📋 Remaining files:" -ForegroundColor Yellow
    Get-ChildItem -Path $testDir -ErrorAction SilentlyContinue | ForEach-Object { 
        Write-Host "  • $($_.Name)" -ForegroundColor White 
    }
    
    return $true
}

# 테스트 실행 함수
function Run-MarkdownTests {
    Write-Host "`n🔨 Building solution..." -ForegroundColor Yellow
    Push-Location $projectRoot
    try {
        dotnet build "src\FileFlux.sln" -c $Configuration --nologo
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Build failed, aborting test run" -ForegroundColor Red
            return $false
        }
        Write-Host "✅ Build successful" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
    
    # Markdown 파일 존재 확인
    $markdownPath = Join-Path $testDir $TestFile
    if (!(Test-Path $markdownPath)) {
        Write-Host "❌ Test markdown file not found: $markdownPath" -ForegroundColor Red
        Write-Host "💡 Available .md files:" -ForegroundColor Yellow
        $availableFiles = Get-ChildItem -Path $testDir -Filter "*.md" -ErrorAction SilentlyContinue
        if ($availableFiles.Count -gt 0) {
            $availableFiles | ForEach-Object { Write-Host "  • $($_.Name)" -ForegroundColor White }
            Write-Host "💡 Use -TestFile parameter to specify a different file" -ForegroundColor Yellow
        }
        else {
            Write-Host "  No .md files found in test directory" -ForegroundColor Gray
        }
        return $false
    }
    
    Write-Host "`n📝 Testing markdown file: $TestFile" -ForegroundColor Cyan
    Write-Host "📁 File path: $markdownPath" -ForegroundColor Gray
    
    # 파일 정보 표시
    $fileInfo = Get-Item $markdownPath
    Write-Host "📊 File size: $($fileInfo.Length) bytes" -ForegroundColor Gray
    Write-Host "📅 Last modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
    
    Write-Host "`n🧪 Running Markdown Processing Tests..." -ForegroundColor Yellow
    
    # Markdown 처리 통합 테스트 실행
    $testFilter = "FullyQualifiedName~MarkdownProcessingIntegrationTests"
    $verbosityLevel = if ($Verbose) { "normal" } else { "minimal" }
    
    Push-Location $projectRoot
    try {
        dotnet test $testProject `
            --filter $testFilter `
            --configuration $Configuration `
            --verbosity $verbosityLevel `
            --nologo `
            --no-build `
            --logger "console;verbosity=detailed"
        
        $testExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    
    # 결과 분석
    if ($testExitCode -eq 0) {
        Write-Host "✅ Markdown Processing Tests: PASSED" -ForegroundColor Green
    }
    else {
        Write-Host "❌ Markdown Processing Tests: FAILED" -ForegroundColor Red
        return $false
    }
    
    # 결과 파일 위치 표시
    $resultsDir = Join-Path $testDir "chunking-results"
    if (Test-Path $resultsDir) {
        Write-Host "`n📁 Test Results Location:" -ForegroundColor Yellow
        Write-Host "  $resultsDir" -ForegroundColor White
        
        # 최신 결과 표시
        $latestResults = Get-ChildItem -Path $resultsDir -Directory -ErrorAction SilentlyContinue | 
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latestResults) {
            Write-Host "`n📄 Latest Test Run:" -ForegroundColor Yellow
            Write-Host "  $($latestResults.FullName)" -ForegroundColor White
            
            # 통계 파일 확인
            $statsFiles = Get-ChildItem -Path $latestResults.FullName -Filter "*statistics.json" -ErrorAction SilentlyContinue
            if ($statsFiles) {
                Write-Host "`n📊 Statistics File:" -ForegroundColor Yellow
                Write-Host "  $($statsFiles.FullName)" -ForegroundColor White
                
                # 간단한 통계 표시
                try {
                    $stats = Get-Content $statsFiles.FullName | ConvertFrom-Json
                    Write-Host "`n📈 Quick Stats:" -ForegroundColor Cyan
                    Write-Host "  File: $($stats.FileName)" -ForegroundColor White
                    Write-Host "  Total Chunks: $($stats.TotalChunks)" -ForegroundColor White
                    Write-Host "  Average Size: $([math]::Round($stats.AverageChunkSize, 1)) chars" -ForegroundColor White
                    Write-Host "  Size Range: $($stats.MinChunkSize) ~ $($stats.MaxChunkSize) chars" -ForegroundColor White
                    Write-Host "  Strategy: $($stats.ChunkingStrategy)" -ForegroundColor White
                    Write-Host "  Max Size Limit: $($stats.ConfiguredMaxChunkSize) chars" -ForegroundColor White
                    Write-Host "  Overlap: $($stats.OverlapSize) chars" -ForegroundColor White
                    
                    # 크기 분포 표시
                    if ($stats.ChunkSizeDistribution) {
                        Write-Host "  Size Distribution:" -ForegroundColor Gray
                        $stats.ChunkSizeDistribution.PSObject.Properties | ForEach-Object {
                            Write-Host "    $($_.Name): $($_.Value) chunks" -ForegroundColor Gray
                        }
                    }
                }
                catch {
                    Write-Host "  (Statistics file could not be parsed)" -ForegroundColor Gray
                }
            }
        }
    }
    
    return $true
}

# 메인 실행 로직
Write-Host "`n🎯 Execution Plan:" -ForegroundColor Cyan

if ($CleanOnly) {
    Write-Host "  • Clean environment only" -ForegroundColor White
}
elseif ($TestOnly) {
    Write-Host "  • Run tests only" -ForegroundColor White
}
elseif ($CleanFirst) {
    Write-Host "  • Clean environment first, then run tests" -ForegroundColor White
}
else {
    Write-Host "  • Run tests with current environment" -ForegroundColor White
}

$success = $true

# 환경 정리 실행
if ($CleanOnly -or $CleanFirst) {
    $success = Clean-TestEnvironment
    if (!$success) {
        Write-Host "`n💥 Environment cleanup failed" -ForegroundColor Red
        exit 1
    }
}

# CleanOnly 모드면 여기서 종료
if ($CleanOnly) {
    Write-Host "`n🎉 Environment cleanup completed successfully!" -ForegroundColor Green
    exit 0
}

# 테스트 실행
if (!$TestOnly -or $success) {
    $success = Run-MarkdownTests
}

# 최종 결과
Write-Host "`n" + "=" * 60 -ForegroundColor Gray

if ($success) {
    Write-Host "🎉 All operations completed successfully!" -ForegroundColor Green
    Write-Host "💡 Use the following options for different scenarios:" -ForegroundColor Cyan
    Write-Host "  -CleanFirst    : Clean environment before testing" -ForegroundColor White
    Write-Host "  -CleanOnly     : Only clean environment" -ForegroundColor White
    Write-Host "  -TestOnly      : Only run tests" -ForegroundColor White
    Write-Host "  -TestFile name : Specify different .md file" -ForegroundColor White
    Write-Host "  -Verbose       : Detailed test output" -ForegroundColor White
}
else {
    Write-Host "💥 Some operations failed - check output above" -ForegroundColor Red
    exit 1
}

exit 0