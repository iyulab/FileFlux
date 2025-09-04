using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.LlmOptimization;

/// <summary>
/// ContextualHeader 생성 기능 통합 테스트
/// </summary>
public class ContextualHeaderGenerationTests
{
    private readonly ITestOutputHelper _output;
    private readonly ProgressiveDocumentProcessor _processor;

    public ContextualHeaderGenerationTests(ITestOutputHelper output)
    {
        _output = output;

        var readerFactory = new DocumentReaderFactory();
        readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

        var mockTextCompletionService = new MockTextCompletionService();
        var parserFactory = new DocumentParserFactory(mockTextCompletionService);
        var chunkingFactory = new ChunkingStrategyFactory();
        chunkingFactory.RegisterStrategy(() => new Infrastructure.Strategies.IntelligentChunkingStrategy());

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ProgressiveDocumentProcessor>();
        _processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, logger);
    }

    [Fact]
    public async Task ContextualHeader_ForTechnicalDocument_ShouldIncludeAllRelevantMetadata()
    {
        // Arrange
        var technicalContent = @"
# API Documentation

## Authentication Service

The REST API endpoint provides OAuth 2.0 authentication:

```javascript
const response = await fetch('/api/auth/login', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ username, password })
});
```

### Database Schema

| Table | Primary Key | Description |
|-------|-------------|-------------|
| users | id          | User data   |
| roles | id          | User roles  |
";

        // Act
        var chunks = await ProcessContent(technicalContent);

        // Assert
        Assert.NotEmpty(chunks);

        var headerChunk = chunks.First(c => c.StructuralRole == "header");
        var codeChunk = chunks.FirstOrDefault(c => c.StructuralRole == "code_block");
        var tableChunk = chunks.FirstOrDefault(c => c.StructuralRole == "table");

        // Header chunk 검증
        Assert.NotNull(headerChunk.ContextualHeader);
        Assert.Contains("Structure: header", headerChunk.ContextualHeader);
        Assert.Contains("Domain: Technical", headerChunk.ContextualHeader);
        Assert.Contains("Tech:", headerChunk.ContextualHeader);

        _output.WriteLine($"Header Chunk ContextualHeader: {headerChunk.ContextualHeader}");
        _output.WriteLine($"Header Chunk Keywords: [{string.Join(", ", headerChunk.TechnicalKeywords)}]");

        // Code chunk 검증 (있는 경우)
        if (codeChunk != null)
        {
            Assert.Contains("Structure: code_block", codeChunk.ContextualHeader);
            _output.WriteLine($"Code Chunk ContextualHeader: {codeChunk.ContextualHeader}");
        }

        // Table chunk 검증 (있는 경우)
        if (tableChunk != null)
        {
            Assert.Contains("Structure: table", tableChunk.ContextualHeader);
            _output.WriteLine($"Table Chunk ContextualHeader: {tableChunk.ContextualHeader}");
        }
    }

    [Fact]
    public async Task ContextualHeader_ForBusinessDocument_ShouldReflectBusinessDomain()
    {
        // Arrange
        var businessContent = @"
# Project Requirements Document

## Business Objectives

The strategic initiative aims to improve customer satisfaction and increase market share through innovative solutions.

### Requirements Analysis

1. Stakeholder engagement strategy
2. Resource allocation planning
3. Timeline and milestone definition

Key business metrics:
- Customer retention rate: 85%
- Market penetration: 15%
- Revenue growth: 20%
";

        // Act
        var chunks = await ProcessContent(businessContent);

        // Assert
        Assert.NotEmpty(chunks);

        var businessChunks = chunks.Where(c => c.DocumentDomain == "Business").ToList();
        Assert.NotEmpty(businessChunks);

        var headerChunk = businessChunks.First(c => c.StructuralRole == "header");

        Assert.NotNull(headerChunk.ContextualHeader);
        Assert.Contains("Domain: Business", headerChunk.ContextualHeader);
        Assert.Contains("Structure: header", headerChunk.ContextualHeader);

        _output.WriteLine($"Business Header ContextualHeader: {headerChunk.ContextualHeader}");
        _output.WriteLine($"Business Domain: {headerChunk.DocumentDomain}");
    }

    [Fact]
    public async Task ContextualHeader_ForAcademicDocument_ShouldReflectAcademicDomain()
    {
        // Arrange
        var academicContent = @"
# Research Methodology

## Abstract

This study investigates the correlation between software architecture patterns and system maintainability through empirical analysis.

## Literature Review

Previous research has established the theoretical framework for measuring code quality metrics and their impact on long-term software maintenance.

### Data Analysis

The statistical analysis reveals significant correlations (p < 0.05) between architectural decisions and maintenance costs.
";

        // Act
        var chunks = await ProcessContent(academicContent);

        // Assert
        Assert.NotEmpty(chunks);

        var academicChunks = chunks.Where(c => c.DocumentDomain == "Academic").ToList();
        Assert.NotEmpty(academicChunks);

        var headerChunk = academicChunks.First(c => c.StructuralRole == "header");

        Assert.NotNull(headerChunk.ContextualHeader);
        Assert.Contains("Domain: Academic", headerChunk.ContextualHeader);

        _output.WriteLine($"Academic Header ContextualHeader: {headerChunk.ContextualHeader}");
        _output.WriteLine($"Academic Domain: {headerChunk.DocumentDomain}");
    }

    [Fact]
    public async Task ContextualHeader_WithoutSpecificDomain_ShouldUseGeneral()
    {
        // Arrange
        var generalContent = @"
# Simple Document

This is a regular document without specific domain markers.

## Basic Content

Just some normal text that talks about everyday topics like weather, food, and general conversation.
";

        // Act
        var chunks = await ProcessContent(generalContent);

        // Assert
        Assert.NotEmpty(chunks);

        var generalChunk = chunks.First();
        Assert.Equal("General", generalChunk.DocumentDomain);

        // General 도메인은 ContextualHeader에 포함되지 않음 (기본값이므로)
        if (generalChunk.ContextualHeader != null)
        {
            Assert.DoesNotContain("Domain: General", generalChunk.ContextualHeader);
        }

        _output.WriteLine($"General Content ContextualHeader: {generalChunk.ContextualHeader ?? "null"}");
        _output.WriteLine($"General Domain: {generalChunk.DocumentDomain}");
    }

    [Fact]
    public async Task ContextualHeader_ConsistencyAcrossChunks_InSameDocument()
    {
        // Arrange
        var content = @"
# Technical API Guide

REST API endpoints for authentication and user management.

## Code Examples

```python
def authenticate_user(username, password):
    return auth_service.verify_credentials(username, password)
```

## Database Integration

The system uses PostgreSQL for data persistence with Redis for caching.
";

        // Act
        var chunks = await ProcessContent(content);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Length > 1, "Should generate multiple chunks");

        // 모든 청크가 Technical 도메인으로 분류되어야 함
        Assert.All(chunks, chunk => Assert.Equal("Technical", chunk.DocumentDomain));

        // 각 청크의 ContextualHeader 검증
        foreach (var chunk in chunks.Take(3)) // 처음 3개만 확인
        {
            Assert.NotNull(chunk.ContextualHeader);
            Assert.Contains("Domain: Technical", chunk.ContextualHeader);
            _output.WriteLine($"Chunk {chunk.ChunkIndex}: {chunk.ContextualHeader}");
        }
    }

    private async Task<DocumentChunk[]> ProcessContent(string content)
    {
        // 임시 파일 생성
        var tempFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempFile, content);

        try
        {
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = "Intelligent",
                MaxChunkSize = 200  // 더 작은 청크 크기로 여러 청크 생성 보장
            };

            DocumentChunk[]? chunks = null;

            await foreach (var result in _processor.ProcessWithProgressAsync(
                tempFile, chunkingOptions, new DocumentParsingOptions(), CancellationToken.None))
            {
                if (result.IsSuccess && result.Result != null)
                {
                    chunks = result.Result;
                }
            }

            Assert.NotNull(chunks);
            return chunks;
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}