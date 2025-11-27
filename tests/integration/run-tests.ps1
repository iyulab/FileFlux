# Load environment variables from .env.local
$envFile = "D:\data\FileFlux\.env.local"
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process')
    }
}

Write-Host "Environment loaded. OPENAI_MODEL: $env:OPENAI_MODEL" -ForegroundColor Green

# Set working directory
Set-Location "D:\data\FileFlux"

# Test 1: Enrich Command
Write-Host "`n=== TEST 1: Enrich Command ===" -ForegroundColor Cyan
$sw = [System.Diagnostics.Stopwatch]::StartNew()
dotnet run --project src/FileFlux.CLI/FileFlux.CLI.csproj -- enrich tests/integration/test-chunks.json --output tests/integration/enriched-chunks.json -v
$sw.Stop()
Write-Host "Enrich time: $($sw.ElapsedMilliseconds)ms" -ForegroundColor Yellow

# Test 2: QA Command
Write-Host "`n=== TEST 2: QA Command ===" -ForegroundColor Cyan
$sw.Restart()
dotnet run --project src/FileFlux.CLI/FileFlux.CLI.csproj -- qa tests/integration/test-chunks.json --output tests/integration/qa-pairs.json --pairs-per-chunk 2 -v
$sw.Stop()
Write-Host "QA generation time: $($sw.ElapsedMilliseconds)ms" -ForegroundColor Yellow

# Test 3: Evaluate Command
Write-Host "`n=== TEST 3: Evaluate Command ===" -ForegroundColor Cyan
$sw.Restart()
dotnet run --project src/FileFlux.CLI/FileFlux.CLI.csproj -- evaluate tests/integration/qa-pairs.json --output tests/integration/evaluated-qa.json --threshold 0.5 -v
$sw.Stop()
Write-Host "Evaluate time: $($sw.ElapsedMilliseconds)ms" -ForegroundColor Yellow

Write-Host "`n=== All tests completed ===" -ForegroundColor Green
