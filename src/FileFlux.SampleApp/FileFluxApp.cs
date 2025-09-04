using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.SampleApp.Data;
using FileFlux.SampleApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FileFlux.SampleApp;

/// <summary>
/// FileFlux 데모 애플리케이션 메인 로직
/// </summary>
public class FileFluxApp
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IProgressiveDocumentProcessor _progressiveProcessor;
    private readonly IVectorStoreService _vectorStore;
    private readonly FileFluxDbContext _context;
    private readonly ILogger<FileFluxApp> _logger;

    public FileFluxApp(
        IDocumentProcessor documentProcessor,
        IProgressiveDocumentProcessor progressiveProcessor,
        IVectorStoreService vectorStore,
        FileFluxDbContext context,
        ILogger<FileFluxApp> logger)
    {
        _documentProcessor = documentProcessor ?? throw new ArgumentNullException(nameof(documentProcessor));
        _progressiveProcessor = progressiveProcessor ?? throw new ArgumentNullException(nameof(progressiveProcessor));
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
            var chunkList = new List<DocumentChunk>();
            await foreach (var chunk in _documentProcessor.ProcessChunksAsync(filePath, chunkingOptions))
            {
                chunkList.Add(chunk);
            }
            var chunks = chunkList.ToArray();
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

                    var chunks = await _documentProcessor.ProcessToArrayAsync(filePath, chunkingOptions);
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
    public async Task ProcessDocumentWithProgressAsync(string filePath, string strategy)
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
            
            // 새로운 스트리밍 API 사용
            await foreach (var chunk in _documentProcessor.ProcessChunksAsync(filePath, chunkingOptions))
            {
                chunkList.Add(chunk);
                chunkCount++;
                
                // 진행 상황 표시 (간단한 카운터)
                if (chunkCount % 10 == 0)
                {
                    Console.Write($"\r📦 처리된 청크: {chunkCount}개");
                }
            }
            
            stopwatch.Stop();
            Console.WriteLine($"\r✅ 처리 완료! 총 {chunkCount}개 청크 생성");
            Console.WriteLine($"⏱️ 처리 시간: {stopwatch.Elapsed:mm\\:ss\\.fff}");
            
            // 결과를 배열로 변환
            var chunks = chunkList.ToArray();

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


    private static string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
            return input;

        return input[..(maxLength - 3)] + "...";
    }
}