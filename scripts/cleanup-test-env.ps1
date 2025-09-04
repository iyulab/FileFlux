# FileFlux Test Environment Cleanup Script
# Keeps only the PDF file and removes all processing results

$testDir = "D:\data\FileFlux\test\test-a"

Write-Host "🧹 FileFlux Test Environment Cleanup" -ForegroundColor Cyan
Write-Host "Target Directory: $testDir" -ForegroundColor Yellow

if (!(Test-Path $testDir)) {
    Write-Host "❌ Test directory not found: $testDir" -ForegroundColor Red
    exit 1
}

# Get PDF files to preserve
$pdfFiles = Get-ChildItem -Path $testDir -Filter "*.pdf" | Select-Object -ExpandProperty Name

if ($pdfFiles.Count -eq 0) {
    Write-Host "⚠️  No PDF files found to preserve!" -ForegroundColor Yellow
    $confirm = Read-Host "Continue with cleanup anyway? (y/N)"
    if ($confirm -ne "y" -and $confirm -ne "Y") {
        Write-Host "❌ Cleanup cancelled" -ForegroundColor Red
        exit 0
    }
} else {
    Write-Host "📄 PDF files to preserve:" -ForegroundColor Green
    $pdfFiles | ForEach-Object { Write-Host "  • $_" -ForegroundColor White }
}

# Directories to remove
$dirsToRemove = @(
    "chunking-results",
    "extraction-results", 
    "parsing-results",
    "logs"
)

# Files to remove (excluding PDFs)
$filesToRemove = Get-ChildItem -Path $testDir -File | Where-Object { $_.Extension -ne ".pdf" }

Write-Host "`n🗑️  Cleaning up test environment..." -ForegroundColor Yellow

# Remove directories
foreach ($dir in $dirsToRemove) {
    $dirPath = Join-Path $testDir $dir
    if (Test-Path $dirPath) {
        Write-Host "  Removing directory: $dir" -ForegroundColor Gray
        Remove-Item -Path $dirPath -Recurse -Force
    }
}

# Remove non-PDF files
foreach ($file in $filesToRemove) {
    Write-Host "  Removing file: $($file.Name)" -ForegroundColor Gray
    Remove-Item -Path $file.FullName -Force
}

Write-Host "`n✅ Cleanup completed!" -ForegroundColor Green
Write-Host "📁 Clean test environment ready at: $testDir" -ForegroundColor Cyan

# Show final directory contents
Write-Host "`n📋 Remaining files:" -ForegroundColor Yellow
Get-ChildItem -Path $testDir | ForEach-Object { 
    Write-Host "  • $($_.Name)" -ForegroundColor White 
}