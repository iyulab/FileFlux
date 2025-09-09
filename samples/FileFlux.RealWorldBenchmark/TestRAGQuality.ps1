# RAG Quality Benchmark Test Script
# This script runs comprehensive RAG quality tests using real OpenAI APIs

param(
    [string]$TestFile = "",
    [string]$Strategy = "Smart",
    [switch]$AllStrategies,
    [switch]$QuickTest
)

Write-Host "🚀 FileFlux RAG Quality Benchmark" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan

# Navigate to project directory
Set-Location "D:\data\FileFlux\samples\FileFlux.RealWorldBenchmark"

# Check for .env.local file with API key
$envFile = ".env.local"
if (-not (Test-Path $envFile)) {
    Write-Host "❌ Error: .env.local file not found!" -ForegroundColor Red
    Write-Host "Please create .env.local with your OpenAI API key:" -ForegroundColor Yellow
    Write-Host "OPENAI_API_KEY=your-api-key-here" -ForegroundColor Gray
    exit 1
}

# Load environment variables
$env:DOTNET_ENVIRONMENT = "Development"
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($matches[1], $matches[2])
    }
}

if (-not $env:OPENAI_API_KEY) {
    Write-Host "❌ Error: OPENAI_API_KEY not found in .env.local!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ OpenAI API Key configured" -ForegroundColor Green

# Build the project first
Write-Host "`n📦 Building RealWorldBenchmark project..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Run the benchmark based on parameters
Write-Host "`n🔬 Running RAG Quality Benchmark..." -ForegroundColor Cyan

if ($AllStrategies) {
    Write-Host "Testing ALL strategies..." -ForegroundColor Gray
    dotnet run -c Release -- "benchmark-all"
}
elseif ($QuickTest) {
    Write-Host "Running quick test with $Strategy strategy..." -ForegroundColor Gray
    dotnet run -c Release -- "quick-test" $Strategy
}
else {
    Write-Host "Testing $Strategy strategy..." -ForegroundColor Gray
    dotnet run -c Release -- "benchmark" $Strategy
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n✅ Benchmark completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n❌ Benchmark failed with exit code: $LASTEXITCODE" -ForegroundColor Red
}

# Display summary
Write-Host "`n📊 Summary:" -ForegroundColor Cyan
Write-Host "- Check the output above for detailed metrics" -ForegroundColor Gray
Write-Host "- Key metrics: Completeness, F1 Score, Recall, Precision" -ForegroundColor Gray
Write-Host "- Target: ≥70% Completeness, ≥85% Recall" -ForegroundColor Gray