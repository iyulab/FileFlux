# FileFlux PDF Processing Test Runner
# Runs PDF processing integration tests and RAG suitability validation

param(
    [switch]$CleanFirst = $false,
    [switch]$Verbose = $false,
    [string]$Configuration = "Debug"
)

$projectRoot = "D:\data\FileFlux"
$testProject = "$projectRoot\src\FileFlux.Tests\FileFlux.Tests.csproj"
$cleanupScript = "$projectRoot\scripts\cleanup-test-env.ps1"

Write-Host "🚀 FileFlux PDF Processing Test Runner" -ForegroundColor Cyan
Write-Host "=" * 50 -ForegroundColor Gray

# Cleanup first if requested
if ($CleanFirst) {
    Write-Host "`n🧹 Cleaning test environment first..." -ForegroundColor Yellow
    if (Test-Path $cleanupScript) {
        & PowerShell -File $cleanupScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Cleanup failed, aborting test run" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "⚠️  Cleanup script not found: $cleanupScript" -ForegroundColor Yellow
    }
}

# Build solution first
Write-Host "`n🔨 Building solution..." -ForegroundColor Yellow
Push-Location $projectRoot
try {
    dotnet build "src\FileFlux.sln" -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed, aborting test run" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ Build successful" -ForegroundColor Green
} finally {
    Pop-Location
}

# Run PDF processing tests
Write-Host "`n🧪 Running PDF Processing Integration Tests..." -ForegroundColor Yellow

$testFilter = "FullyQualifiedName~PdfProcessingIntegrationTests"
$verbosityLevel = if ($Verbose) { "normal" } else { "minimal" }

Push-Location $projectRoot
try {
    dotnet test $testProject `
        --filter $testFilter `
        --configuration $Configuration `
        --verbosity $verbosityLevel `
        --nologo `
        --no-build
    
    $testExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

# Run RAG Suitability Tests
Write-Host "`n🎯 Running RAG Suitability Tests..." -ForegroundColor Yellow

$ragTestFilter = "FullyQualifiedName~RagSuitabilityTests"

Push-Location $projectRoot
try {
    dotnet test $testProject `
        --filter $ragTestFilter `
        --configuration $Configuration `
        --verbosity $verbosityLevel `
        --nologo `
        --no-build
    
    $ragTestExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

# Results summary
Write-Host "`n" + "=" * 50 -ForegroundColor Gray
Write-Host "📊 Test Results Summary" -ForegroundColor Cyan

if ($testExitCode -eq 0) {
    Write-Host "✅ PDF Processing Tests: PASSED" -ForegroundColor Green
} else {
    Write-Host "❌ PDF Processing Tests: FAILED" -ForegroundColor Red
}

if ($ragTestExitCode -eq 0) {
    Write-Host "✅ RAG Suitability Tests: PASSED" -ForegroundColor Green
} else {
    Write-Host "❌ RAG Suitability Tests: FAILED" -ForegroundColor Red
}

# Show test results location
$testResultsDir = "D:\data\FileFlux\test\test-a\chunking-results"
if (Test-Path $testResultsDir) {
    Write-Host "`n📁 Test Results Location:" -ForegroundColor Yellow
    Write-Host "  $testResultsDir" -ForegroundColor White
    
    # Show latest results
    $latestResults = Get-ChildItem -Path $testResultsDir -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latestResults) {
        Write-Host "`n📄 Latest Test Run:" -ForegroundColor Yellow
        Write-Host "  $($latestResults.FullName)" -ForegroundColor White
        
        # Show key files
        $keyFiles = Get-ChildItem -Path $latestResults.FullName -Filter "*statistics.json" -ErrorAction SilentlyContinue
        if ($keyFiles) {
            Write-Host "`n📊 Statistics File:" -ForegroundColor Yellow
            Write-Host "  $($keyFiles.FullName)" -ForegroundColor White
        }
    }
}

Write-Host "`n" + "=" * 50 -ForegroundColor Gray

# Final exit code
$finalExitCode = if ($testExitCode -eq 0 -and $ragTestExitCode -eq 0) { 0 } else { 1 }

if ($finalExitCode -eq 0) {
    Write-Host "🎉 All tests completed successfully!" -ForegroundColor Green
} else {
    Write-Host "💥 Some tests failed - check output above" -ForegroundColor Red
}

exit $finalExitCode