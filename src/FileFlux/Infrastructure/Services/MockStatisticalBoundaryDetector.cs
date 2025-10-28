#if DEBUG
namespace FileFlux.Infrastructure.Services;

/// <summary>
/// Mock implementation of statistical boundary detector for testing.
/// Simulates uncertainty calculation without actual LLM.
/// Only available in DEBUG builds - excluded from production Release builds.
/// </summary>
public class MockStatisticalBoundaryDetector : IStatisticalBoundaryDetector
{
    private double _baseThreshold = 50.0;
    private bool _useAdaptiveThreshold = true;
    private readonly Random _random = new();

    public double BaseThreshold
    {
        get => _baseThreshold;
        set => _baseThreshold = Math.Max(1, value);
    }

    public bool UseAdaptiveThreshold
    {
        get => _useAdaptiveThreshold;
        set => _useAdaptiveThreshold = value;
    }

    public Task<StatisticalBoundaryResult> CalculateUncertaintyAsync(
        string segment,
        string? context,
        ITextCompletionService textCompletionService,
        CancellationToken cancellationToken = default)
    {
        // 세그먼트와 컨텍스트 기반 모의 uncertainty 계산
        var uncertainty = CalculateMockUncertainty(segment, context);

        // 토큰 확률 시뮬레이션
        var tokens = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokenProbabilities = new List<TokenProbability>();

        double sumLogProb = 0;
        for (int i = 0; i < Math.Min(tokens.Length, 10); i++)
        {
            var logProb = -Math.Log(uncertainty) - _random.NextDouble() * 2;
            sumLogProb += logProb;

            tokenProbabilities.Add(new TokenProbability
            {
                Token = tokens[i],
                LogProbability = logProb,
                Position = i
            });
        }

        var avgLogProb = tokens.Length > 0 ? sumLogProb / tokens.Length : 0;

        return Task.FromResult(new StatisticalBoundaryResult
        {
            UncertaintyScore = uncertainty,
            TokenProbabilities = tokenProbabilities,
            AverageLogProbability = avgLogProb,
            TokenCount = tokens.Length,
            Confidence = CalculateConfidence(uncertainty),
            Metadata = new Dictionary<string, object>
            {
                ["Method"] = "Mock",
                ["HasContext"] = !string.IsNullOrEmpty(context)
            }
        });
    }

    public bool IsTopicBoundary(double uncertaintyScore, double? threshold = null)
    {
        var effectiveThreshold = threshold ?? (_useAdaptiveThreshold ? GetAdaptiveThreshold(uncertaintyScore) : _baseThreshold);
        return uncertaintyScore > effectiveThreshold;
    }

    public async Task<IEnumerable<StatisticalBoundaryPoint>> DetectBoundariesAsync(
        IList<string> segments,
        ITextCompletionService textCompletionService,
        CancellationToken cancellationToken = default)
    {
        var boundaries = new List<StatisticalBoundaryPoint>();
        double previousUncertainty = 0;

        for (int i = 0; i < segments.Count - 1; i++)
        {
            // 현재 세그먼트를 컨텍스트로, 다음 세그먼트의 uncertainty 계산
            var result = await CalculateUncertaintyAsync(
                segments[i + 1],
                segments[i],
                textCompletionService,
                cancellationToken);

            var delta = previousUncertainty > 0 ? result.UncertaintyScore - previousUncertainty : 0;
            var threshold = _useAdaptiveThreshold ? GetAdaptiveThreshold(result.UncertaintyScore) : _baseThreshold;
            var isBoundary = IsTopicBoundary(result.UncertaintyScore, threshold);

            if (isBoundary)
            {
                boundaries.Add(new StatisticalBoundaryPoint
                {
                    SegmentIndex = i,
                    UncertaintyScore = result.UncertaintyScore,
                    UncertaintyDelta = delta,
                    IsBoundary = true,
                    Confidence = result.Confidence,
                    ThresholdUsed = threshold
                });
            }

            previousUncertainty = result.UncertaintyScore;
        }

        return boundaries;
    }

    private double CalculateMockUncertainty(string segment, string? context)
    {
        // 기본 uncertainty
        double uncertainty = 20.0;

        // 컨텍스트가 없으면 uncertainty 증가
        if (string.IsNullOrEmpty(context))
        {
            uncertainty *= 2;
        }
        else
        {
            // 컨텍스트와 세그먼트 유사도 기반 조정
            var contextWords = new HashSet<string>(
                context.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var segmentWords = segment.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var overlap = segmentWords.Count(w => contextWords.Contains(w));
            var overlapRatio = segmentWords.Length > 0 ? (double)overlap / segmentWords.Length : 0;

            // 오버랩이 적으면 uncertainty 증가 (주제 변경)
            uncertainty *= (2 - overlapRatio);
        }

        // 구조적 변화 감지
        if (segment.StartsWith("#", StringComparison.Ordinal) || segment.Contains("Chapter") || segment.Contains("Section"))
        {
            uncertainty *= 1.5; // 섹션 변경
        }

        if (segment.Contains("```") || segment.Contains("def ") || segment.Contains("function "))
        {
            uncertainty *= 1.8; // 코드 블록
        }

        if (segment.Contains('|') && segment.Count(c => c == '|') > 3)
        {
            uncertainty *= 1.6; // 테이블
        }

        // 문장 복잡도
        var avgWordLength = CalculateAverageWordLength(segment);
        if (avgWordLength > 6)
        {
            uncertainty *= 1.2; // 복잡한 단어
        }

        // 랜덤 변동 추가 (현실성)
        uncertainty *= (0.8 + _random.NextDouble() * 0.4);

        return Math.Max(1, Math.Min(1000, uncertainty));
    }

    private double CalculateAverageWordLength(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;

        return words.Average(w => w.Length);
    }

    private double GetAdaptiveThreshold(double uncertainty)
    {
        // Uncertainty 수준에 따른 동적 임계값
        if (uncertainty < 10) return 20;
        if (uncertainty < 30) return 40;
        if (uncertainty < 50) return 60;
        return _baseThreshold;
    }

    private double CalculateConfidence(double uncertainty)
    {
        // Uncertainty가 극단적일수록 신뢰도 높음
        if (uncertainty < 5 || uncertainty > 100)
        {
            return 0.9;
        }
        if (uncertainty < 10 || uncertainty > 80)
        {
            return 0.7;
        }
        if (uncertainty < 20 || uncertainty > 60)
        {
            return 0.5;
        }
        return 0.3;
    }
}
#endif
