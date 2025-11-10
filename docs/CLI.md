# FileFlux CLI Documentation

Command-line interface for FileFlux document processing without writing code.

## Table of Contents

- [Installation](#installation)
- [Basic Commands](#basic-commands)
  - [Extract Command](#extract-command)
  - [Chunk Command](#chunk-command)
  - [Process Command](#process-command)
  - [Info Command](#info-command)
- [AI Provider Integration](#ai-provider-integration)
  - [OpenAI Vision API](#openai-vision-api)
  - [Anthropic Claude API](#anthropic-claude-api)
  - [Provider Selection](#provider-selection)
- [Advanced Usage](#advanced-usage)
  - [Environment Variables](#environment-variables)
  - [Output Formats](#output-formats)
  - [Cost Considerations](#cost-considerations)
- [Troubleshooting](#troubleshooting)
- [Uninstall](#uninstall)

## Installation

Deploy CLI to your local system using the deployment script:

```powershell
# From repository root
.\scripts\deploy-cli-local.ps1

# Restart terminal, then verify
fileflux --version
```

**Script Options**:
- `-InstallPath`: Custom installation path (default: `$env:LOCALAPPDATA\FileFlux`)
- `-Configuration`: Debug or Release (default: Release)
- `-SkipBuild`: Use existing binaries without rebuilding
- `-AddToPath`: Add to user PATH (default: true)

**Example with custom options**:
```powershell
.\scripts\deploy-cli-local.ps1 -InstallPath "C:\Tools\FileFlux" -Configuration Debug
```

## Basic Commands

### Extract Command

Extract raw text and content from documents:

```bash
# Basic extraction
fileflux extract "document.pdf"

# Specify output format
fileflux extract "document.pdf" -f json
fileflux extract "document.pdf" -f jsonl
fileflux extract "document.pdf" -f markdown

# Custom output path
fileflux extract "document.pdf" -o "output.json"

# Quiet mode (suppress progress)
fileflux extract "document.pdf" --quiet
```

**Supported Input Formats**:
- PDF (`.pdf`)
- Word (`.docx`)
- Excel (`.xlsx`)
- PowerPoint (`.pptx`)
- Markdown (`.md`)
- Text (`.txt`)
- JSON (`.json`)
- CSV (`.csv`)

### Chunk Command

Process documents with specific chunking strategies:

```bash
# Auto strategy (recommended - automatically selects best strategy)
fileflux chunk "document.pdf"

# Specific strategies
fileflux chunk "document.pdf" -s Smart      # AI-powered semantic chunking
fileflux chunk "document.pdf" -s Intelligent # Structure-aware chunking
fileflux chunk "document.pdf" -s Semantic   # Meaning-based chunking

# Custom chunk size and overlap
fileflux chunk "document.pdf" -s Smart --max-size 512 --overlap 64

# Multiple output formats
fileflux chunk "document.pdf" -f json -o "chunks.json"
fileflux chunk "document.pdf" -f jsonl -o "chunks.jsonl"
```

**Chunking Strategies**:
- **Auto**: Automatically selects optimal strategy based on document type
- **Smart**: AI-powered semantic chunking (requires AI provider)
- **Intelligent**: Structure-aware chunking using document hierarchy
- **Semantic**: Meaning-based chunking for coherent text segments

### Process Command

Complete document processing pipeline with metadata enrichment:

```bash
# Full pipeline (extract → parse → chunk → enrich metadata)
fileflux process "document.pdf"

# With custom strategy and output
fileflux process "document.pdf" -s Smart -o "output.json"

# With vision API enabled
fileflux process "presentation.pptx" --enable-vision -o "output.json"
```

### Info Command

View document information without processing:

```bash
# Display document metadata
fileflux info "document.pdf"

# Example output:
# Document: document.pdf
# Size: 2.5 MB
# Format: PDF
# Pages: 42
# Estimated processing time: ~3 seconds
```

## AI Provider Integration

FileFlux CLI supports two AI providers for advanced features like vision API and semantic chunking:

### OpenAI Vision API

Enable AI-powered image extraction using OpenAI's multimodal models.

#### Setup

Configure OpenAI credentials:

```powershell
# Windows PowerShell (current session)
$env:OPENAI_API_KEY = "sk-your-api-key"
$env:OPENAI_MODEL = "gpt-5-nano"

# Windows PowerShell (permanent)
[System.Environment]::SetEnvironmentVariable('OPENAI_API_KEY', 'sk-...', 'User')
[System.Environment]::SetEnvironmentVariable('OPENAI_MODEL', 'gpt-5-nano', 'User')

# Linux/macOS
export OPENAI_API_KEY="sk-your-api-key"
export OPENAI_MODEL="gpt-5-nano"
```

**Supported Models**:
- `gpt-5-nano` (recommended) - Latest, fastest, most cost-effective
- `gpt-4o` - Higher accuracy for complex images
- `gpt-4-turbo` - Alternative with vision capabilities

#### Usage

```bash
# Extract with image analysis
fileflux extract "presentation.pptx" --enable-vision

# Full processing with vision
fileflux process "document.pdf" --enable-vision -o "output.json"

# With specific output format
fileflux extract "slides.pptx" --enable-vision -f markdown
```

**Vision Capabilities**:
- **Charts/Graphs**: Extracts titles, axis labels, data points, trends
- **Tables**: Structured extraction of headers and cell values
- **Diagrams**: Visual element descriptions and text labels
- **Documents**: Text extraction with layout preservation
- **Scanned Images**: OCR-like extraction from scanned documents

**Cost Estimate** (GPT-5-nano):
- ~$0.00015 per image (input)
- ~$0.0006 per image (output)
- Average: ~$0.0008 per image

### Anthropic Claude API

Enable AI-powered processing using Anthropic's Claude models with vision capabilities.

#### Setup

Configure Anthropic credentials:

```powershell
# Windows PowerShell (current session)
$env:ANTHROPIC_API_KEY = "sk-ant-your-api-key"
$env:ANTHROPIC_MODEL = "claude-3-5-sonnet-20241022"

# Windows PowerShell (permanent)
[System.Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', 'sk-ant-...', 'User')
[System.Environment]::SetEnvironmentVariable('ANTHROPIC_MODEL', 'claude-3-5-sonnet-20241022', 'User')

# Linux/macOS
export ANTHROPIC_API_KEY="sk-ant-your-api-key"
export ANTHROPIC_MODEL="claude-3-5-sonnet-20241022"
```

**Supported Models**:
- `claude-3-5-sonnet-20241022` (recommended) - Best balance of speed and quality
- `claude-3-5-haiku-20241022` - Fastest, most cost-effective
- `claude-3-opus-20240229` - Highest quality for complex analysis

#### Usage

```bash
# Same commands work with Anthropic if API key is configured
fileflux extract "presentation.pptx" --enable-vision
fileflux process "document.pdf" --enable-vision -o "output.json"
```

**Vision Capabilities**:
- Same as OpenAI: charts, tables, diagrams, documents, scanned images
- Claude excels at: detailed analysis, complex diagrams, technical content
- High accuracy with confidence scores (~0.90)

**Cost Estimate** (Claude 3.5 Sonnet):
- ~$0.003 per 1K input tokens
- ~$0.015 per 1K output tokens
- Typical image: ~$0.001-0.003

### Provider Selection

FileFlux automatically detects and uses available AI providers:

**Priority Order**:
1. Explicitly set via `FILEFLUX_PROVIDER` environment variable
2. OpenAI (if `OPENAI_API_KEY` is set)
3. Anthropic (if `ANTHROPIC_API_KEY` is set)
4. No AI provider (basic processing only)

**Force Specific Provider**:
```powershell
# Use OpenAI even if both are configured
$env:FILEFLUX_PROVIDER = "openai"

# Use Anthropic
$env:FILEFLUX_PROVIDER = "anthropic"
```

**Check Active Provider**:
```bash
# The CLI shows active provider at startup
fileflux extract "document.pdf" --enable-vision

# Output shows:
# Provider: Anthropic (claude-3-5-sonnet-20241022) + Vision
```

## Advanced Usage

### Environment Variables

Complete environment variable reference:

#### Provider Selection
- `FILEFLUX_PROVIDER` - Force specific provider: `openai` or `anthropic`
- `PROVIDER` - Fallback for provider selection

#### OpenAI Configuration
Priority order (first found is used):
1. `FILEFLUX_OPENAI_API_KEY` - FileFlux-specific API key
2. `OPENAI_API_KEY` - Standard OpenAI key
3. `API_KEY` - Generic fallback

Model selection:
1. `FILEFLUX_OPENAI_MODEL` - FileFlux-specific model
2. `OPENAI_MODEL` - Standard model setting
3. `MODEL` - Generic fallback
4. Default: `gpt-5-nano`

#### Anthropic Configuration
Priority order (first found is used):
1. `FILEFLUX_ANTHROPIC_API_KEY` - FileFlux-specific API key
2. `ANTHROPIC_API_KEY` - Standard Anthropic key
3. `API_KEY` - Generic fallback

Model selection:
1. `FILEFLUX_ANTHROPIC_MODEL` - FileFlux-specific model
2. `ANTHROPIC_MODEL` - Standard model setting
3. `MODEL` - Generic fallback
4. Default: `claude-3-5-sonnet-20241022`

**Example Configuration**:
```powershell
# Use different providers for different projects
$env:FILEFLUX_OPENAI_API_KEY = "sk-proj1-key"
$env:FILEFLUX_ANTHROPIC_API_KEY = "sk-ant-proj1-key"
$env:FILEFLUX_PROVIDER = "anthropic"
```

### Output Formats

FileFlux supports multiple output formats for different use cases:

#### JSON Format
```bash
fileflux extract "document.pdf" -f json -o "output.json"
```

**Structure**:
```json
{
  "metadata": {
    "fileName": "document.pdf",
    "fileSize": 2621440,
    "processedAt": "2025-01-15T10:30:00Z",
    "chunkCount": 42
  },
  "chunks": [
    {
      "index": 0,
      "id": "550e8400-e29b-41d4-a716-446655440000",
      "content": "Document content here...",
      "quality": 0.85,
      "metadata": {
        "page": 1,
        "section": "Introduction",
        "hasImages": false
      }
    }
  ]
}
```

#### JSONL Format (JSON Lines)
```bash
fileflux extract "document.pdf" -f jsonl -o "output.jsonl"
```

**Advantages**:
- Streaming-friendly
- Memory efficient for large documents
- Easy to process line-by-line

**Structure** (one JSON object per line):
```jsonl
{"index":0,"id":"550e8400-...","content":"First chunk...","quality":0.85}
{"index":1,"id":"550e8401-...","content":"Second chunk...","quality":0.90}
```

#### Markdown Format
```bash
fileflux extract "document.pdf" -f markdown -o "output.md"
```

**Advantages**:
- Human-readable
- Preserves document structure
- Easy to preview and edit

**Structure**:
```markdown
# Document Title

## Section 1

Content from first section...

---
**Chunk Metadata**: Index: 0, Quality: 0.85

## Section 2

Content from second section...

---
**Chunk Metadata**: Index: 1, Quality: 0.90
```

### Cost Considerations

#### Vision API Costs

**OpenAI (GPT-5-nano)**:
- Input: ~$0.00015 per image
- Output: ~$0.0006 per image
- Total: ~$0.00075 per image
- 1,000 images: ~$0.75

**Anthropic (Claude 3.5 Sonnet)**:
- Input: ~$0.003 per 1K tokens (~500 tokens per image)
- Output: ~$0.015 per 1K tokens (~200 tokens per image)
- Total: ~$0.0045 per image
- 1,000 images: ~$4.50

**Best Practices**:
1. **Test first**: Process small batches to estimate costs
2. **Use vision selectively**: Enable only for image-heavy documents
3. **Choose appropriate model**:
   - GPT-5-nano: Cost-effective for most cases
   - Claude 3.5 Haiku: Fastest Anthropic option
4. **Monitor usage**: Set up billing alerts in provider dashboards
5. **Batch processing**: Process multiple documents in one session

**Cost Optimization Examples**:
```bash
# Process only image-heavy documents with vision
fileflux extract "text-only.pdf" -o "text.json"              # No AI cost
fileflux extract "with-charts.pptx" --enable-vision -o "charts.json"  # AI cost

# Use Haiku for simple images
$env:ANTHROPIC_MODEL = "claude-3-5-haiku-20241022"
fileflux extract "simple-diagrams.pdf" --enable-vision
```

## Troubleshooting

### CLI Not Found

**Problem**: `fileflux` command not recognized after installation

**Solutions**:

```powershell
# Windows: Check PATH
echo $env:Path

# Verify FileFlux directory is listed
# Should see: C:\Users\YourName\AppData\Local\FileFlux

# Add manually to current session
$env:Path += ";$env:LOCALAPPDATA\FileFlux"

# Permanently add to PATH
[System.Environment]::SetEnvironmentVariable('Path',
  $env:Path + ";$env:LOCALAPPDATA\FileFlux", 'User')

# Restart terminal after PATH changes
```

```bash
# Linux/macOS: Check PATH
echo $PATH

# Add to current session
export PATH="$HOME/.local/share/FileFlux:$PATH"

# Add permanently to shell profile
echo 'export PATH="$HOME/.local/share/FileFlux:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

### API Key Not Found

**Problem**: Vision enabled but API key not detected

**Error Message**:
```
Warning: Vision enabled but no API key found.
Falling back to basic extraction...
```

**Solutions**:

```powershell
# 1. Verify environment variable is set
echo $env:OPENAI_API_KEY     # or $env:ANTHROPIC_API_KEY

# 2. If empty, set it
$env:OPENAI_API_KEY = "sk-your-api-key"
$env:ANTHROPIC_API_KEY = "sk-ant-your-api-key"

# 3. For permanent setup
[System.Environment]::SetEnvironmentVariable('OPENAI_API_KEY', 'sk-...', 'User')
[System.Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', 'sk-ant-...', 'User')

# 4. Restart terminal after setting permanent variables

# 5. Test with simple command
fileflux info "test.pdf"  # Should show provider info
```

### Provider Selection Issues

**Problem**: Wrong AI provider being used

**Solutions**:

```powershell
# Check which provider is active
fileflux extract "document.pdf" --enable-vision
# Output shows active provider

# Force specific provider
$env:FILEFLUX_PROVIDER = "openai"      # Use OpenAI
$env:FILEFLUX_PROVIDER = "anthropic"   # Use Anthropic

# Unset provider to use auto-detection
Remove-Item Env:FILEFLUX_PROVIDER
```

### PowerShell Execution Policy

**Problem**: Deployment script blocked

**Error Message**:
```
.\scripts\deploy-cli-local.ps1 : File cannot be loaded because running scripts is disabled
```

**Solution**:
```powershell
# Check current policy
Get-ExecutionPolicy

# Set policy to allow local scripts
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned

# Verify change
Get-ExecutionPolicy
```

### Vision API Errors

**Problem**: Vision processing fails with API errors

**Common Issues**:

1. **Invalid API Key**:
   ```
   Error: Authentication failed. Check your API key.
   ```
   Solution: Verify API key is correct and active in provider dashboard

2. **Rate Limit Exceeded**:
   ```
   Error: Rate limit exceeded. Please retry after a few seconds.
   ```
   Solution: Wait and retry, or upgrade API plan for higher limits

3. **Unsupported Image Format**:
   ```
   Error: Image format not supported
   ```
   Solution: Verify image is PNG, JPEG, GIF, or WebP

4. **Image Too Large**:
   ```
   Error: Image size exceeds maximum allowed
   ```
   Solution:
   - OpenAI: Max 20MB per image
   - Anthropic: Max 5MB per image
   - Resize images before processing if needed

### Performance Issues

**Problem**: Processing is slow or hanging

**Solutions**:

```bash
# 1. Check file size
fileflux info "large-document.pdf"

# 2. Process without vision first
fileflux extract "large-document.pdf" -o "output.json"

# 3. Use lighter chunking strategy
fileflux chunk "large-document.pdf" -s Intelligent  # Instead of Smart

# 4. Enable quiet mode for large batches
fileflux process "*.pdf" --quiet
```

### Uninstall

Remove FileFlux CLI from your system:

```powershell
# Windows
.\scripts\undeploy-cli-local.ps1

# Manually remove if script fails
Remove-Item -Path "$env:LOCALAPPDATA\FileFlux" -Recurse -Force

# Remove from PATH (if added manually)
# Edit environment variables in System Properties
```

```bash
# Linux/macOS
rm -rf ~/.local/share/FileFlux

# Remove from PATH
# Edit ~/.bashrc or ~/.zshrc and remove FileFlux PATH entry
```

## Related Documentation

- [Main Tutorial](TUTORIAL.md) - Complete usage guide with SDK examples
- [Architecture](ARCHITECTURE.md) - System design and architecture details
- [Building](../BUILDING.md) - Build, test, and development guide
- [Test Results](RESULTS.md) - Real-world API test results
- [GitHub Repository](https://github.com/iyulab/FileFlux)
- [NuGet Package](https://www.nuget.org/packages/FileFlux)

## Quick Reference

### Common Commands
```bash
# Basic extraction
fileflux extract "doc.pdf"

# Chunking with strategy
fileflux chunk "doc.pdf" -s Smart

# Full pipeline
fileflux process "doc.pdf"

# With vision API
fileflux process "slides.pptx" --enable-vision

# Custom output
fileflux extract "doc.pdf" -f json -o "output.json"
```

### Environment Setup
```powershell
# OpenAI
$env:OPENAI_API_KEY = "sk-..."
$env:OPENAI_MODEL = "gpt-5-nano"

# Anthropic
$env:ANTHROPIC_API_KEY = "sk-ant-..."
$env:ANTHROPIC_MODEL = "claude-3-5-sonnet-20241022"

# Force provider
$env:FILEFLUX_PROVIDER = "anthropic"
```

### Supported Formats
**Input**: PDF, DOCX, XLSX, PPTX, MD, TXT, JSON, CSV
**Output**: JSON, JSONL, Markdown
**Images**: PNG, JPEG, GIF, BMP, WebP (with vision API)
