using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Quality;
using FileFlux.SampleApp.Data;
using FileFlux.SampleApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FileFlux.SampleApp;

/// <summary>
/// FileFlux 데모 애플리케이션 메인 로직
/// </summary>
public class FileFluxApp
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IVectorStoreService _vectorStore;
    private readonly FileFluxDbContext _context;
    private readonly ILogger<FileFluxApp> _logger;

    public FileFluxApp(
        IDocumentProcessor documentProcessor,
        IVectorStoreService vectorStore,
        FileFluxDbContext context,
        ILogger<FileFluxApp> logger)
    {
        _documentProcessor = documentProcessor ?? throw new ArgumentNullException(nameof(documentProcessor));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 문서 처리 및 저장
    /// </summary>
    public async Task ProcessDocumentAsync(string filePath, string strategy)
    {
        try
        {
            // 데이터베이스 초기화
            await _context.Database.EnsureCreatedAsync();

            Console.WriteLine($"📄 문서 처리 시작: {filePath}");
            Console.WriteLine($"📋 청킹 전략: {strategy}");

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {filePath}");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            // 청킹 옵션 설정
            var chunkingOptions = new ChunkingOptions
            {
                Strategy = strategy switch
                {
                    "FixedSize" => ChunkingStrategies.FixedSize,
                    "Semantic" => ChunkingStrategies.Semantic,
                    "Paragraph" => ChunkingStrategies.Paragraph,
                    "Intelligent" or _ => ChunkingStrategies.Intelligent
                },
                MaxChunkSize = 500,
                OverlapSize = 50
            };

            // 기본 문서 처리 - 새로운 스트리밍 API 사용
            Console.WriteLine("📋 기본 문서 처리");
            var chunks = await _documentProcessor.ProcessAsync(filePath, chunkingOptions);
            Console.WriteLine($"✅ 청크 생성 완료: {chunks.Length}개 청크");

            // 벡터 스토어에 저장
            var document = await _vectorStore.StoreDocumentAsync(filePath, chunks, strategy);

            stopwatch.Stop();

            Console.WriteLine($"💾 데이터베이스 저장 완료");
            Console.WriteLine($"📊 처리 결과:");
            Console.WriteLine($"   - 문서 ID: {document.Id}");
            Console.WriteLine($"   - 파일 크기: {document.FileSize:N0} bytes");
            Console.WriteLine($"   - 청크 수: {document.ChunkCount}");
            Console.WriteLine($"   - 처리 시간: {stopwatch.Elapsed:mm\\:ss\\.fff}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "문서 처리 중 오류 발생: {FilePath}", filePath);
            Console.WriteLine($"❌ 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// RAG 쿼리 실행
    /// </summary>
    public async Task ExecuteQueryAsync(string query, int topK)
    {
        try
        {
            Console.WriteLine($"🔍 쿼리 실행: {query}");
            Console.WriteLine($"📊 반환 결과 수: {topK}");
            Console.WriteLine();

            var stopwatch = Stopwatch.StartNew();

            // RAG 쿼리 실행
            var result = await _vectorStore.ExecuteRagQueryAsync(query, topK);

            stopwatch.Stop();

            Console.WriteLine("🤖 AI 응답:");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine(result.Response);
            Console.WriteLine(new string('=', 50));
            Console.WriteLine();

            Console.WriteLine($"⏱️ 응답 시간: {result.ResponseTimeMs}ms");
            Console.WriteLine($"📋 쿼리 ID: {result.Id}");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "쿼리 실행 중 오류 발생: {Query}", query);
            Console.WriteLine($"❌ 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 저장된 문서 목록 조회
    /// </summary>
    public async Task ListDocumentsAsync()
    {
        try
        {
            Console.WriteLine("📚 저장된 문서 목록:");
            Console.WriteLine();

            var documents = await _vectorStore.GetDocumentsAsync();

            if (!documents.Any())
            {
                Console.WriteLine("📭 저장된 문서가 없습니다.");
                Console.WriteLine("먼저 'process' 명령으로 문서를 처리해주세요.");
                return;
            }

            Console.WriteLine($"{"ID",-5} {"파일명",-40} {"청킹전략",-12} {"청크수",-8} {"처리일시",-20}");
            Console.WriteLine(new string('-', 90));

            foreach (var doc in documents)
            {
                var fileName = Path.GetFileName(doc.FilePath);
                Console.WriteLine($"{doc.Id,-5} {fileName,-40} {doc.ChunkingStrategy,-12} {doc.ChunkCount,-8} {doc.ProcessedAt:yyyy-MM-dd HH:mm}");
            }

            Console.WriteLine();
            Console.WriteLine($"📊 총 {documents.Count()}개 문서, {documents.Sum(d => d.ChunkCount)}개 청크");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "문서 목록 조회 중 오류 발생");
            Console.WriteLine($"❌ 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 쿼리 기록 조회
    /// </summary>
    public async Task ShowQueryHistoryAsync(int limit)
    {
        try
        {
            Console.WriteLine($"🔍 최근 쿼리 기록 (최대 {limit}개):");
            Console.WriteLine();

            var queries = await _vectorStore.GetQueryHistoryAsync(limit);

            if (!queries.Any())
            {
                Console.WriteLine("📭 쿼리 기록이 없습니다.");
                Console.WriteLine("먼저 'query' 명령으로 검색을 해보세요.");
                return;
            }

            foreach (var query in queries)
            {
                Console.WriteLine($"[{query.QueryTime:yyyy-MM-dd HH:mm:ss}] (ID: {query.Id})");
                Console.WriteLine($"❓ 쿼리: {query.Query}");
                Console.WriteLine($"⏱️ 응답시간: {query.ResponseTimeMs}ms");
                Console.WriteLine($"🤖 응답: {TruncateString(query.Response ?? "없음", 100)}");
                Console.WriteLine(new string('-', 50));
                Console.WriteLine();
            }

            Console.WriteLine($"📊 총 {queries.Count()}개 쿼리 기록");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "쿼리 기록 조회 중 오류 발생");
            Console.WriteLine($"❌ 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 전문 문서 리더 테스트 (OpenXML, EPPlus, PdfPig, HtmlAgilityPack)
    /// </summary>
    public async Task TestProfessionalReadersAsync(string folderPath)
    {
        try
        {
            await _context.Database.EnsureCreatedAsync();

            Console.WriteLine("🧪 전문 문서 리더 테스트 시작");
            Console.WriteLine($"📁 테스트 폴더: {folderPath}");
            Console.WriteLine();

            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine($"❌ 폴더를 찾을 수 없습니다: {folderPath}");
                return;
            }

            // 지원하는 전문 문서 형식들
            var supportedExtensions = new[]
            {
                ".docx", ".docm",  // Word (OpenXML)
                ".xlsx", ".xlsm",  // Excel (EPPlus)
                ".pdf",            // PDF (PdfPig)
                ".html", ".htm"    // HTML (HtmlAgilityPack)
            };

            // 폴더에서 지원되는 파일들 찾기
            var testFiles = Directory.GetFiles(folderPath)
                .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                .ToList();

            if (testFiles.Count == 0)
            {
                Console.WriteLine($"❌ 지원되는 문서 파일을 찾을 수 없습니다.");
                Console.WriteLine($"지원 형식: {string.Join(", ", supportedExtensions)}");
                return;
            }

            Console.WriteLine($"🔍 발견된 테스트 파일: {testFiles.Count}개");
            foreach (var file in testFiles)
            {
                var ext = Path.GetExtension(file);
                var library = ext switch
                {
                    ".docx" or ".docm" => "OpenXML",
                    ".xlsx" or ".xlsm" => "EPPlus",
                    ".pdf" => "PdfPig",
                    ".html" or ".htm" => "HtmlAgilityPack",
                    _ => "Unknown"
                };
                Console.WriteLine($"   📄 {Path.GetFileName(file)} ({library})");
            }
            Console.WriteLine();

            int successCount = 0;
            int totalProcessed = 0;

            foreach (var filePath in testFiles)
            {
                totalProcessed++;
                var fileName = Path.GetFileName(filePath);
                var extension = Path.GetExtension(filePath);

                Console.WriteLine($"[{totalProcessed}/{testFiles.Count}] 테스트 중: {fileName}");

                try
                {
                    var stopwatch = Stopwatch.StartNew();

                    // 기본 문서 처리로 테스트
                    var chunkingOptions = new ChunkingOptions
                    {
                        Strategy = ChunkingStrategies.Intelligent,
                        MaxChunkSize = 500,
                        OverlapSize = 50
                    };

                    var chunks = await _documentProcessor.ProcessAsync(filePath, chunkingOptions);
                    stopwatch.Stop();

                    Console.WriteLine($"   ✅ 성공: {chunks.Length}개 청크 생성 ({stopwatch.Elapsed:mm\\:ss\\.fff})");

                    // 첫 번째 청크 미리보기 표시
                    if (chunks.Length > 0)
                    {
                        var firstChunk = chunks[0];
                        var preview = firstChunk.Content.Length > 100
                            ? string.Concat(firstChunk.Content.AsSpan(0, 100), "...")
                            : firstChunk.Content;
                        Console.WriteLine($"   📝 미리보기: {preview.Replace('\n', ' ').Replace('\r', ' ')}");
                    }

                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ 실패: {ex.Message}");
                    _logger.LogWarning(ex, "문서 리더 테스트 실패: {FilePath}", filePath);
                }

                Console.WriteLine();
            }

            // 결과 요약
            Console.WriteLine("📊 전문 문서 리더 테스트 결과:");
            Console.WriteLine($"   - 총 테스트 파일: {totalProcessed}개");
            Console.WriteLine($"   - 성공: {successCount}개 ({(double)successCount / totalProcessed:P1})");
            Console.WriteLine($"   - 실패: {totalProcessed - successCount}개");
            Console.WriteLine();

            if (successCount == totalProcessed)
            {
                Console.WriteLine("🎉 모든 전문 문서 리더가 정상 동작합니다!");
            }
            else if (successCount > 0)
            {
                Console.WriteLine("⚠️ 일부 문서 리더에 문제가 있을 수 있습니다.");
            }
            else
            {
                Console.WriteLine("❌ 전문 문서 리더에 심각한 문제가 있습니다.");
            }

            Console.WriteLine();
            Console.WriteLine("💡 사용된 전문 라이브러리:");
            Console.WriteLine("   - OpenXML: Word 문서 (.docx, .docm)");
            Console.WriteLine("   - EPPlus: Excel 문서 (.xlsx, .xlsm)");
            Console.WriteLine("   - PdfPig: PDF 문서 (.pdf)");
            Console.WriteLine("   - HtmlAgilityPack: HTML 문서 (.html, .htm)");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "전문 문서 리더 테스트 중 오류 발생: {FolderPath}", folderPath);
            Console.WriteLine($"❌ 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 스트리밍 방식으로 문서를 처리합니다 (새로운 간소화된 API 사용)
    /// </summary>
    public async Task ProcessDocumentStreamAsync(string filePath, string strategy)
    {
        try
        {
            // 데이터베이스 초기화
            await _context.Database.EnsureCreatedAsync();

            Console.WriteLine($"🚀 스트리밍 문서 처리 시작: {filePath}");
            Console.WriteLine($"📋 청킹 전략: {strategy}");
            Console.WriteLine();

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {filePath}");
                return;
            }

            var chunkingOptions = new ChunkingOptions
            {
                Strategy = strategy switch
                {
                    "FixedSize" => ChunkingStrategies.FixedSize,
                    "Semantic" => ChunkingStrategies.Semantic,
                    "Paragraph" => ChunkingStrategies.Paragraph,
                    "Intelligent" or _ => ChunkingStrategies.Intelligent
                },
                MaxChunkSize = 500,
                OverlapSize = 50
            };

            var chunkList = new List<DocumentChunk>();
            var chunkCount = 0;

            Console.WriteLine("📊 청크 처리 중...");

            var stopwatch = Stopwatch.StartNew();
            
            // 문서 처리
            var chunks = await _documentProcessor.ProcessAsync(filePath, chunkingOptions);
            chunkCount = chunks.Length;

            stopwatch.Stop();
            Console.WriteLine($"\r✅ 처리 완료! 총 {chunkCount}개 청크 생성");
            Console.WriteLine($"⏱️ 처리 시간: {stopwatch.Elapsed:mm\\:ss\\.fff}");

            // 벡터 스토어에 저장
            var document = await _vectorStore.StoreDocumentAsync(filePath, chunks, strategy);

            // 결과 요약 출력
            Console.WriteLine("\n📊 처리 결과 요약:");
            Console.WriteLine($"   📁 파일: {Path.GetFileName(filePath)}");
            Console.WriteLine($"   📏 파일 크기: {new FileInfo(filePath).Length:N0} bytes");
            Console.WriteLine($"   🔢 총 청크: {chunks.Length}개");
            Console.WriteLine($"   📝 총 문자: {chunks.Sum(c => c.Content.Length):N0}자");
            Console.WriteLine($"   📊 평균 청크 크기: {chunks.Average(c => c.Content.Length):N0}자");
            Console.WriteLine($"   🆔 문서 ID: {document.Id}");

            // 첫 번째 청크 미리보기
            if (chunks.Length > 0)
            {
                var firstChunk = chunks[0];
                var preview = firstChunk.Content.Length > 200
                    ? string.Concat(firstChunk.Content.AsSpan(0, 200), "...")
                    : firstChunk.Content;

                Console.WriteLine($"\n📄 첫 번째 청크 미리보기:");
                Console.WriteLine($"   {preview.Replace('\n', ' ').Replace('\r', ' ')}");
            }

            Console.WriteLine($"\n✅ 문서가 성공적으로 처리되어 저장되었습니다!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "진행률 추적 문서 처리 중 오류 발생: {FilePath}", filePath);
            Console.WriteLine($"\n❌ 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// OpenAI Vision을 사용한 이미지 텍스트 추출 테스트
    /// </summary>
    public async Task TestVisionProcessingAsync(string filePath)
    {
        try
        {
            Console.WriteLine($"🖼️  Vision 테스트 시작: {filePath}");

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {filePath}");
                return;
            }

            var stopwatch = Stopwatch.StartNew();

            // MultiModalPdfDocumentReader를 직접 생성하여 테스트
            var serviceProvider = new ServiceCollection()
                .AddScoped<IImageToTextService>(provider => 
                    new Services.OpenAiImageToTextService(Environment.GetEnvironmentVariable("OPENAI_API_KEY")!))
                .BuildServiceProvider();

            var multiModalReader = new FileFlux.Infrastructure.Readers.MultiModalPdfDocumentReader(serviceProvider);

            Console.WriteLine("📄 PDF 텍스트 + 이미지 추출 중...");
            var rawContent = await multiModalReader.ExtractAsync(filePath);

            stopwatch.Stop();

            Console.WriteLine($"\n✅ Vision 처리 완료!");
            Console.WriteLine($"⏱️ 처리 시간: {stopwatch.Elapsed:mm\\:ss\\.fff}");

            // 결과 분석
            var hasImages = rawContent.Hints?.ContainsKey("HasImages") == true
                         && (bool)(rawContent.Hints["HasImages"]);

            Console.WriteLine("\n📊 Vision 처리 결과:");
            Console.WriteLine($"   📁 파일: {Path.GetFileName(filePath)}");
            Console.WriteLine($"   📏 파일 크기: {new FileInfo(filePath).Length:N0} bytes");
            Console.WriteLine($"   🖼️  이미지 처리: {(hasImages ? "✅ 있음" : "❌ 없음")}");

            if (hasImages && rawContent.Hints != null)
            {
                var imageCount = rawContent.Hints.GetValueOrDefault("ImageCount", 0);
                Console.WriteLine($"   🔢 처리된 이미지: {imageCount}개");

                if (rawContent.Hints.ContainsKey("ImageProcessingResults") &&
                    rawContent.Hints["ImageProcessingResults"] is System.Collections.Generic.List<string> results)
                {
                    Console.WriteLine("   📋 이미지 처리 상세:");
                    foreach (var result in results)
                    {
                        Console.WriteLine($"      - {result}");
                    }
                }
            }

            Console.WriteLine($"   📝 추출된 텍스트 길이: {rawContent.Text.Length:N0}자");

            // 이미지 마커가 포함된 텍스트 미리보기
            var imageStartIndex = rawContent.Text.IndexOf("<!-- IMAGE_START", StringComparison.OrdinalIgnoreCase);
            if (imageStartIndex >= 0)
            {
                Console.WriteLine("\n🖼️  이미지 텍스트 추출 예시:");
                
                var imageEndIndex = rawContent.Text.IndexOf("<!-- IMAGE_END", imageStartIndex, StringComparison.OrdinalIgnoreCase);
                if (imageEndIndex >= 0)
                {
                    imageEndIndex = rawContent.Text.IndexOf("-->", imageEndIndex) + 3;
                    var imageSection = rawContent.Text.Substring(imageStartIndex, imageEndIndex - imageStartIndex);
                    var preview = imageSection.Length > 500 
                        ? string.Concat(imageSection.AsSpan(0, 500), "...")
                        : imageSection;
                    
                    Console.WriteLine($"   {preview}");
                }
            }
            else
            {
                // 일반 텍스트 미리보기
                Console.WriteLine("\n📄 텍스트 미리보기:");
                var preview = rawContent.Text.Length > 300
                    ? string.Concat(rawContent.Text.AsSpan(0, 300), "...")
                    : rawContent.Text;
                Console.WriteLine($"   {preview.Replace('\n', ' ').Replace('\r', ' ')}");
            }

            // 경고사항 출력
            if (rawContent.Warnings?.Any() == true)
            {
                Console.WriteLine("\n⚠️  경고사항:");
                foreach (var warning in rawContent.Warnings)
                {
                    Console.WriteLine($"   - {warning}");
                }
            }

            Console.WriteLine($"\n🎉 Vision 테스트가 성공적으로 완료되었습니다!");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vision 처리 중 오류 발생: {FilePath}", filePath);
            Console.WriteLine($"\n❌ 오류: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   상세: {ex.InnerException.Message}");
            }
        }
    }

    /// <summary>
    /// 문서 품질 분석 실행
    /// </summary>
    public async Task AnalyzeDocumentQualityAsync(string filePath, string strategy, bool benchmark, bool qaGeneration)
    {
        try
        {
            Console.WriteLine("🔬 FileFlux 품질 분석 시스템 데모");
            Console.WriteLine($"📄 대상 파일: {filePath}");
            Console.WriteLine();

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"❌ 파일을 찾을 수 없습니다: {filePath}");
                return;
            }

            // DocumentQualityAnalyzer 생성
            var qualityEngine = new ChunkQualityEngine();
            var qualityAnalyzer = new DocumentQualityAnalyzer(qualityEngine, _documentProcessor);

            if (benchmark)
            {
                await RunQualityBenchmarkAsync(qualityAnalyzer, filePath);
            }
            else
            {
                await RunSingleQualityAnalysisAsync(qualityAnalyzer, filePath, strategy, qaGeneration);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "품질 분석 중 오류 발생: {FilePath}", filePath);
            Console.WriteLine($"❌ 오류: {ex.Message}");
        }
    }

    private async Task RunSingleQualityAnalysisAsync(DocumentQualityAnalyzer analyzer, string filePath, string strategy, bool qaGeneration)
    {
        Console.WriteLine($"📊 단일 전략 품질 분석: {strategy}");
        Console.WriteLine();

        var chunkingOptions = new ChunkingOptions
        {
            Strategy = strategy switch
            {
                "FixedSize" => ChunkingStrategies.FixedSize,
                "Semantic" => ChunkingStrategies.Semantic,
                "Paragraph" => ChunkingStrategies.Paragraph,
                "Intelligent" or _ => ChunkingStrategies.Intelligent
            },
            MaxChunkSize = 512,
            OverlapSize = 64
        };

        var stopwatch = Stopwatch.StartNew();
        
        Console.WriteLine("🔍 문서 품질 분석 실행 중...");
        var qualityReport = await analyzer.AnalyzeQualityAsync(filePath, chunkingOptions);
        
        stopwatch.Stop();

        DisplayQualityReport(qualityReport, stopwatch.Elapsed);

        // QA 벤치마크 생성 (선택사항)
        if (qaGeneration)
        {
            Console.WriteLine("\n🤖 QA 벤치마크 생성 중...");
            var qaBenchmarkStopwatch = Stopwatch.StartNew();
            
            try
            {
                var qaBenchmark = await analyzer.GenerateQABenchmarkAsync(filePath, questionCount: 5);
                qaBenchmarkStopwatch.Stop();
                
                DisplayQABenchmark(qaBenchmark, qaBenchmarkStopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  QA 벤치마크 생성 실패: {ex.Message}");
            }
        }
    }

    private async Task RunQualityBenchmarkAsync(DocumentQualityAnalyzer analyzer, string filePath)
    {
        Console.WriteLine("🏁 다중 전략 품질 벤치마크 실행");
        Console.WriteLine();

        var strategies = new[] { "Intelligent", "Semantic", "FixedSize", "Paragraph" };
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("⚡ 벤치마크 실행 중...");
        var benchmarkResult = await analyzer.BenchmarkChunkingAsync(filePath, strategies);
        
        stopwatch.Stop();

        DisplayBenchmarkResults(benchmarkResult, stopwatch.Elapsed);
    }

    private void DisplayQualityReport(DocumentQualityReport report, TimeSpan processingTime)
    {
        Console.WriteLine("📋 품질 분석 결과");
        Console.WriteLine(new string('=', 60));
        
        Console.WriteLine($"📄 문서: {Path.GetFileName(report.DocumentPath)}");
        Console.WriteLine($"🆔 문서 ID: {report.DocumentId}");
        Console.WriteLine($"⏱️ 분석 시간: {processingTime:mm\\:ss\\.fff}");
        Console.WriteLine($"🎯 전체 품질 점수: {report.OverallQualityScore:P1}");
        Console.WriteLine();

        // 청킹 품질 메트릭
        Console.WriteLine("📦 청킹 품질 메트릭:");
        Console.WriteLine($"   • 평균 완전성: {report.ChunkingQuality.AverageCompleteness:P1}");
        Console.WriteLine($"   • 내용 일관성: {report.ChunkingQuality.ContentConsistency:P1}");
        Console.WriteLine($"   • 경계 품질: {report.ChunkingQuality.BoundaryQuality:P1}");
        Console.WriteLine($"   • 크기 분산: {report.ChunkingQuality.SizeDistribution:P1}");
        Console.WriteLine($"   • 중복 효과성: {report.ChunkingQuality.OverlapEffectiveness:P1}");
        Console.WriteLine();

        // 정보 밀도 메트릭
        Console.WriteLine("📊 정보 밀도 메트릭:");
        Console.WriteLine($"   • 평균 정보 밀도: {report.InformationDensity.AverageInformationDensity:P1}");
        Console.WriteLine($"   • 키워드 풍부성: {report.InformationDensity.KeywordRichness:P1}");
        Console.WriteLine($"   • 사실적 내용 비율: {report.InformationDensity.FactualContentRatio:P1}");
        Console.WriteLine($"   • 중복성 수준: {report.InformationDensity.RedundancyLevel:P1}");
        Console.WriteLine();

        // 구조적 일관성 메트릭
        Console.WriteLine("🏗️ 구조적 일관성:");
        Console.WriteLine($"   • 구조 보존: {report.StructuralCoherence.StructurePreservation:P1}");
        Console.WriteLine($"   • 맥락 연속성: {report.StructuralCoherence.ContextContinuity:P1}");
        Console.WriteLine($"   • 참조 무결성: {report.StructuralCoherence.ReferenceIntegrity:P1}");
        Console.WriteLine($"   • 메타데이터 풍부성: {report.StructuralCoherence.MetadataRichness:P1}");
        Console.WriteLine();

        // 개선 권장사항
        if (report.Recommendations.Any())
        {
            Console.WriteLine("💡 개선 권장사항:");
            foreach (var recommendation in report.Recommendations.Take(3))
            {
                Console.WriteLine($"   • {recommendation.Description} (우선도: {recommendation.Priority})");
            }
            Console.WriteLine();
        }
    }

    private void DisplayQABenchmark(QABenchmark qaBenchmark, TimeSpan processingTime)
    {
        Console.WriteLine("🤖 QA 벤치마크 결과");
        Console.WriteLine(new string('=', 60));
        
        Console.WriteLine($"📄 문서: {Path.GetFileName(qaBenchmark.DocumentPath)}");
        Console.WriteLine($"🆔 문서 ID: {qaBenchmark.DocumentId}");
        Console.WriteLine($"⏱️ 생성 시간: {processingTime:mm\\:ss\\.fff}");
        Console.WriteLine($"🎯 답변 가능성 점수: {qaBenchmark.AnswerabilityScore:P1}");
        Console.WriteLine();

        Console.WriteLine($"❓ 생성된 질문 수: {qaBenchmark.Questions.Count}개");
        Console.WriteLine();

        // 질문 타입별 분포
        var questionsByType = qaBenchmark.Questions.GroupBy(q => q.Type).ToList();
        Console.WriteLine("📊 질문 유형 분포:");
        foreach (var group in questionsByType)
        {
            Console.WriteLine($"   • {group.Key}: {group.Count()}개");
        }
        Console.WriteLine();

        // 샘플 질문들
        Console.WriteLine("💬 샘플 질문들:");
        var sampleQuestions = qaBenchmark.Questions.Take(3);
        foreach (var (question, index) in sampleQuestions.Select((q, i) => (q, i + 1)))
        {
            Console.WriteLine($"   {index}. [{question.Type}] {question.Question}");
            Console.WriteLine($"      💡 예상 답변: {TruncateString(question.ExpectedAnswer, 100)}");
            Console.WriteLine($"      🎯 난이도: {question.DifficultyScore:P1}, 신뢰도: {question.ConfidenceScore:P1}");
            Console.WriteLine();
        }

        // 검증 결과
        if (qaBenchmark.ValidationResult != null)
        {
            var validation = qaBenchmark.ValidationResult;
            Console.WriteLine("✅ 답변 가능성 검증:");
            Console.WriteLine($"   • 총 질문: {validation.TotalQuestions}개");
            Console.WriteLine($"   • 답변 가능: {validation.AnswerableQuestions}개");
            Console.WriteLine($"   • 답변 가능 비율: {validation.AnswerabilityRatio:P1}");
            Console.WriteLine($"   • 평균 신뢰도: {validation.AverageConfidence:P1}");
        }
        
        Console.WriteLine();
    }

    private void DisplayBenchmarkResults(QualityBenchmarkResult benchmarkResult, TimeSpan processingTime)
    {
        Console.WriteLine("🏁 청킹 전략 벤치마크 결과");
        Console.WriteLine(new string('=', 60));
        
        Console.WriteLine($"📄 문서: {Path.GetFileName(benchmarkResult.QualityReports.First().DocumentPath)}");
        Console.WriteLine($"⏱️ 총 벤치마크 시간: {processingTime:mm\\:ss\\.fff}");
        Console.WriteLine($"🏆 권장 전략: {benchmarkResult.RecommendedStrategy}");
        Console.WriteLine();

        // 전략별 결과 표시
        Console.WriteLine("📊 전략별 품질 비교:");
        Console.WriteLine($"{"전략",-12} {"전체점수",-10} {"완전성",-8} {"일관성",-8} {"경계품질",-10}");
        Console.WriteLine(new string('-', 50));

        foreach (var report in benchmarkResult.QualityReports.OrderByDescending(r => r.OverallQualityScore))
        {
            var strategy = report.ProcessingOptions?.Strategy ?? "Unknown";
            Console.WriteLine($"{strategy,-12} {report.OverallQualityScore,-10:P1} {report.ChunkingQuality.AverageCompleteness,-8:P1} " +
                            $"{report.ChunkingQuality.ContentConsistency,-8:P1} {report.ChunkingQuality.BoundaryQuality,-10:P1}");
        }
        Console.WriteLine();

        // 비교 메트릭
        if (benchmarkResult.ComparisonMetrics.Any())
        {
            Console.WriteLine("📈 비교 메트릭:");
            foreach (var (key, value) in benchmarkResult.ComparisonMetrics)
            {
                Console.WriteLine($"   • {key}: {value}");
            }
            Console.WriteLine();
        }

        // 최고 성능 전략의 세부 정보
        var bestReport = benchmarkResult.QualityReports.OrderByDescending(r => r.OverallQualityScore).First();
        var bestStrategyName = bestReport.ProcessingOptions?.Strategy ?? "Unknown";
        
        Console.WriteLine($"🥇 최고 성능 전략 ({bestStrategyName}) 상세:");
        Console.WriteLine($"   • 전체 품질 점수: {bestReport.OverallQualityScore:P1}");
        Console.WriteLine($"   • 정보 밀도: {bestReport.InformationDensity.AverageInformationDensity:P1}");
        Console.WriteLine($"   • 구조 보존: {bestReport.StructuralCoherence.StructurePreservation:P1}");
        
        if (bestReport.Recommendations.Any())
        {
            Console.WriteLine($"   • 주요 권장사항: {bestReport.Recommendations.First().Description}");
        }
        
        Console.WriteLine();
    }

    private static string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
            return input;

        return input[..(maxLength - 3)] + "...";
    }
}