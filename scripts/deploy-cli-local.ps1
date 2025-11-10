#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploy FileFlux CLI to local user directory

.DESCRIPTION
    This script builds and deploys the FileFlux CLI tool to a local directory
    and optionally adds it to the user's PATH environment variable.

.PARAMETER InstallPath
    Custom installation path (default: $env:LOCALAPPDATA\FileFlux)

.PARAMETER AddToPath
    Add installation directory to user PATH if not already present

.PARAMETER SkipBuild
    Skip building the project and deploy existing binaries

.PARAMETER Configuration
    Build configuration: Debug or Release (default: Release)

.EXAMPLE
    .\deploy-cli-local.ps1
    Deploy to default location with PATH update

.EXAMPLE
    .\deploy-cli-local.ps1 -InstallPath "C:\Tools\FileFlux"
    Deploy to custom location

.EXAMPLE
    .\deploy-cli-local.ps1 -SkipBuild
    Deploy existing binaries without rebuilding

.NOTES
    Author: FileFlux Team
    Requires: .NET 9 SDK
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$InstallPath = "$env:LOCALAPPDATA\FileFlux",

    [Parameter(Mandatory=$false)]
    [switch]$AddToPath = $true,

    [Parameter(Mandatory=$false)]
    [switch]$SkipBuild,

    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Colors for output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = 'White'
    )
    Write-Host $Message -ForegroundColor $Color
}

function Write-Success { param([string]$Message) Write-ColorOutput "✓ $Message" 'Green' }
function Write-Info { param([string]$Message) Write-ColorOutput "ℹ $Message" 'Cyan' }
function Write-Warning { param([string]$Message) Write-ColorOutput "⚠ $Message" 'Yellow' }
function Write-Error { param([string]$Message) Write-ColorOutput "✗ $Message" 'Red' }

# Script paths
$scriptRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $scriptRoot "src\FileFlux.CLI\FileFlux.CLI.csproj"
$buildOutput = Join-Path $scriptRoot "src\FileFlux.CLI\bin\$Configuration\net9.0"

Write-ColorOutput "`n=== FileFlux CLI Local Deployment ===" 'Magenta'
Write-Info "Install Path: $InstallPath"
Write-Info "Configuration: $Configuration"
Write-Info "Add to PATH: $AddToPath"
Write-Host ""

# Check .NET SDK
try {
    $dotnetVersion = dotnet --version
    Write-Success ".NET SDK found: $dotnetVersion"
} catch {
    Write-Error ".NET SDK not found. Please install .NET 9 SDK."
    Write-Info "Download from: https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
}

# Check project file
if (-not (Test-Path $projectPath)) {
    Write-Error "Project file not found: $projectPath"
    exit 1
}
Write-Success "Project file found"

# Build project
if (-not $SkipBuild) {
    Write-Info "Building FileFlux CLI..."
    try {
        Push-Location (Split-Path $projectPath)
        dotnet build $projectPath `
            --configuration $Configuration `
            --verbosity minimal

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
        Pop-Location
        Write-Success "Build completed successfully"
    } catch {
        Pop-Location
        Write-Error "Build failed: $_"
        exit 1
    }
} else {
    Write-Info "Skipping build (using existing binaries)"
}

# Verify build output
if (-not (Test-Path $buildOutput)) {
    Write-Error "Build output not found: $buildOutput"
    Write-Info "Try running without -SkipBuild flag"
    exit 1
}

$exePath = Join-Path $buildOutput "FileFlux.CLI.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Executable not found: $exePath"
    exit 1
}
Write-Success "Build output verified"

# Create installation directory
Write-Info "Creating installation directory..."
if (Test-Path $InstallPath) {
    Write-Warning "Installation directory already exists, will overwrite"
    Remove-Item $InstallPath -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Write-Success "Installation directory created"

# Copy files
Write-Info "Copying files to installation directory..."
try {
    Copy-Item "$buildOutput\*" -Destination $InstallPath -Recurse -Force
    Write-Success "Files copied successfully"
} catch {
    Write-Error "Failed to copy files: $_"
    exit 1
}

# Create fileflux.exe wrapper (shorter name)
$wrapperPath = Join-Path $InstallPath "fileflux.exe"
$originalPath = Join-Path $InstallPath "FileFlux.CLI.exe"

if (Test-Path $originalPath) {
    Copy-Item $originalPath $wrapperPath -Force
    Write-Success "Created fileflux.exe wrapper"
}

# Verify installation
$installedExe = Join-Path $InstallPath "fileflux.exe"
if (-not (Test-Path $installedExe)) {
    Write-Error "Installation verification failed: fileflux.exe not found"
    exit 1
}

# Get version
try {
    $version = & $installedExe --version 2>&1
    Write-Success "Installation verified: $version"
} catch {
    Write-Warning "Could not get version, but executable exists"
}

# Add to PATH
if ($AddToPath) {
    Write-Info "Checking PATH environment variable..."

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $paths = $userPath -split ';' | Where-Object { $_ -ne '' }

    if ($paths -contains $InstallPath) {
        Write-Info "Installation path already in PATH"

        # Ensure it's at the beginning for priority
        $filteredPaths = $paths | Where-Object { $_ -ne $InstallPath }
        $newPath = "$InstallPath;" + ($filteredPaths -join ';')
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Success "Moved installation path to beginning of PATH (higher priority)"

        # Update current session
        $env:Path = "$InstallPath;" + ($env:Path -replace [regex]::Escape(";$InstallPath"), '')
        Write-Info "PATH updated for current session"
    } else {
        Write-Info "Adding installation path to user PATH..."
        try {
            # Add to beginning, not end
            $newPath = "$InstallPath;$userPath"
            [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
            Write-Success "Added to user PATH with high priority (restart terminal to apply)"

            # Update current session
            $env:Path = "$InstallPath;$env:Path"
            Write-Info "PATH updated for current session"
        } catch {
            Write-Warning "Failed to update PATH: $_"
            Write-Info "You can manually add to PATH: $InstallPath"
        }
    }
}

# Summary
Write-Host ""
Write-ColorOutput "=== Deployment Summary ===" 'Magenta'
Write-Success "FileFlux CLI deployed successfully!"
Write-Info "Installation path: $InstallPath"
Write-Info "Executable: fileflux.exe"

if ($AddToPath) {
    Write-Host ""
    Write-ColorOutput "Usage (after restarting terminal):" 'Yellow'
    Write-Host "  fileflux --help"
    Write-Host "  fileflux extract `"document.pdf`""
    Write-Host "  fileflux extract `"document.pptx`" --enable-vision"
    Write-Host "  fileflux chunk `"document.pdf`" -s Smart"
} else {
    Write-Host ""
    Write-ColorOutput "Usage:" 'Yellow'
    Write-Host "  $installedExe --help"
    Write-Host "  $installedExe extract `"document.pdf`""
}

Write-Host ""
Write-ColorOutput "Configuration:" 'Yellow'
Write-Host "  Set OPENAI_API_KEY for AI features"
Write-Host "  Set OPENAI_MODEL for custom model (default: gpt-5-nano)"
Write-Host ""
Write-Info "Documentation: docs\CLI_VISION.md"
Write-Host ""
