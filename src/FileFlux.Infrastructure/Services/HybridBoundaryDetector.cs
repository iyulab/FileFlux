namespace FileFlux.Infrastructure.Services;

/// <summary>
/// 하이브리드 경계 감지기 구현 - 통계적 모델링과 임베딩을 결합
/// 연구 기반 F1 Score 향상을 위한 다중 신호 분석
/// </summary>
public class HybridBoundaryDetector : IHybridBoundaryDetector
{
    private readonly IStatisticalBoundaryDetector _statisticalDetector;
    private readonly ISemanticBoundaryDetector _semanticDetector;
    private double _alpha = 0.6;
    private double _boundaryThreshold = 0.7;

    public HybridBoundaryDetector(
        IStatisticalBoundaryDetector? statisticalDetector = null,
        ISemanticBoundaryDetector? semanticDetector = null)
    {
#if DEBUG
        _statisticalDetector = statisticalDetector ?? new MockStatisticalBoundaryDetector();
#else
        _statisticalDetector = statisticalDetector ?? throw new InvalidOperationException("IStatisticalBoundaryDetector must be provided in Release builds");
#endif
        _semanticDetector = semanticDetector ?? new SemanticBoundaryDetector();
    }

    public double Alpha
    {
        get => _alpha;
        set => _alpha = Math.Max(0, Math.Min(1, value));
    }

    public double BoundaryThreshold
    {
        get => _boundaryThreshold;
        set => _boundaryThreshold = Math.Max(0, Math.Min(1, value));
    }

    public async Task<HybridBoundaryResult> DetectBoundaryAsync(
        string segment1,
        string segment2,
        ITextCompletionService textCompletionService,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken = default)
    {
        // 병렬로 통계적 분석과 임베딩 분석 수행
        var statisticalTask = CalculateStatisticalScoreAsync(
            segment1, segment2, textCompletionService, cancellationToken);
        
        var similarityTask = CalculateSimilarityScoreAsync(
            segment1, segment2, embeddingService, cancellationToken);

        await Task.WhenAll(statisticalTask, similarityTask);

        var statisticalScore = await statisticalTask;
        var similarityScore = await similarityTask;

        // 하이브리드 점수 계산
        var hybridScore = CalculateHybridScore(statisticalScore.Normalized, similarityScore);

        // 경계 판단
        var isBoundary = hybridScore > _boundaryThreshold;

        // 경계 타입 결정
        var boundaryType = DetermineBoundaryType(
            segment1, segment2, statisticalScore.Raw, similarityScore, hybridScore);

        // 신뢰도 계산
        var confidence = CalculateConfidence(hybridScore, statisticalScore.Normalized, similarityScore);

        return new HybridBoundaryResult
        {
            IsBoundary = isBoundary,
            HybridScore = hybridScore,
            StatisticalScore = statisticalScore.Normalized,
            SimilarityScore = similarityScore,
            RawStatisticalScore = statisticalScore.Raw,
            RawSimilarity = similarityScore,
            Confidence = confidence,
            BoundaryType = boundaryType,
            Metadata = new Dictionary<string, object>
            {
                ["Alpha"] = _alpha,
                ["Threshold"] = _boundaryThreshold,
                ["Method"] = "Hybrid (PPL + Embedding)"
            }
        };
    }

    public double CalculateHybridScore(double statisticalScore, double similarity)
    {
        // Hybrid Score = α * Statistical + (1-α) * (1 - similarity)
        // statisticalScore는 이미 정규화된 값 (0-1)
        // similarity는 유사도이므로 반전 필요
        return _alpha * statisticalScore + (1 - _alpha) * (1 - similarity);
    }

    public async Task<IEnumerable<HybridBoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        ITextCompletionService textCompletionService,
        IEmbeddingService embeddingService,
        HybridDetectionOptions? options,
        CancellationToken cancellationToken = default)
    {
        options ??= new HybridDetectionOptions();
        var boundaries = new List<HybridBoundaryPoint>();

        // 모든 세그먼트 쌍에 대해 분석
        for (int i = 0; i < segments.Count - 1; i++)
        {
            // 최소 길이 체크
            if (segments[i].Length < options.MinSegmentLength ||
                segments[i + 1].Length < options.MinSegmentLength)
            {
                continue;
            }

            var result = await DetectBoundaryAsync(
                segments[i],
                segments[i + 1],
                textCompletionService,
                embeddingService,
                cancellationToken);

            if (result.HybridScore > options.BoundaryThreshold || 
                (options.UseAdaptiveThreshold && result.HybridScore > GetAdaptiveThreshold(segments, i)))
            {
                boundaries.Add(new HybridBoundaryPoint
                {
                    SegmentIndex = i,
                    HybridScore = result.HybridScore,
                    StatisticalContribution = result.StatisticalScore * options.Alpha,
                    SimilarityContribution = (1 - result.SimilarityScore) * (1 - options.Alpha),
                    IsBoundary = true,
                    Confidence = result.Confidence,
                    Type = result.BoundaryType,
                    Reason = GenerateReason(result)
                });
            }
        }

        // 근접 경계 병합
        if (options.MergeNearbyBoundaries)
        {
            boundaries = MergeNearbyBoundaries(boundaries, options.MergeDistance);
        }

        return boundaries;
    }

    private async Task<(double Raw, double Normalized)> CalculateStatisticalScoreAsync(
        string segment1,
        string segment2,
        ITextCompletionService textCompletionService,
        CancellationToken cancellationToken)
    {
        // 통계적 불확실성 계산 (context로 segment1 사용)
        var statisticalResult = await _statisticalDetector.CalculateUncertaintyAsync(
            segment2, segment1, textCompletionService, cancellationToken);

        // 불확실성 점수 정규화 (0-1 범위로)
        // 일반적으로 1-1000 범위, log scale 사용
        var normalized = NormalizeUncertainty(statisticalResult.UncertaintyScore);

        return (statisticalResult.UncertaintyScore, normalized);
    }

    private async Task<double> CalculateSimilarityScoreAsync(
        string segment1,
        string segment2,
        IEmbeddingService embeddingService,
        CancellationToken cancellationToken)
    {
        var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(
            new[] { segment1, segment2 },
            EmbeddingPurpose.Analysis,
            cancellationToken);

        var embeddingArray = embeddings.ToArray();
        return embeddingService.CalculateSimilarity(embeddingArray[0], embeddingArray[1]);
    }

    private double NormalizeUncertainty(double uncertaintyScore)
    {
        // Log scale normalization
        // Score 1-10: low (0-0.3)
        // Score 10-100: medium (0.3-0.7)  
        // Score 100+: high (0.7-1.0)
        if (uncertaintyScore <= 1) return 0;
        if (uncertaintyScore >= 1000) return 1;

        var logScore = Math.Log10(uncertaintyScore);
        return Math.Min(1, logScore / 3); // log10(1000) = 3
    }

    private BoundaryType DetermineBoundaryType(
        string segment1,
        string segment2,
        double uncertaintyScore,
        double similarity,
        double hybridScore)
    {
        // 매우 높은 불확실성과 낮은 유사도 = 주제 변경
        if (uncertaintyScore > 100 && similarity < 0.3)
        {
            return BoundaryType.TopicChange;
        }

        // 구조적 마커 체크
        if (segment2.StartsWith("#") || segment2.Contains("HEADING_START"))
        {
            return BoundaryType.Section;
        }

        if (segment2.Contains("```") || segment2.Contains("CODE_"))
        {
            return BoundaryType.CodeBlock;
        }

        if (segment2.Contains("TABLE_START") || (segment2.Contains("|") && segment2.Count(c => c == '|') > 3))
        {
            return BoundaryType.Table;
        }

        if (segment2.Contains("LIST_START") || 
            System.Text.RegularExpressions.Regex.IsMatch(segment2, @"^\s*[-*+•]\s+"))
        {
            return BoundaryType.List;
        }

        // 중간 정도의 경계 = 단락
        if (hybridScore > 0.5)
        {
            return BoundaryType.Paragraph;
        }

        return BoundaryType.Sentence;
    }

    private double CalculateConfidence(double hybridScore, double statisticalScore, double similarityScore)
    {
        // 두 신호가 일치하면 신뢰도 높음
        var agreement = 1 - Math.Abs(statisticalScore - (1 - similarityScore));
        
        // 경계 임계값과의 거리
        var distanceFromThreshold = Math.Abs(hybridScore - _boundaryThreshold);
        
        // 종합 신뢰도
        return (agreement * 0.6 + distanceFromThreshold * 0.4);
    }

    private double GetAdaptiveThreshold(IList<string> segments, int index)
    {
        // 문서 위치에 따른 적응형 임계값
        // 시작/끝 부분은 더 낮은 임계값
        var position = (double)index / segments.Count;
        
        if (position < 0.1 || position > 0.9)
        {
            return _boundaryThreshold * 0.8;
        }

        // 중간 부분은 표준 임계값
        return _boundaryThreshold;
    }

    private List<HybridBoundaryPoint> MergeNearbyBoundaries(
        List<HybridBoundaryPoint> boundaries,
        int mergeDistance)
    {
        if (boundaries.Count < 2) return boundaries;

        var merged = new List<HybridBoundaryPoint>();
        var lastAdded = boundaries[0];
        merged.Add(lastAdded);

        for (int i = 1; i < boundaries.Count; i++)
        {
            if (boundaries[i].SegmentIndex - lastAdded.SegmentIndex <= mergeDistance)
            {
                // 더 강한 경계 유지
                if (boundaries[i].HybridScore > lastAdded.HybridScore)
                {
                    merged[merged.Count - 1] = boundaries[i];
                    lastAdded = boundaries[i];
                }
            }
            else
            {
                merged.Add(boundaries[i]);
                lastAdded = boundaries[i];
            }
        }

        return merged;
    }

    private string GenerateReason(HybridBoundaryResult result)
    {
        var reasons = new List<string>();

        if (result.RawStatisticalScore > 50)
        {
            reasons.Add($"High uncertainty ({result.RawStatisticalScore:F1})");
        }

        if (result.RawSimilarity < 0.5)
        {
            reasons.Add($"Low similarity ({result.RawSimilarity:F2})");
        }

        reasons.Add($"{result.BoundaryType} boundary");

        return string.Join(", ", reasons);
    }
}