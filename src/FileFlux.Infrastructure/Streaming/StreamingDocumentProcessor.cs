using FileFlux.Domain;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FileFlux.Infrastructure.Streaming;

/// <summary>
/// 스트리밍 최적화된 문서 처리기 - 백프레셔 제어 및 메모리 효율성
/// </summary>
public class StreamingDocumentProcessor : IStreamingDocumentProcessor
{
    private readonly IDocumentProcessor _baseProcessor;
    private readonly IDocumentCacheService _cacheService;
    private readonly ILogger<StreamingDocumentProcessor> _logger;
    private readonly StreamingOptions _defaultOptions;

    public StreamingDocumentProcessor(
        IDocumentProcessor baseProcessor,
        IDocumentCacheService cacheService,
        ILogger<StreamingDocumentProcessor> logger)
    {
        _baseProcessor = baseProcessor ?? throw new ArgumentNullException(nameof(baseProcessor));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _defaultOptions = new StreamingOptions();
        
        _logger.LogInformation("StreamingDocumentProcessor initialized with cache support");
    }

    /// <summary>
    /// 스트리밍 방식으로 문서 처리 - 캐시 우선 검사
    /// </summary>
    public async IAsyncEnumerable<StreamingChunkResult> ProcessStreamAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        StreamingOptions? streamingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        
        var options = chunkingOptions ?? new ChunkingOptions();
        var streaming = streamingOptions ?? _defaultOptions;
        
        _logger.LogInformation("Starting streaming processing for: {FileName}", Path.GetFileName(filePath));

        // Check cache first
        var cacheKey = await _cacheService.GenerateCacheKeyAsync(filePath, options, cancellationToken)
            .ConfigureAwait(false);
        
        var cachedResult = await _cacheService.GetAsync(cacheKey, cancellationToken)
            .ConfigureAwait(false);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for {FileName}, returning {ChunkCount} cached chunks", 
                Path.GetFileName(filePath), cachedResult.Chunks.Count);
            
            // Return cached chunks as streaming results
            await foreach (var result in StreamCachedChunks(cachedResult, streaming, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return result;
            }
            yield break;
        }

        // Process and cache
        _logger.LogDebug("Cache miss for {FileName}, processing and caching", Path.GetFileName(filePath));
        
        var chunks = new List<DocumentChunk>();
        DocumentMetadata? metadata = null;

        await foreach (var chunk in _baseProcessor.ProcessAsync(filePath, options, cancellationToken)
            .ConfigureAwait(false))
        {
            chunks.Add(chunk);
            metadata ??= chunk.Metadata;

            // Yield immediately for streaming
            yield return new StreamingChunkResult
            {
                Chunk = chunk,
                IsFromCache = false,
                ProcessingTimeMs = 0, // Real-time processing
                ChunkIndex = chunks.Count - 1,
                TotalEstimatedChunks = null // Unknown during processing
            };

            // Apply backpressure control
            if (chunks.Count % streaming.BackpressureBatchSize == 0)
            {
                await Task.Delay(streaming.BackpressureDelayMs, cancellationToken).ConfigureAwait(false);
                
                // Force garbage collection hint for memory management
                if (streaming.EnableMemoryOptimization && chunks.Count % (streaming.BackpressureBatchSize * 10) == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }
        }

        // Cache the complete result
        if (metadata != null && chunks.Count > 0)
        {
            try
            {
                await _cacheService.SetAsync(cacheKey, chunks, metadata, 
                    expiration: TimeSpan.FromHours(streaming.CacheExpirationHours), 
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("Cached processing result for {FileName}: {ChunkCount} chunks", 
                    Path.GetFileName(filePath), chunks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache processing result for {FileName}", Path.GetFileName(filePath));
            }
        }
    }

    /// <summary>
    /// 다중 파일 스트리밍 처리 - 우선순위 기반
    /// </summary>
    public async IAsyncEnumerable<StreamingBatchResult> ProcessMultipleStreamAsync(
        IEnumerable<string> filePaths,
        ChunkingOptions? chunkingOptions = null,
        StreamingOptions? streamingOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        
        var files = filePaths.ToList();
        var options = chunkingOptions ?? new ChunkingOptions();
        var streaming = streamingOptions ?? _defaultOptions;
        
        _logger.LogInformation("Starting batch streaming processing for {FileCount} files", files.Count);

        // Create processing channel with backpressure
        var channelOptions = new BoundedChannelOptions(streaming.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        var channel = Channel.CreateBounded<StreamingBatchResult>(channelOptions);
        var writer = channel.Writer;
        var reader = channel.Reader;

        // Start background processing
        var processingTask = Task.Run(async () =>
        {
            var semaphore = new SemaphoreSlim(streaming.MaxConcurrentFiles);
            
            try
            {
                var fileTasks = files.Select(async (filePath, index) =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    
                    try
                    {
                        var fileResults = new List<StreamingChunkResult>();
                        var processingStart = DateTime.UtcNow;
                        
                        await foreach (var chunkResult in ProcessStreamAsync(filePath, options, streaming, cancellationToken)
                            .ConfigureAwait(false))
                        {
                            fileResults.Add(chunkResult);
                            
                            // Yield intermediate results for large files
                            if (fileResults.Count % streaming.IntermediateYieldSize == 0)
                            {
                                var intermediateResult = new StreamingBatchResult
                                {
                                    FilePath = filePath,
                                    FileIndex = index,
                                    ChunkResults = new List<StreamingChunkResult>(fileResults),
                                    IsCompleted = false,
                                    ProcessingTimeMs = (long)(DateTime.UtcNow - processingStart).TotalMilliseconds,
                                    IsFromCache = fileResults.FirstOrDefault()?.IsFromCache ?? false
                                };
                                
                                await writer.WriteAsync(intermediateResult, cancellationToken).ConfigureAwait(false);
                                fileResults.Clear(); // Clear to save memory
                            }
                        }
                        
                        // Final result for this file
                        var finalResult = new StreamingBatchResult
                        {
                            FilePath = filePath,
                            FileIndex = index,
                            ChunkResults = fileResults,
                            IsCompleted = true,
                            ProcessingTimeMs = (long)(DateTime.UtcNow - processingStart).TotalMilliseconds,
                            IsFromCache = fileResults.FirstOrDefault()?.IsFromCache ?? false
                        };
                        
                        await writer.WriteAsync(finalResult, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing file {FileName}", Path.GetFileName(filePath));
                        
                        var errorResult = new StreamingBatchResult
                        {
                            FilePath = filePath,
                            FileIndex = index,
                            ChunkResults = new List<StreamingChunkResult>(),
                            IsCompleted = true,
                            Error = ex.Message,
                            ProcessingTimeMs = 0
                        };
                        
                        await writer.WriteAsync(errorResult, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(fileTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Batch streaming processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch streaming processing");
            }
            finally
            {
                writer.TryComplete();
                semaphore.Dispose();
            }
        }, cancellationToken);

        // Yield results as they become available
        await foreach (var result in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }

        await processingTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 스트리밍 통계 조회
    /// </summary>
    public async Task<StreamingStats> GetStreamingStatsAsync(CancellationToken cancellationToken = default)
    {
        var cacheStats = await _cacheService.GetStatsAsync(cancellationToken).ConfigureAwait(false);
        
        return new StreamingStats
        {
            CacheHitCount = cacheStats.TotalHits,
            CacheItemCount = cacheStats.ItemCount,
            CacheMemoryUsageBytes = cacheStats.EstimatedMemoryUsageBytes,
            CacheEfficiency = cacheStats.MemoryEfficiency,
            CacheUsageRatio = cacheStats.UsageRatio,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 캐시된 청크를 스트리밍 결과로 변환
    /// </summary>
    private async IAsyncEnumerable<StreamingChunkResult> StreamCachedChunks(
        CachedDocumentResult cachedResult,
        StreamingOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < cachedResult.Chunks.Count; i++)
        {
            var chunk = cachedResult.Chunks[i];
            
            yield return new StreamingChunkResult
            {
                Chunk = chunk,
                IsFromCache = true,
                ProcessingTimeMs = 0, // Cached, no processing time
                ChunkIndex = i,
                TotalEstimatedChunks = cachedResult.Chunks.Count
            };

            // Apply throttling for cached results to prevent overwhelming
            if (i % options.CachedResultBatchSize == 0 && i > 0)
            {
                await Task.Delay(options.CachedResultDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}