#!/usr/bin/env pwsh
# Quick build verification script - excludes slow integration tests

Write-Host "🔨 Building FileFlux Solution..." -ForegroundColor Cyan

# Navigate to src directory
Push-Location "$PSScriptRoot\..\src"

try {
    # Build solution
    dotnet build --no-incremental

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Build successful!" -ForegroundColor Green

        # Optional: Run fast unit tests only
        Write-Host "`n🧪 Running unit tests (excluding Integration/Benchmarks)..." -ForegroundColor Cyan
        dotnet test --no-build --filter "FullyQualifiedName!~Integration&FullyQualifiedName!~Benchmark&FullyQualifiedName!~Performance"

        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Unit tests passed!" -ForegroundColor Green
        } else {
            Write-Host "❌ Some unit tests failed" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "❌ Build failed" -ForegroundColor Red
        exit 1
    }
}
finally {
    Pop-Location
}
