<#
.SYNOPSIS
    Publishes FileFlux CLI as a local dotnet tool.

.DESCRIPTION
    This script packages the FileFlux CLI project and installs it as a global dotnet tool
    for local development and testing.

.PARAMETER Uninstall
    Uninstall the existing tool before installing.

.PARAMETER Force
    Force reinstallation even if the same version exists.

.EXAMPLE
    .\publish-local.ps1
    Packages and installs FileFlux CLI as a global tool.

.EXAMPLE
    .\publish-local.ps1 -Uninstall
    Uninstalls the existing tool first, then installs fresh.

.EXAMPLE
    .\publish-local.ps1 -Force
    Force reinstalls the tool.
#>

param(
    [switch]$Uninstall,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Join-Path $ScriptDir "FileFlux.CLI"
$ProjectFile = Join-Path $ProjectDir "FileFlux.CLI.csproj"
$OutputDir = Join-Path $ScriptDir "nupkg"
$ToolName = "fileflux"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  FileFlux CLI Local Publisher" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Verify project exists
if (-not (Test-Path $ProjectFile)) {
    Write-Host "Error: Project file not found at $ProjectFile" -ForegroundColor Red
    exit 1
}

# Uninstall existing tool if requested or if reinstalling
if ($Uninstall -or $Force) {
    Write-Host "[1/4] Uninstalling existing tool..." -ForegroundColor Yellow
    $existing = dotnet tool list -g | Select-String $ToolName
    if ($existing) {
        dotnet tool uninstall -g FileFlux.CLI
        Write-Host "  -> Uninstalled existing tool" -ForegroundColor Green
    } else {
        Write-Host "  -> No existing tool found" -ForegroundColor Gray
    }
} else {
    Write-Host "[1/4] Checking existing installation..." -ForegroundColor Yellow
    $existing = dotnet tool list -g | Select-String $ToolName
    if ($existing) {
        Write-Host "  -> Tool already installed. Use -Force to reinstall or -Uninstall to remove first." -ForegroundColor Yellow
    }
}

# Clean output directory
Write-Host "[2/4] Cleaning output directory..." -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
Write-Host "  -> Output: $OutputDir" -ForegroundColor Gray

# Pack the project
Write-Host "[3/4] Packing project..." -ForegroundColor Yellow
$packResult = dotnet pack $ProjectFile -c Release -o $OutputDir 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Pack failed" -ForegroundColor Red
    Write-Host $packResult
    exit 1
}

# Find the generated nupkg
$nupkg = Get-ChildItem $OutputDir -Filter "*.nupkg" | Select-Object -First 1
if (-not $nupkg) {
    Write-Host "Error: No .nupkg file found in $OutputDir" -ForegroundColor Red
    exit 1
}
Write-Host "  -> Package: $($nupkg.Name)" -ForegroundColor Green

# Install as global tool
Write-Host "[4/4] Installing as global tool..." -ForegroundColor Yellow
$installArgs = @("tool", "install", "-g", "FileFlux.CLI", "--add-source", $OutputDir)
if ($Force) {
    # Uninstall first if force (ignore errors if not installed)
    try {
        $ErrorActionPreference = "SilentlyContinue"
        dotnet tool uninstall -g FileFlux.CLI 2>&1 | Out-Null
        $ErrorActionPreference = "Stop"
    } catch {
        # Ignore - tool may not be installed
    }
}

$installResult = & dotnet @installArgs 2>&1
if ($LASTEXITCODE -ne 0) {
    # Try update if install fails
    Write-Host "  -> Install failed, trying update..." -ForegroundColor Yellow
    $updateResult = dotnet tool update -g FileFlux.CLI --add-source $OutputDir 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Installation failed" -ForegroundColor Red
        Write-Host $updateResult
        exit 1
    }
    Write-Host "  -> Updated successfully" -ForegroundColor Green
} else {
    Write-Host "  -> Installed successfully" -ForegroundColor Green
}

# Verify installation
Write-Host "`n----------------------------------------" -ForegroundColor Cyan
Write-Host "Verifying installation..." -ForegroundColor Cyan
$version = & $ToolName --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  Tool: $ToolName" -ForegroundColor Green
    Write-Host "  Version: $version" -ForegroundColor Green

    Write-Host "`n----------------------------------------" -ForegroundColor Cyan
    Write-Host "Usage examples:" -ForegroundColor Cyan
    Write-Host "  fileflux --help" -ForegroundColor Gray
    Write-Host "  fileflux status" -ForegroundColor Gray
    Write-Host "  fileflux extract document.pdf" -ForegroundColor Gray
    Write-Host "  fileflux process document.docx --output ./output" -ForegroundColor Gray
    Write-Host "----------------------------------------`n" -ForegroundColor Cyan
} else {
    Write-Host "Warning: Could not verify installation" -ForegroundColor Yellow
    Write-Host "Try running: $ToolName --help" -ForegroundColor Gray
}

Write-Host "Done!" -ForegroundColor Green
