using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Factories;
using FileFlux.Infrastructure.Services;
using FileFlux.Infrastructure.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests.Performance
{
    public class AutoStrategyPerformanceTests
    {
        private readonly IServiceProvider _serviceProvider;
        
        public AutoStrategyPerformanceTests()
        {
            var services = new ServiceCollection();
            services.AddFileFlux(new MockTextCompletionService());
            _serviceProvider = services.BuildServiceProvider();
        }
        
        [Fact]
        public async Task AutoStrategy_ShouldPerformWithinReasonableTime()
        {
            // Arrange
            var factory = _serviceProvider.GetRequiredService<IChunkingStrategyFactory>();
            var testDocument = GenerateTestDocument(1000);
            var documentContent = new DocumentContent
            {
                Content = testDocument,
                Metadata = new DocumentMetadata
                {
                    FileName = "test-document.md",
                    FileType = "Markdown"
                }
            };
            
            var options = new ChunkingOptions
            {
                MaxChunkSize = 512,
                OverlapSize = 64
            };
            
            // Act & Assert - Auto 전략 테스트
            var autoStrategy = factory.CreateStrategy("Auto");
            Assert.NotNull(autoStrategy);
            
            var autoStopwatch = Stopwatch.StartNew();
            var autoChunks = await autoStrategy.ChunkAsync(documentContent, options);
            autoStopwatch.Stop();
            
            // Smart 전략과 비교
            var smartStrategy = factory.CreateStrategy("Smart");
            var smartStopwatch = Stopwatch.StartNew();
            var smartChunks = await smartStrategy.ChunkAsync(documentContent, options);
            smartStopwatch.Stop();
            
            // Assertions
            Assert.NotEmpty(autoChunks);
            Assert.NotEmpty(smartChunks);
            
            // Auto 전략이 5초 이내에 완료되어야 함
            Assert.True(autoStopwatch.ElapsedMilliseconds < 5000, 
                $"Auto strategy took {autoStopwatch.ElapsedMilliseconds}ms, should be under 5000ms");
            
            // Auto와 Smart의 성능 차이가 10배를 넘지 않아야 함
            var performanceRatio = (double)autoStopwatch.ElapsedMilliseconds / smartStopwatch.ElapsedMilliseconds;
            Assert.True(performanceRatio < 10, 
                $"Auto strategy is {performanceRatio:F2}x slower than Smart, should be under 10x");
            
            // 결과 품질 검증
            Assert.Equal(autoChunks.Count(), smartChunks.Count(), 1); // ±1개 차이 허용
            
            // Auto 전략 메타데이터 확인
            var firstChunk = autoChunks.First();
            if (firstChunk.Properties.ContainsKey("AutoSelectedStrategy"))
            {
                var selectedStrategy = firstChunk.Properties["AutoSelectedStrategy"];
                Assert.NotNull(selectedStrategy);
                Assert.NotEmpty(selectedStrategy.ToString());
            }
        }
        
        [Fact]
        public async Task AutoStrategy_ShouldSelectAppropriateStrategyBasedOnContent()
        {
            // Arrange
            var factory = _serviceProvider.GetRequiredService<IChunkingStrategyFactory>();
            var options = new ChunkingOptions { MaxChunkSize = 512, OverlapSize = 64 };
            
            // 코드 블록이 포함된 기술 문서
            var technicalDoc = new DocumentContent
            {
                Content = @"# API 가이드
                
```csharp
var processor = serviceProvider.GetRequiredService<IDocumentProcessor>();
var chunks = await processor.ProcessAsync(""document.pdf"");
```

## 테이블 예제

| 전략 | 속도 | 품질 |
|------|------|------|
| Smart | 중간 | 높음 |
| Auto | 빠름 | 높음 |

### 리스트
- 첫 번째 항목
- 두 번째 항목",
                Metadata = new DocumentMetadata
                {
                    FileName = "api-guide.md",
                    FileType = "Markdown"
                }
            };
            
            // Act
            var autoStrategy = factory.CreateStrategy("Auto");
            var chunks = await autoStrategy.ChunkAsync(technicalDoc, options);
            
            // Assert
            Assert.NotEmpty(chunks);
            var firstChunk = chunks.First();
            
            // Auto가 전략을 선택했는지 확인
            Assert.True(firstChunk.Properties.ContainsKey("AutoSelectedStrategy"),
                "Auto strategy should record which strategy was selected");
            
            var selectedStrategy = firstChunk.Properties["AutoSelectedStrategy"]?.ToString();
            Assert.NotNull(selectedStrategy);
            
            // 기술 문서이므로 Intelligent나 Smart 전략이 선택되어야 함
            Assert.True(selectedStrategy == "Intelligent" || selectedStrategy == "Smart",
                $"For technical documents, should select Intelligent or Smart, but selected: {selectedStrategy}");
        }
        
        private static string GenerateTestDocument(int wordCount)
        {
            var paragraphs = new[]
            {
                "FileFlux는 순수 RAG 전처리 SDK입니다. 문서를 고품질 청크로 변환하는데 특화되어 있습니다.",
                "# 주요 기능\\nFileFlux는 8가지 문서 형식을 지원합니다. PDF, DOCX, XLSX, PPTX, Markdown, 텍스트, JSON, CSV 파일을 처리할 수 있습니다.",
                "## 청킹 전략\\n4가지 청킹 전략을 제공합니다:\\n- **Intelligent**: 구조 인식 청킹\\n- **Smart**: 70% 완성도 보장\\n- **Semantic**: 의미적 경계 보존\\n- **FixedSize**: 일정한 크기",
                "```csharp\\nvar processor = serviceProvider.GetRequiredService<IDocumentProcessor>();\\nvar chunks = await processor.ProcessAsync(\\\"document.pdf\\\");\\n```",
                "### 성능 특징\\nFileFlux는 메모리 효율적으로 설계되었습니다. 대용량 파일도 스트리밍으로 처리할 수 있습니다.",
                "| 전략 | 속도 | 품질 | 특징 |\\n|------|------|------|------|\\n| Intelligent | 중간 | 높음 | 구조보존 |\\n| Smart | 중간 | 최고 | 완성도보장 |"
            };
            
            var words = 0;
            var result = "";
            var random = new Random(42);
            
            while (words < wordCount)
            {
                var paragraph = paragraphs[random.Next(paragraphs.Length)];
                result += paragraph + "\\n\\n";
                words += paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }
            
            return result;
        }
    }
}