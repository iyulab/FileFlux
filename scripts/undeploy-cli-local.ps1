#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Uninstall FileFlux CLI from local user directory

.DESCRIPTION
    This script removes the FileFlux CLI tool from the local installation
    directory and optionally removes it from the user's PATH environment variable.

.PARAMETER InstallPath
    Installation path to remove (default: $env:LOCALAPPDATA\FileFlux)

.PARAMETER RemoveFromPath
    Remove installation directory from user PATH

.EXAMPLE
    .\undeploy-cli-local.ps1
    Remove from default location and update PATH

.EXAMPLE
    .\undeploy-cli-local.ps1 -InstallPath "C:\Tools\FileFlux"
    Remove from custom location

.NOTES
    Author: FileFlux Team
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$InstallPath = "$env:LOCALAPPDATA\FileFlux",

    [Parameter(Mandatory=$false)]
    [switch]$RemoveFromPath = $true
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

Write-ColorOutput "`n=== FileFlux CLI Uninstallation ===" 'Magenta'
Write-Info "Install Path: $InstallPath"
Write-Host ""

# Check if installation exists
if (-not (Test-Path $InstallPath)) {
    Write-Warning "Installation not found at: $InstallPath"
    Write-Info "Nothing to uninstall"
    exit 0
}

# Confirm uninstallation
Write-Warning "This will remove FileFlux CLI from your system"
$confirm = Read-Host "Continue? (y/N)"
if ($confirm -ne 'y' -and $confirm -ne 'Y') {
    Write-Info "Uninstallation cancelled"
    exit 0
}

# Remove installation directory
Write-Info "Removing installation directory..."
try {
    Remove-Item $InstallPath -Recurse -Force
    Write-Success "Installation directory removed"
} catch {
    Write-Error "Failed to remove installation: $_"
    exit 1
}

# Remove from PATH
if ($RemoveFromPath) {
    Write-Info "Removing from PATH environment variable..."

    try {
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        $paths = $userPath -split ';' | Where-Object { $_ -ne '' -and $_ -ne $InstallPath }

        $newPath = $paths -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Success "Removed from user PATH (restart terminal to apply)"
    } catch {
        Write-Warning "Failed to update PATH: $_"
    }
}

Write-Host ""
Write-Success "FileFlux CLI uninstalled successfully!"
Write-Host ""
