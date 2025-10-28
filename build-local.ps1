#!/usr/bin/env pwsh
# FileFlux Local Build Script
# Builds and publishes FileFlux package to local NuGet feed

param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "D:\data\FileFlux\nupkg",
    [switch]$CleanFirst
)

$ErrorActionPreference = "Stop"

Write-Host "===================================" -ForegroundColor Cyan
Write-Host "FileFlux Local Build Script" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

# Change to FileFlux directory
Set-Location "D:\data\FileFlux"

# Clean if requested
if ($CleanFirst) {
    Write-Host "Cleaning previous build artifacts..." -ForegroundColor Yellow
    dotnet clean -c $Configuration
    if (Test-Path $OutputPath) {
        Remove-Item "$OutputPath\*.nupkg" -Force -ErrorAction SilentlyContinue
    }
}

# Restore dependencies
Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore src/FileFlux.sln

# Build
Write-Host "Building FileFlux..." -ForegroundColor Yellow
dotnet build src/FileFlux.sln -c $Configuration --no-restore

# Run tests
Write-Host "Running tests..." -ForegroundColor Yellow
dotnet test src/FileFlux.sln -c $Configuration --no-build --verbosity minimal

# Pack
Write-Host "Creating NuGet package..." -ForegroundColor Yellow
dotnet pack src/FileFlux/FileFlux.csproj -c $Configuration --no-build -o $OutputPath

# List created packages
Write-Host ""
Write-Host "===================================" -ForegroundColor Green
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "===================================" -ForegroundColor Green
Write-Host ""
Write-Host "Created packages:" -ForegroundColor Green
Get-ChildItem "$OutputPath\FileFlux.*.nupkg" | Sort-Object LastWriteTime -Descending | Select-Object -First 5 | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Cyan
}

Write-Host ""
Write-Host "To use this package in FilerBasis:" -ForegroundColor Yellow
Write-Host "  1. Update package reference in Filer.Basis.FluxIndex.csproj" -ForegroundColor Gray
Write-Host "  2. Run: dotnet restore" -ForegroundColor Gray
Write-Host ""
