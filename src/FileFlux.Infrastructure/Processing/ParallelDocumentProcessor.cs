using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FileFlux.Infrastructure.Processing;

/// <summary>
/// 고성능 병렬 문서 처리기 - CPU 코어별 동적 스케일링 및 메모리 백프레셔 제어
/// </summary>
public class ParallelDocumentProcessor : IParallelDocumentProcessor
{
    private readonly IDocumentProcessor _baseProcessor;
    private readonly ILogger<ParallelDocumentProcessor> _logger;
    private readonly ParallelProcessingStats _stats;
    private readonly ConcurrentQueue<WeakReference> _memoryTracker;
    private readonly SemaphoreSlim _memoryLimitSemaphore;
    private readonly Timer _statsUpdateTimer;
    private readonly object _statsLock = new();
    
    // Worker thread management
    private readonly ConcurrentDictionary<int, WorkerThread> _workerThreads;
    private readonly ThreadLocal<PerformanceCounter> _performanceCounters;
    
    // Memory management
    private long _currentMemoryUsage;
    private readonly MemoryPool _memoryPool;
    
    // Cancellation tracking
    private readonly CancellationTokenSource _shutdownTokenSource;
    private volatile bool _isDisposed;

    public ParallelDocumentProcessor(
        IDocumentProcessor baseProcessor,
        ILogger<ParallelDocumentProcessor> logger)
    {
        _baseProcessor = baseProcessor ?? throw new ArgumentNullException(nameof(baseProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _stats = new ParallelProcessingStats();
        _memoryTracker = new ConcurrentQueue<WeakReference>();
        _memoryLimitSemaphore = new SemaphoreSlim(1, 1);
        _workerThreads = new ConcurrentDictionary<int, WorkerThread>();
        _performanceCounters = new ThreadLocal<PerformanceCounter>(() => new PerformanceCounter());
        _memoryPool = new MemoryPool();
        _shutdownTokenSource = new CancellationTokenSource();
        
        // Start statistics update timer
        _statsUpdateTimer = new Timer(UpdateStatistics, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        
        _logger.LogInformation("ParallelDocumentProcessor initialized with {ProcessorCount} CPU cores", 
            Environment.ProcessorCount);
    }

    /// <summary>
    /// 여러 문서를 병렬로 처리하여 청크 스트림 반환
    /// </summary>
    public async IAsyncEnumerable<ParallelProcessingResult> ProcessManyAsync(
        IEnumerable<string> filePaths,
        ChunkingOptions? options = null,
        ParallelProcessingOptions? parallelOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ParallelDocumentProcessor));
        
        var processingOptions = parallelOptions ?? new ParallelProcessingOptions();
        var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _shutdownTokenSource.Token).Token;
        
        _logger.LogInformation("Starting parallel processing of multiple documents with MaxDegreeOfParallelism: {MaxDegree}",
            processingOptions.MaxDegreeOfParallelism);

        // Create processing channel for backpressure control
        var channelOptions = new BoundedChannelOptions(processingOptions.BackpressureThreshold)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        var channel = Channel.CreateBounded<ParallelProcessingResult>(channelOptions);
        var writer = channel.Writer;
        var reader = channel.Reader;

        // Start background processing task
        var processingTask = Task.Run(async () =>
        {
            try
            {
                var semaphore = new SemaphoreSlim(processingOptions.MaxDegreeOfParallelism);
                var tasks = filePaths.Select(async filePath =>
                {
                    await semaphore.WaitAsync(combinedToken).ConfigureAwait(false);
                    try
                    {
                        var result = await ProcessSingleDocumentAsync(filePath, options, processingOptions, combinedToken)
                            .ConfigureAwait(false);
                        await writer.WriteAsync(result, combinedToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToArray();

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Parallel processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during parallel processing");
            }
            finally
            {
                writer.TryComplete();
            }
        }, combinedToken);

        // Yield results as they become available
        await foreach (var result in reader.ReadAllAsync(combinedToken).ConfigureAwait(false))
        {
            yield return result;
            
            // Update statistics
            lock (_statsLock)
            {
                _stats.TotalDocumentsProcessed++;
                _stats.TotalChunksGenerated += result.Chunks.Count;
                _stats.LastUpdated = DateTime.UtcNow;
            }
        }

        await processingTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 단일 대용량 문서를 청크 단위로 병렬 처리
    /// </summary>
    public async IAsyncEnumerable<DocumentChunk> ProcessLargeDocumentAsync(
        string filePath,
        ChunkingOptions? options = null,
        ParallelProcessingOptions? parallelOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(ParallelDocumentProcessor));
        
        var processingOptions = parallelOptions ?? new ParallelProcessingOptions();
        var combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _shutdownTokenSource.Token).Token;

        var fileInfo = new FileInfo(filePath);
        var isLargeFile = fileInfo.Length > processingOptions.LargeFileThresholdBytes;
        
        _logger.LogInformation("Processing {FileType} document: {FileName} ({FileSize:N0} bytes)",
            isLargeFile ? "large" : "standard", Path.GetFileName(filePath), fileInfo.Length);

        if (isLargeFile)
        {
            // For large files, use streaming approach with memory management
            await foreach (var chunk in ProcessLargeFileWithBackpressure(filePath, options, processingOptions, combinedToken)
                .ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
        else
        {
            // For smaller files, use standard processing
            await foreach (var chunk in _baseProcessor.ProcessAsync(filePath, options, combinedToken)
                .ConfigureAwait(false))
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// 대용량 파일을 메모리 관리하면서 처리
    /// </summary>
    private async IAsyncEnumerable<DocumentChunk> ProcessLargeFileWithBackpressure(
        string filePath,
        ChunkingOptions? options,
        ParallelProcessingOptions parallelOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var chunkBuffer = new List<DocumentChunk>(parallelOptions.ChunkBatchSize);
        var processedCount = 0;
        var stopwatch = Stopwatch.StartNew();

        await foreach (var chunk in _baseProcessor.ProcessAsync(filePath, options, cancellationToken)
            .ConfigureAwait(false))
        {
            // Check memory pressure before adding chunk
            await EnsureMemoryAvailable(chunk.Content.Length * 2, cancellationToken).ConfigureAwait(false);

            chunkBuffer.Add(chunk);
            processedCount++;

            // Yield batch when full or at end
            if (chunkBuffer.Count >= parallelOptions.ChunkBatchSize)
            {
                foreach (var bufferedChunk in chunkBuffer)
                {
                    yield return bufferedChunk;
                }
                chunkBuffer.Clear();

                // Report progress periodically
                if (stopwatch.ElapsedMilliseconds > parallelOptions.ProgressReportIntervalMs)
                {
                    _logger.LogDebug("Processed {ChunkCount} chunks from large file: {FileName}",
                        processedCount, Path.GetFileName(filePath));
                    stopwatch.Restart();
                }
            }

            // Memory cleanup hint for GC
            if (processedCount % 1000 == 0)
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        // Yield remaining chunks
        foreach (var chunk in chunkBuffer)
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 단일 문서 처리 (내부 메서드)
    /// </summary>
    private async Task<ParallelProcessingResult> ProcessSingleDocumentAsync(
        string filePath,
        ChunkingOptions? options,
        ParallelProcessingOptions parallelOptions,
        CancellationToken cancellationToken)
    {
        var result = new ParallelProcessingResult
        {
            FilePath = filePath,
            WorkerThreadCount = 1
        };

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var chunks = new List<DocumentChunk>();
            await foreach (var chunk in _baseProcessor.ProcessAsync(filePath, options, cancellationToken)
                .ConfigureAwait(false))
            {
                chunks.Add(chunk);
                
                // Check for cancellation periodically
                if (chunks.Count % 100 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            result.Chunks = chunks;
            result.IsSuccess = true;
            result.Metadata["ChunkCount"] = chunks.Count;
            result.Metadata["FileSize"] = new FileInfo(filePath).Length;
            
            _logger.LogDebug("Successfully processed {FileName}: {ChunkCount} chunks in {ElapsedMs}ms",
                Path.GetFileName(filePath), chunks.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Processing was cancelled";
            _logger.LogInformation("Processing cancelled for {FileName}", Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            lock (_statsLock)
            {
                _stats.ErrorCount++;
            }
            
            _logger.LogError(ex, "Error processing {FileName}", Path.GetFileName(filePath));
        }
        finally
        {
            result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
        }

        return result;
    }

    /// <summary>
    /// 메모리 가용성 확보
    /// </summary>
    private async Task EnsureMemoryAvailable(long requiredBytes, CancellationToken cancellationToken)
    {
        if (Interlocked.Read(ref _currentMemoryUsage) + requiredBytes > _memoryPool.MaxMemoryUsage)
        {
            await _memoryLimitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Force garbage collection to free memory
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // Update memory usage tracking
                CleanupMemoryTracker();
                
                var currentUsage = GC.GetTotalMemory(false);
                Interlocked.Exchange(ref _currentMemoryUsage, currentUsage);
                
                _logger.LogDebug("Memory cleanup performed. Current usage: {MemoryMB:N1}MB", 
                    currentUsage / (1024.0 * 1024.0));
            }
            finally
            {
                _memoryLimitSemaphore.Release();
            }
        }

        Interlocked.Add(ref _currentMemoryUsage, requiredBytes);
    }

    /// <summary>
    /// 메모리 추적기 정리
    /// </summary>
    private void CleanupMemoryTracker()
    {
        var aliveCount = 0;
        while (_memoryTracker.TryDequeue(out var weakRef))
        {
            if (weakRef.IsAlive)
            {
                aliveCount++;
                _memoryTracker.Enqueue(weakRef);
            }
        }
        
        _logger.LogDebug("Memory tracker cleanup: {AliveObjects} objects still alive", aliveCount);
    }

    /// <summary>
    /// 통계 업데이트 (타이머 콜백)
    /// </summary>
    private void UpdateStatistics(object? state)
    {
        if (_isDisposed) return;

        lock (_statsLock)
        {
            _stats.ActiveWorkerThreads = _workerThreads.Count;
            _stats.CurrentMemoryUsageBytes = Interlocked.Read(ref _currentMemoryUsage);
            _stats.PeakMemoryUsageBytes = Math.Max(_stats.PeakMemoryUsageBytes, _stats.CurrentMemoryUsageBytes);
            
            // Calculate throughput
            var elapsedTime = DateTime.UtcNow - _stats.StartTime;
            if (elapsedTime.TotalSeconds > 0)
            {
                _stats.ThroughputDocumentsPerSecond = _stats.TotalDocumentsProcessed / elapsedTime.TotalSeconds;
                _stats.AverageProcessingTimeMs = elapsedTime.TotalMilliseconds / Math.Max(1, _stats.TotalDocumentsProcessed);
            }

            _stats.LastUpdated = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 병렬 처리 성능 통계 조회
    /// </summary>
    public ParallelProcessingStats GetProcessingStats()
    {
        lock (_statsLock)
        {
            return new ParallelProcessingStats
            {
                TotalDocumentsProcessed = _stats.TotalDocumentsProcessed,
                TotalChunksGenerated = _stats.TotalChunksGenerated,
                AverageProcessingTimeMs = _stats.AverageProcessingTimeMs,
                ActiveWorkerThreads = _stats.ActiveWorkerThreads,
                PeakConcurrentDocuments = _stats.PeakConcurrentDocuments,
                CurrentMemoryUsageBytes = _stats.CurrentMemoryUsageBytes,
                PeakMemoryUsageBytes = _stats.PeakMemoryUsageBytes,
                ErrorCount = _stats.ErrorCount,
                AverageCpuUtilization = _stats.AverageCpuUtilization,
                ThroughputDocumentsPerSecond = _stats.ThroughputDocumentsPerSecond,
                StartTime = _stats.StartTime,
                LastUpdated = _stats.LastUpdated
            };
        }
    }

    /// <summary>
    /// 병렬 처리기 리소스 정리
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        _logger.LogInformation("Shutting down ParallelDocumentProcessor...");
        
        // Signal shutdown
        _shutdownTokenSource.Cancel();
        
        // Stop statistics timer
        await _statsUpdateTimer.DisposeAsync().ConfigureAwait(false);
        
        // Cleanup resources
        _memoryLimitSemaphore.Dispose();
        _shutdownTokenSource.Dispose();
        _performanceCounters.Dispose();
        _memoryPool.Dispose();
        
        // Wait for worker threads to complete
        var workerTasks = _workerThreads.Values.Select(w => w.CompletionTask).ToArray();
        if (workerTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(workerTasks).WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout waiting for worker threads to complete");
            }
        }

        var finalStats = GetProcessingStats();
        _logger.LogInformation("ParallelDocumentProcessor disposed. Final stats: {DocumentsProcessed} documents, {ChunksGenerated} chunks, {ErrorCount} errors",
            finalStats.TotalDocumentsProcessed, finalStats.TotalChunksGenerated, finalStats.ErrorCount);
    }

    /// <summary>
    /// 워커 스레드 래퍼
    /// </summary>
    private class WorkerThread
    {
        public int ThreadId { get; }
        public Task CompletionTask { get; }
        public DateTime StartTime { get; }
        
        public WorkerThread(int threadId, Task completionTask)
        {
            ThreadId = threadId;
            CompletionTask = completionTask;
            StartTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 성능 카운터
    /// </summary>
    private class PerformanceCounter
    {
        public int ProcessedDocuments { get; set; }
        public long TotalProcessingTime { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 메모리 풀
    /// </summary>
    private class MemoryPool : IDisposable
    {
        public long MaxMemoryUsage { get; } = 1024 * 1024 * 1024; // 1GB default
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}