using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FileFlux.Infrastructure.Performance;

/// <summary>
/// 메모리 사용량 측정 및 프로파일링 유틸리티
/// 대용량 문서 처리 시 메모리 효율성 분석을 위한 도구
/// </summary>
public class MemoryProfiler : IDisposable
{
    private readonly ILogger<MemoryProfiler>? _logger;
    private readonly Process _currentProcess;
    private readonly List<MemorySnapshot> _snapshots;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public MemoryProfiler(ILogger<MemoryProfiler>? logger = null)
    {
        _logger = logger;
        _currentProcess = Process.GetCurrentProcess();
        _snapshots = new List<MemorySnapshot>();
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// 현재 메모리 사용량 스냅샷 생성
    /// </summary>
    public MemorySnapshot TakeSnapshot(string label = "")
    {
        GC.Collect(); // 가비지 컬렉션 강제 실행으로 정확한 측정
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var snapshot = new MemorySnapshot
        {
            Label = label,
            Timestamp = DateTime.UtcNow,
            ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds,
            WorkingSetBytes = _currentProcess.WorkingSet64,
            PrivateMemoryBytes = _currentProcess.PrivateMemorySize64,
            ManagedMemoryBytes = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };

        _snapshots.Add(snapshot);

        _logger?.LogDebug("Memory snapshot [{Label}]: Working Set: {WorkingSetMB} MB, " +
                         "Private: {PrivateMB} MB, Managed: {ManagedMB} MB",
            label,
            BytesToMegabytes(snapshot.WorkingSetBytes),
            BytesToMegabytes(snapshot.PrivateMemoryBytes),
            BytesToMegabytes(snapshot.ManagedMemoryBytes));

        return snapshot;
    }

    /// <summary>
    /// 파일 크기 대비 메모리 사용률 분석
    /// </summary>
    public MemoryAnalysisResult AnalyzeFileProcessing(long fileSizeBytes, string baselineLabel, string completedLabel)
    {
        var baseline = _snapshots.FirstOrDefault(s => s.Label == baselineLabel);
        var completed = _snapshots.FirstOrDefault(s => s.Label == completedLabel);

        if (baseline == null || completed == null)
        {
            throw new InvalidOperationException($"스냅샷을 찾을 수 없습니다. Baseline: {baselineLabel}, Completed: {completedLabel}");
        }

        var workingSetDelta = completed.WorkingSetBytes - baseline.WorkingSetBytes;
        var privateDelta = completed.PrivateMemoryBytes - baseline.PrivateMemoryBytes;
        var managedDelta = completed.ManagedMemoryBytes - baseline.ManagedMemoryBytes;

        var result = new MemoryAnalysisResult
        {
            FileSizeBytes = fileSizeBytes,
            FileSizeMB = BytesToMegabytes(fileSizeBytes),
            
            MemoryUsageDelta = new MemoryUsage
            {
                WorkingSetBytes = workingSetDelta,
                PrivateMemoryBytes = privateDelta,
                ManagedMemoryBytes = managedDelta
            },
            
            MemoryToFileRatio = workingSetDelta / (double)fileSizeBytes,
            ProcessingTimeMs = completed.ElapsedMilliseconds - baseline.ElapsedMilliseconds,
            
            GarbageCollections = new GCInfo
            {
                Gen0 = completed.Gen0Collections - baseline.Gen0Collections,
                Gen1 = completed.Gen1Collections - baseline.Gen1Collections,
                Gen2 = completed.Gen2Collections - baseline.Gen2Collections
            },
            
            IsEfficient = workingSetDelta < fileSizeBytes * 2, // 파일 크기의 2배 이하
            PerformanceGrade = CalculatePerformanceGrade(workingSetDelta, fileSizeBytes)
        };

        _logger?.LogInformation("메모리 분석 결과 - 파일: {FileMB} MB, 메모리: {MemoryMB} MB, " +
                               "비율: {Ratio:F2}, 등급: {Grade}",
            result.FileSizeMB,
            BytesToMegabytes(workingSetDelta),
            result.MemoryToFileRatio,
            result.PerformanceGrade);

        return result;
    }

    /// <summary>
    /// 전체 프로파일링 리포트 생성
    /// </summary>
    public ProfilingReport GenerateReport()
    {
        if (_snapshots.Count < 2)
        {
            throw new InvalidOperationException("리포트 생성을 위해서는 최소 2개의 스냅샷이 필요합니다.");
        }

        var report = new ProfilingReport
        {
            StartTime = _snapshots.First().Timestamp,
            EndTime = _snapshots.Last().Timestamp,
            TotalDurationMs = _stopwatch.ElapsedMilliseconds,
            SnapshotCount = _snapshots.Count,
            Snapshots = _snapshots.ToList(),
            
            PeakMemoryUsage = new MemoryUsage
            {
                WorkingSetBytes = _snapshots.Max(s => s.WorkingSetBytes),
                PrivateMemoryBytes = _snapshots.Max(s => s.PrivateMemoryBytes),
                ManagedMemoryBytes = _snapshots.Max(s => s.ManagedMemoryBytes)
            },
            
            TotalGarbageCollections = new GCInfo
            {
                Gen0 = _snapshots.Last().Gen0Collections - _snapshots.First().Gen0Collections,
                Gen1 = _snapshots.Last().Gen1Collections - _snapshots.First().Gen1Collections,
                Gen2 = _snapshots.Last().Gen2Collections - _snapshots.First().Gen2Collections
            }
        };

        return report;
    }

    private static double BytesToMegabytes(long bytes) => bytes / (1024.0 * 1024.0);

    private static string CalculatePerformanceGrade(long memoryUsed, long fileSize)
    {
        var ratio = memoryUsed / (double)fileSize;
        
        return ratio switch
        {
            < 1.0 => "A+ (Excellent)",
            < 1.5 => "A (Very Good)",
            < 2.0 => "B (Good)",
            < 3.0 => "C (Fair)",
            < 5.0 => "D (Poor)",
            _ => "F (Critical)"
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _stopwatch.Stop();
            _currentProcess?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// 메모리 스냅샷 정보
/// </summary>
public record MemorySnapshot
{
    public string Label { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public long WorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long ManagedMemoryBytes { get; init; }
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
}

/// <summary>
/// 메모리 사용량 정보
/// </summary>
public record MemoryUsage
{
    public long WorkingSetBytes { get; init; }
    public long PrivateMemoryBytes { get; init; }
    public long ManagedMemoryBytes { get; init; }
    
    public double WorkingSetMB => WorkingSetBytes / (1024.0 * 1024.0);
    public double PrivateMemoryMB => PrivateMemoryBytes / (1024.0 * 1024.0);
    public double ManagedMemoryMB => ManagedMemoryBytes / (1024.0 * 1024.0);
}

/// <summary>
/// 가비지 컬렉션 정보
/// </summary>
public record GCInfo
{
    public int Gen0 { get; init; }
    public int Gen1 { get; init; }
    public int Gen2 { get; init; }
    
    public int Total => Gen0 + Gen1 + Gen2;
}

/// <summary>
/// 파일 처리 메모리 분석 결과
/// </summary>
public record MemoryAnalysisResult
{
    public long FileSizeBytes { get; init; }
    public double FileSizeMB { get; init; }
    public MemoryUsage MemoryUsageDelta { get; init; } = new();
    public double MemoryToFileRatio { get; init; }
    public long ProcessingTimeMs { get; init; }
    public GCInfo GarbageCollections { get; init; } = new();
    public bool IsEfficient { get; init; }
    public string PerformanceGrade { get; init; } = string.Empty;
}

/// <summary>
/// 프로파일링 종합 리포트
/// </summary>
public record ProfilingReport
{
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public long TotalDurationMs { get; init; }
    public int SnapshotCount { get; init; }
    public List<MemorySnapshot> Snapshots { get; init; } = new();
    public MemoryUsage PeakMemoryUsage { get; init; } = new();
    public GCInfo TotalGarbageCollections { get; init; } = new();
}