using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Strategies;
using FileFlux.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests
{
    /// <summary>
    /// 테이블 청킹 문제를 TDD 방식으로 해결하기 위한 테스트 클래스
    /// </summary>
    public class TableChunkingTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ProgressiveDocumentProcessor _processor;

        public TableChunkingTests(ITestOutputHelper output)
        {
            _output = output;

            // 기존 통합 테스트와 동일한 설정 사용
            var readerFactory = new DocumentReaderFactory();
            readerFactory.RegisterReader(new Infrastructure.Readers.TextDocumentReader());

            var mockTextCompletionService = new MockTextCompletionService();
            var parserFactory = new DocumentParserFactory(mockTextCompletionService);
            var chunkingFactory = new ChunkingStrategyFactory();

            chunkingFactory.RegisterStrategy(() => new IntelligentChunkingStrategy());

            var logger = new LoggerFactory().CreateLogger<ProgressiveDocumentProcessor>();
            _processor = new ProgressiveDocumentProcessor(readerFactory, parserFactory, chunkingFactory, logger);
        }

        [Fact]
        public async Task ProcessMarkdownTable_LongTableRow_ShouldNotSplitInMiddle()
        {
            // Arrange: FR-002와 같은 긴 테이블 행 테스트
            var markdownContent = @"## 기능 요구사항

| 요구사항이름 | 설명 | 제약사항 | 전제조건(검토사항) | NFR |
|-------------|------|----------|-------------------|-----|
| **FR-001: 문서 업로드** | 웹 UI를 통해 PDF, TXT, MD 파일을 단일 업로드 | • 파일 크기 50MB 이하<br>• 지원 포맷: PDF, TXT, MD만<br>• 멀티파일 업로드 미지원 | • 파일 업로드 크기 제한 설정<br>• 파일 형식 검증 로직<br>• 임시 저장소 경로 설정 | • 업로드 시간 30초 이하<br>• 동시 업로드 10건 처리 |
| **FR-002: 문서 벡터화** | GPUStack 임베딩 API를 통한 문서 벡터화 및 Qdrant 저장 | • GPUStack 임베딩 모델 의존성<br>• OpenAI 호환 임베딩 API 사용<br>• 청킹 단위 512토큰 고정 | • GPUStack에 임베딩 모델 배포<br>• 문서 청킹 전략 수립<br>• 벡터 차원 수 결정 | • 벡터화 처리 시간 문서당 10초 이하<br>• 벡터 검색 응답시간 2초 이하 |
| **FR-003: 문서 상태 관리** | 문서 처리 상태를 실시간 표시 (업로드/처리중/완료/오류) | • 상태 정보 DB 저장 필요<br>• 실시간 UI 업데이트<br>• 상태 전이 규칙 고정 | • 상태 관리 데이터 모델 설계<br>• 실시간 통신 방식 구현<br>• 오류 처리 및 재시도 로직 | • 상태 업데이트 지연 5초 이하 |";

            _output.WriteLine($"전체 마크다운 콘텐츠 길이: {markdownContent.Length}자");

            var tempFilePath = Path.GetTempFileName();
            tempFilePath = Path.ChangeExtension(tempFilePath, ".md");
            await File.WriteAllTextAsync(tempFilePath, markdownContent);

            try
            {
                var options = new ChunkingOptions
                {
                    Strategy = "Intelligent",
                    MaxChunkSize = 400,
                    OverlapSize = 60,
                    PreserveStructure = true
                };

                // Act
                DocumentChunk[]? finalResult = null;
                var parsingOptions = new DocumentParsingOptions
                {
                    UseLlm = true,
                    StructuringLevel = StructuringLevel.Medium
                };

                await foreach (var result in _processor.ProcessWithProgressAsync(tempFilePath, options, parsingOptions, CancellationToken.None))
                {
                    if (result.IsSuccess && result.Result != null)
                    {
                        finalResult = result.Result;
                    }
                }

                Assert.NotNull(finalResult);
                var chunks = finalResult.ToList();

                // Assert - 테이블 행이 중간에 분할되지 않아야 함
                foreach (var chunk in chunks)
                {
                    var content = chunk.Content;

                    _output.WriteLine($"청크 내용: {content.Replace("\n", "\\n")}");

                    // FR-002 행이 중간에 잘리면 안됨
                    if (content.Contains("**FR-002: 문서 벡터화**"))
                    {
                        _output.WriteLine($"FR-002 청크 발견: {content.Length}자");
                        _output.WriteLine($"FR-002 청크 전체 내용:\n{content}");

                        // 이 청크는 FR-002의 완전한 행을 포함해야 함
                        Assert.Contains("GPUStack 임베딩 모델 의존성", content);
                        Assert.Contains("OpenAI 호환 임베딩 API 사용", content);
                        Assert.Contains("청킹 단위 512토큰 고정", content);
                        Assert.Contains("벡터화 처리 시간 문서당 10초 이하", content);
                        Assert.Contains("벡터 검색 응답시간 2초 이하", content);

                        _output.WriteLine($"FR-002 청크 확인됨: {content.Length}자");
                    }

                    // 테이블 행 중간 분할 감지
                    Assert.False(content.TrimStart().StartsWith("의존성<br>•"),
                        $"테이블 행이 중간에 분할됨: '{content.Substring(0, Math.Min(100, content.Length))}...'");

                    Assert.False(content.TrimStart().StartsWith("사용<br>•"),
                        "테이블 행이 중간에 분할됨");

                    Assert.False(content.TrimEnd().EndsWith("• GPUStack 임베딩 모델"),
                        "테이블 행이 중간에 분할됨");
                }

                _output.WriteLine($"총 {chunks.Count}개 청크 생성됨");
                for (int i = 0; i < chunks.Count; i++)
                {
                    var preview = chunks[i].Content.Replace("\n", "\\n").Substring(0, Math.Min(100, chunks[i].Content.Length));
                    _output.WriteLine($"청크 {i + 1}: {chunks[i].Content.Length}자 - {preview}...");
                }
            }
            finally
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }

        [Fact]
        public async Task ProcessMarkdownTable_CompleteTable_ShouldPreserveAsOneChunk()
        {
            // Arrange: 작은 테이블이 하나의 청크로 보존되는지 테스트
            var markdownContent = @"## 작은 테이블 테스트

| 이름 | 값 | 설명 |
|------|----|----- |
| 항목1 | 값1 | 설명1 |
| 항목2 | 값2 | 설명2 |

다른 내용";

            var tempFilePath = Path.GetTempFileName();
            tempFilePath = Path.ChangeExtension(tempFilePath, ".md");
            await File.WriteAllTextAsync(tempFilePath, markdownContent);

            try
            {
                var options = new ChunkingOptions
                {
                    Strategy = "Intelligent",
                    MaxChunkSize = 400,
                    OverlapSize = 60,
                    PreserveStructure = true
                };

                // Act
                DocumentChunk[]? finalResult = null;
                var parsingOptions = new DocumentParsingOptions
                {
                    UseLlm = true,
                    StructuringLevel = StructuringLevel.Medium
                };

                await foreach (var result in _processor.ProcessWithProgressAsync(tempFilePath, options, parsingOptions, CancellationToken.None))
                {
                    if (result.IsSuccess && result.Result != null)
                    {
                        finalResult = result.Result;
                    }
                }

                Assert.NotNull(finalResult);
                var chunks = finalResult.ToList();

                // Assert - 작은 테이블은 하나의 청크에 완전히 포함되어야 함
                var tableChunk = chunks.FirstOrDefault(c => c.Content.Contains("| 이름 | 값 | 설명 |"));
                Assert.NotNull(tableChunk);

                // 테이블 전체 구조가 포함되어야 함
                Assert.Contains("| 이름 | 값 | 설명 |", tableChunk.Content);
                Assert.Contains("|------|----|----- |", tableChunk.Content);
                Assert.Contains("| 항목1 | 값1 | 설명1 |", tableChunk.Content);
                Assert.Contains("| 항목2 | 값2 | 설명2 |", tableChunk.Content);

                _output.WriteLine($"테이블 청크 확인: {tableChunk.Content.Length}자");
                _output.WriteLine($"테이블 내용: {tableChunk.Content}");
            }
            finally
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }

        [Fact]
        public async Task TableRowSplit_ShouldNotOccur_DetectedByKeywords()
        {
            // Arrange: 단순화된 테스트 - 키워드로 테이블 분할 감지
            var markdownContent = @"| **FR-002: 문서 벡터화** | GPUStack 임베딩 API를 통한 문서 벡터화 및 Qdrant 저장 | • GPUStack 임베딩 모델 의존성<br>• OpenAI 호환 임베딩 API 사용<br>• 청킹 단위 512토큰 고정 | • GPUStack에 임베딩 모델 배포<br>• 문서 청킹 전략 수립<br>• 벡터 차원 수 결정 | • 벡터화 처리 시간 문서당 10초 이하<br>• 벡터 검색 응답시간 2초 이하 |";

            var tempFilePath = Path.GetTempFileName();
            tempFilePath = Path.ChangeExtension(tempFilePath, ".md");
            await File.WriteAllTextAsync(tempFilePath, markdownContent);

            try
            {
                var options = new ChunkingOptions
                {
                    Strategy = "Intelligent",
                    MaxChunkSize = 300, // 작게 설정하여 분할 유도
                    OverlapSize = 30,
                    PreserveStructure = true
                };

                // Act
                DocumentChunk[]? finalResult = null;
                var parsingOptions = new DocumentParsingOptions
                {
                    UseLlm = true,
                    StructuringLevel = StructuringLevel.Medium
                };

                await foreach (var result in _processor.ProcessWithProgressAsync(tempFilePath, options, parsingOptions, CancellationToken.None))
                {
                    if (result.IsSuccess && result.Result != null)
                    {
                        finalResult = result.Result;
                    }
                }

                Assert.NotNull(finalResult);
                var chunks = finalResult.ToList();

                // Assert - 테이블 행이 중간에 분할되면 안됨 (현재 실패할 테스트)
                bool hasIncompleteFR002 = false;
                bool hasPartialContent = false;

                foreach (var chunk in chunks)
                {
                    var content = chunk.Content.Trim();

                    // "의존성<br>•"으로 시작하면 테이블 중간 분할이 발생한 것
                    if (content.StartsWith("의존성<br>•"))
                    {
                        hasPartialContent = true;
                        _output.WriteLine($"테이블 중간 분할 감지됨: {content.Substring(0, Math.Min(100, content.Length))}...");
                    }

                    // FR-002가 포함된 청크가 불완전하면 분할 발생
                    if (content.Contains("**FR-002: 문서 벡터화**") && !content.Contains("벡터 검색 응답시간 2초 이하"))
                    {
                        hasIncompleteFR002 = true;
                        _output.WriteLine($"FR-002 불완전 청크 감지됨: {content.Length}자");
                    }
                }

                // 현재 구현에서는 이 테스트가 실패할 것임 (의도된 실패)
                Assert.False(hasPartialContent, "테이블 행이 중간에 분할되었습니다");
                Assert.False(hasIncompleteFR002, "FR-002 테이블 행이 불완전합니다");

                _output.WriteLine($"총 {chunks.Count}개 청크 생성됨");
            }
            finally
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }
    }
}