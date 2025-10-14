using FileFlux;
using FileFlux.Domain;
using Microsoft.Extensions.ObjectPool;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 메모리 최적화된 지능형 청킹 전략
/// Phase 10: Intelligent 전략 메모리 사용량 50% 절감 목표 (27MB → 13.5MB)
/// </summary>
public partial class MemoryOptimizedIntelligentChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex SentenceEndRegex = MyRegex();
    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$|^.+\n[=\-]+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ListItemRegex = new(@"^\s*[-*+]\s+|^\s*\d+\.\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ImportantKeywordRegex = new(@"\b(중요|핵심|요약|결론|참고|주의|경고|important|key|summary|conclusion|note|warning|attention)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 테이블 감지를 위한 정규식
    private static readonly Regex TableRegex = new(@"^\s*\|[^|]+\|[^|]+\|", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownSectionRegex = new(@"^#{1,6}\s+.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    // Phase 10: 메모리 최적화 컴포넌트
    private static readonly AdaptiveOverlapManager _overlapManager = new();
    private static readonly BoundaryQualityManager _boundaryQualityManager = new();
    
    // Phase 10: 메모리 풀링 시스템
    private static readonly ObjectPool<List<SemanticUnitStruct>> _listPool = new DefaultObjectPool<List<SemanticUnitStruct>>(new ListPooledObjectPolicy<SemanticUnitStruct>());
    private static readonly ObjectPool<StringBuilder> _stringBuilderPool = new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy());
    private static readonly ArrayPool<char> _charArrayPool = ArrayPool<char>.Shared;

    public string StrategyName => "MemoryOptimizedIntelligent";

    public IEnumerable<string> SupportedOptions => new[]
    {
        "SemanticCoherence",       // 의미적 일관성 임계값 (기본값: 0.75)
        "ContextWindowSize",       // 컨텍스트 윈도우 크기 (기본값: 3000)
        "StructuralAwareness",     // 구조적 인식 활성화 (기본값: true)
        "AdaptiveOverlap",         // 적응형 오버랩 활성화 (기본값: true)
        "MemoryOptimization"       // 메모리 최적화 모드 (기본값: true)
    };

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        // Phase 10: 메모리 사용량 모니터링
        var initialMemory = GC.GetTotalMemory(false);
        
        try
        {
            // 1단계: 스트리밍 방식으로 의미 단위 추출 (메모리 최적화)
            var semanticUnits = await ExtractSemanticUnitsStreamingAsync(content.Text, cancellationToken);
            
            // 2단계: 문서 구조 분석 (최소 메모리 사용)
            var documentStructure = AnalyzeDocumentStructureLightweight(content.Text);
            
            // 3단계: 메모리 효율적인 청킹
            var chunks = await CreateMemoryEfficientChunksAsync(
                semanticUnits,
                options,
                documentStructure,
                cancellationToken);

            // Phase 10: 메모리 사용량 리포트
            var finalMemory = GC.GetTotalMemory(true); // 강제 GC 실행
            var memoryUsed = finalMemory - initialMemory;
            
            // 메타데이터에 메모리 사용량 포함
            foreach (var chunk in chunks)
            {
                chunk.Metadata.CustomProperties["MemoryOptimized"] = true;
                chunk.Metadata.CustomProperties["MemoryUsageBytes"] = memoryUsed / chunks.Count(); // 청크당 평균 메모리
            }

            return chunks;
        }
        catch (Exception ex)
        {
            // 예외 발생 시 메모리 정리
            GC.Collect();
            throw new InvalidOperationException($"Memory optimized chunking failed: {ex.Message}", ex);
        }
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        var textLength = content.Text.Length;
        var avgChunkSize = options.MaxChunkSize * 0.8; // 평균적으로 80% 사용 가정
        return Math.Max(1, (int)Math.Ceiling(textLength / avgChunkSize));
    }

    /// <summary>
    /// 스트리밍 방식으로 의미 단위 추출 (메모리 최적화)
    /// </summary>
    private async Task<IEnumerable<SemanticUnitStruct>> ExtractSemanticUnitsStreamingAsync(
        string text, 
        CancellationToken cancellationToken)
    {
        var units = _listPool.Get();
        
        try
        {
            var lines = text.Split('\n');
            var position = 0;

            // 스트리밍 처리로 메모리 사용량 최소화
            for (int i = 0; i < lines.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    position += line.Length + 1;
                    continue;
                }

                // struct 사용으로 힙 할당 최소화
                var unit = new SemanticUnitStruct
                {
                    Content = line,
                    Position = position,
                    SemanticWeight = CalculateSemanticWeightFast(line),
                    ContextualRelevance = CalculateContextualRelevanceFast(line, i, lines),
                    Importance = CalculateImportanceFast(line)
                };

                units.Add(unit);
                position += line.Length + 1;

                // 메모리 압박 상황에서 중간 정리
                if (i % 10000 == 0 && i > 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }

            return units.ToArray(); // 배열로 복사하여 반환
        }
        finally
        {
            units.Clear();
            _listPool.Return(units);
        }
    }

    /// <summary>
    /// 메모리 효율적인 청크 생성
    /// </summary>
    private async Task<List<DocumentChunk>> CreateMemoryEfficientChunksAsync(
        IEnumerable<SemanticUnitStruct> units,
        ChunkingOptions options,
        DocumentStructureLightweight structure,
        CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();
        var currentUnits = _listPool.Get();
        var stringBuilder = _stringBuilderPool.Get();
        
        try
        {
            var maxSize = options.MaxChunkSize;
            var currentSize = 0;
            var previousChunkText = string.Empty;
            
            foreach (var unit in units)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var unitSize = unit.Content.Length;
                
                // 청크 크기 초과 시 분할 결정
                if (currentUnits.Count > 0 && currentSize + unitSize > maxSize)
                {
                    // 메모리 효율적인 청크 생성
                    var chunkText = BuildChunkTextEfficiently(currentUnits, stringBuilder);
                    
                    var chunk = new DocumentChunk
                    {
                        Content = chunkText,
                        Location = new SourceLocation
                        {
                            StartChar = currentUnits[0].Position,
                            EndChar = currentUnits[^1].Position + currentUnits[^1].Content.Length
                        }
                    };
                    
                    chunk.Metadata.CustomProperties["Strategy"] = StrategyName;
                    chunk.Metadata.CustomProperties["SemanticUnits"] = currentUnits.Count;
                    chunk.Metadata.CustomProperties["MemoryOptimized"] = true;

                    chunks.Add(chunk);
                    previousChunkText = chunkText;
                    
                    // 현재 청크 초기화 (메모리 재사용)
                    currentUnits.Clear();
                    stringBuilder.Clear();
                    currentSize = 0;
                }

                currentUnits.Add(unit);
                currentSize += unitSize;
            }

            // 마지막 청크 처리
            if (currentUnits.Count > 0)
            {
                var chunkText = BuildChunkTextEfficiently(currentUnits, stringBuilder);
                var chunk = new DocumentChunk
                {
                    Content = chunkText,
                    Location = new SourceLocation
                    {
                        StartChar = currentUnits[0].Position,
                        EndChar = currentUnits[^1].Position + currentUnits[^1].Content.Length
                    }
                };
                
                chunk.Metadata.CustomProperties["Strategy"] = StrategyName;
                chunk.Metadata.CustomProperties["SemanticUnits"] = currentUnits.Count;
                chunk.Metadata.CustomProperties["MemoryOptimized"] = true;
                chunks.Add(chunk);
            }

            return chunks;
        }
        finally
        {
            currentUnits.Clear();
            _listPool.Return(currentUnits);
            stringBuilder.Clear();
            _stringBuilderPool.Return(stringBuilder);
        }
    }

    /// <summary>
    /// StringBuilder를 사용한 메모리 효율적인 청크 텍스트 구성
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string BuildChunkTextEfficiently(List<SemanticUnitStruct> units, StringBuilder stringBuilder)
    {
        stringBuilder.Clear();
        
        for (int i = 0; i < units.Count; i++)
        {
            if (i > 0) stringBuilder.AppendLine();
            stringBuilder.Append(units[i].Content);
        }
        
        return stringBuilder.ToString();
    }

    /// <summary>
    /// 빠른 의미 가중치 계산 (최적화됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double CalculateSemanticWeightFast(ReadOnlySpan<char> content)
    {
        if (content.IsEmpty) return 0.0;
        
        double weight = 0.5; // 기본값
        
        // 중요 키워드 존재 여부 (간단한 검사)
        if (content.Contains("중요", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("important", StringComparison.OrdinalIgnoreCase))
            weight += 0.3;
            
        // 문장 완결성
        if (content.EndsWith('.') || content.EndsWith('!') || content.EndsWith('?'))
            weight += 0.2;
            
        return Math.Min(weight, 1.0);
    }

    /// <summary>
    /// 빠른 맥락 관련성 계산 (최적화됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double CalculateContextualRelevanceFast(string content, int lineIndex, string[] lines)
    {
        if (string.IsNullOrEmpty(content)) return 0.0;
        
        // 간단한 맥락 관련성 계산
        double relevance = 0.5;
        
        // 이전/다음 줄과의 연관성 (키워드 기반)
        if (lineIndex > 0 && HasCommonWords(content, lines[lineIndex - 1]))
            relevance += 0.2;
            
        if (lineIndex < lines.Length - 1 && HasCommonWords(content, lines[lineIndex + 1]))
            relevance += 0.2;
            
        return Math.Min(relevance, 1.0);
    }

    /// <summary>
    /// 빠른 중요도 계산 (최적화됨)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double CalculateImportanceFast(ReadOnlySpan<char> content)
    {
        if (content.IsEmpty) return 0.0;
        
        double importance = 0.5;
        
        // 헤더 감지
        if (content.StartsWith('#'))
            importance += 0.4;
            
        // 리스트 항목
        if (content.TrimStart().StartsWith('-') || content.TrimStart().StartsWith('*'))
            importance += 0.2;
            
        return Math.Min(importance, 1.0);
    }

    /// <summary>
    /// 공통 단어 존재 여부 (빠른 검사)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasCommonWords(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return false;
            
        // 간단한 단어 비교 (성능 최적화)
        var words1 = text1.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words2 = text2.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        return words1.Take(5).Intersect(words2.Take(5), StringComparer.OrdinalIgnoreCase).Any();
    }

    /// <summary>
    /// 경량 문서 구조 분석
    /// </summary>
    private DocumentStructureLightweight AnalyzeDocumentStructureLightweight(string text)
    {
        return new DocumentStructureLightweight
        {
            HasHeaders = HeaderRegex.IsMatch(text),
            HasTables = TableRegex.IsMatch(text),
            HasLists = ListItemRegex.IsMatch(text),
            EstimatedComplexity = Math.Min(text.Length / 10000, 10) // 간단한 복잡도 추정
        };
    }

    [GeneratedRegex(@"[.!?]+(?:\s|$)", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}

/// <summary>
/// 메모리 효율성을 위한 값 타입 의미 단위 (class → struct)
/// </summary>
public struct SemanticUnitStruct
{
    public string Content { get; set; }
    public int Position { get; set; }
    public double SemanticWeight { get; set; }
    public double ContextualRelevance { get; set; }
    public double Importance { get; set; }
}

/// <summary>
/// 경량 문서 구조 정보
/// </summary>
public struct DocumentStructureLightweight
{
    public bool HasHeaders { get; set; }
    public bool HasTables { get; set; }
    public bool HasLists { get; set; }
    public int EstimatedComplexity { get; set; }
}

/// <summary>
/// List pooling policy
/// </summary>
public class ListPooledObjectPolicy<T> : PooledObjectPolicy<List<T>>
{
    public override List<T> Create() => new List<T>();

    public override bool Return(List<T> obj)
    {
        obj.Clear();
        return obj.Capacity < 1024; // 너무 큰 리스트는 풀에 반환하지 않음
    }
}

/// <summary>
/// StringBuilder 풀링을 위한 정책
/// </summary>
public class StringBuilderPooledObjectPolicy : PooledObjectPolicy<StringBuilder>
{
    public override StringBuilder Create() => new StringBuilder();

    public override bool Return(StringBuilder obj)
    {
        obj.Clear();
        return obj.Capacity < 8192; // 너무 큰 StringBuilder는 풀에 반환하지 않음
    }
}