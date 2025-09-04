using Microsoft.Extensions.Logging;

namespace FileFlux.SampleApp;

/// <summary>
/// 벤치마크 실행 전용 프로그램
/// </summary>
public static class BenchmarkProgram
{
    public static async Task RunBenchmarkAsync(string[] args)
    {
        // 로깅 설정
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        var logger = loggerFactory.CreateLogger<ComprehensiveBenchmarkRunner>();
        
        // 벤치마크 실행
        var runner = new ComprehensiveBenchmarkRunner(logger);
        
        Console.WriteLine("FileFlux 종합 벤치마크를 시작합니다...\n");
        
        try
        {
            var report = await runner.RunComprehensiveBenchmarkAsync();
            
            // 상세 보고서 출력
            await GenerateDetailedReportAsync(report);
            
            Console.WriteLine("\n벤치마크가 완료되었습니다.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"벤치마크 실행 중 오류 발생: {ex.Message}");
            Console.WriteLine($"상세 오류: {ex}");
        }
        
        Console.WriteLine("\n벤치마크 실행이 완료되었습니다.");
    }
    
    private static async Task GenerateDetailedReportAsync(BenchmarkReport report)
    {
        var reportPath = Path.Combine(Environment.CurrentDirectory, $"benchmark-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        
        var reportBuilder = new System.Text.StringBuilder();
        reportBuilder.AppendLine("=".PadRight(80, '='));
        reportBuilder.AppendLine("FileFlux 종합 벤치마크 보고서");
        reportBuilder.AppendLine($"생성 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportBuilder.AppendLine("=".PadRight(80, '='));
        
        if (report.Summary != null)
        {
            reportBuilder.AppendLine("\n📊 종합 통계");
            reportBuilder.AppendLine("-".PadRight(50, '-'));
            reportBuilder.AppendLine($"총 테스트 파일: {report.Summary.TotalFiles}개");
            reportBuilder.AppendLine($"성공: {report.Summary.SuccessfulFiles}개");
            reportBuilder.AppendLine($"실패: {report.Summary.FailedFiles}개");
            reportBuilder.AppendLine($"성공률: {(double)report.Summary.SuccessfulFiles / report.Summary.TotalFiles * 100:F1}%");
            
            if (report.Summary.SuccessfulFiles > 0)
            {
                reportBuilder.AppendLine($"평균 처리 속도: {report.Summary.AverageProcessingSpeedMBps:F2} MB/초");
                reportBuilder.AppendLine($"평균 메모리 사용률: {report.Summary.AverageMemoryRatio:F2}배");
                reportBuilder.AppendLine($"평균 청킹 수: {report.Summary.AverageChunkCount:F0}개");
            }
        }
        
        reportBuilder.AppendLine("\n📋 파일별 상세 결과");
        reportBuilder.AppendLine("-".PadRight(80, '-'));
        
        var successful = report.Results.Where(r => r.IsSuccessful).OrderBy(r => r.FileType).ThenBy(r => r.FileName).ToList();
        var failed = report.Results.Where(r => !r.IsSuccessful).ToList();
        
        // 성공한 파일들
        if (successful.Any())
        {
            reportBuilder.AppendLine("\n✅ 성공한 파일들:");
            
            foreach (var result in successful)
            {
                reportBuilder.AppendLine($"\n파일명: {result.FileName}");
                reportBuilder.AppendLine($"파일 형식: {result.FileType}");
                reportBuilder.AppendLine($"파일 크기: {result.FileSizeMB:F2} MB");
                reportBuilder.AppendLine($"처리 시간: {result.ProcessingTimeMs:F0} ms");
                reportBuilder.AppendLine($"처리 속도: {result.ProcessingSpeedMBps:F2} MB/초");
                reportBuilder.AppendLine($"청킹 수: {result.ChunkCount:N0}개");
                reportBuilder.AppendLine($"추출된 텍스트 길이: {result.ExtractedTextLength:N0}자");
                reportBuilder.AppendLine($"메모리 사용량: {result.MemoryUsageMB:F2} MB");
                reportBuilder.AppendLine($"메모리/파일 비율: {result.MemoryToFileRatio:F2}배");
                reportBuilder.AppendLine($"성능 등급: {result.PerformanceGrade}");
                reportBuilder.AppendLine($"효율성 평가: {(result.IsEfficient ? "통과" : "개선 필요")}");
                reportBuilder.AppendLine("-".PadRight(40, '-'));
            }
        }
        
        // 실패한 파일들
        if (failed.Any())
        {
            reportBuilder.AppendLine("\n❌ 실패한 파일들:");
            
            foreach (var result in failed)
            {
                reportBuilder.AppendLine($"\n파일명: {result.FileName}");
                reportBuilder.AppendLine($"파일 형식: {result.FileType}");
                reportBuilder.AppendLine($"파일 크기: {result.FileSizeMB:F2} MB");
                reportBuilder.AppendLine($"오류 메시지: {result.ErrorMessage}");
                reportBuilder.AppendLine("-".PadRight(40, '-'));
            }
        }
        
        // 파일 형식별 통계
        if (successful.Any())
        {
            reportBuilder.AppendLine("\n📈 파일 형식별 통계");
            reportBuilder.AppendLine("-".PadRight(50, '-'));
            
            var byType = successful.GroupBy(r => r.FileType).OrderBy(g => g.Key).ToList();
            
            foreach (var group in byType)
            {
                var items = group.ToList();
                reportBuilder.AppendLine($"\n{group.Key} ({items.Count}개 파일):");
                reportBuilder.AppendLine($"  평균 처리 속도: {items.Average(r => r.ProcessingSpeedMBps):F2} MB/초");
                reportBuilder.AppendLine($"  평균 메모리 효율: {items.Average(r => r.MemoryToFileRatio):F2}배");
                reportBuilder.AppendLine($"  평균 청킹 수: {items.Average(r => r.ChunkCount):F0}개");
                reportBuilder.AppendLine($"  성능 등급 분포:");
                
                var gradeStats = items.GroupBy(r => r.PerformanceGrade).OrderBy(g => g.Key);
                foreach (var grade in gradeStats)
                {
                    reportBuilder.AppendLine($"    {grade.Key}: {grade.Count()}개 ({(double)grade.Count() / items.Count * 100:F1}%)");
                }
            }
        }
        
        reportBuilder.AppendLine("\n" + "=".PadRight(80, '='));
        reportBuilder.AppendLine("보고서 생성 완료");
        reportBuilder.AppendLine("=".PadRight(80, '='));
        
        // 파일로 저장
        await File.WriteAllTextAsync(reportPath, reportBuilder.ToString());
        
        // 콘솔에 출력
        Console.WriteLine(reportBuilder.ToString());
        Console.WriteLine($"\n📄 상세 보고서가 저장되었습니다: {reportPath}");
    }
}