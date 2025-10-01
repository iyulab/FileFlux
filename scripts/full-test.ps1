#!/usr/bin/env pwsh
# Full test suite including integration tests and benchmarks

Write-Host "🔨 Building and Testing FileFlux (Full Suite)..." -ForegroundColor Cyan

# Navigate to src directory
Push-Location "$PSScriptRoot\..\src"

try {
    # Build solution
    Write-Host "Building solution..." -ForegroundColor Yellow
    dotnet build --no-incremental

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed" -ForegroundColor Red
        exit 1
    }

    Write-Host "✅ Build successful!`n" -ForegroundColor Green

    # Run all tests
    Write-Host "🧪 Running all tests (including Integration/Benchmarks)..." -ForegroundColor Cyan
    Write-Host "⚠️  This may take 10-15 minutes..." -ForegroundColor Yellow

    dotnet test --no-build --verbosity normal

    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n✅ All tests passed!" -ForegroundColor Green
    } else {
        Write-Host "`n❌ Some tests failed" -ForegroundColor Red
        exit 1
    }
}
finally {
    Pop-Location
}
