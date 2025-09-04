using FileFlux.Core;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 고정 크기 청킹 전략 구현
/// </summary>
public class FixedSizeChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex WordBoundaryRegex = new(@"\b", RegexOptions.Compiled);

    public string StrategyName => ChunkingStrategies.FixedSize;

    public IEnumerable<string> SupportedOptions => new[]
    {
        "RespectWordBoundaries",
        "MinSentenceCount",
        "MaxSentenceCount"
    };

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (content == null)
            throw new ArgumentNullException(nameof(content));

        if (string.IsNullOrWhiteSpace(content.Text))
            return Enumerable.Empty<DocumentChunk>();

        var chunks = new List<DocumentChunk>();
        var text = content.Text;
        var maxChunkSize = options.MaxChunkSize;
        var overlapSize = Math.Min(options.OverlapSize, maxChunkSize / 2);
        var respectWordBoundaries = GetStrategyOption<bool>(options, "RespectWordBoundaries", true);

        var currentPosition = 0;
        var chunkIndex = 0;

        while (currentPosition < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkText = ExtractChunk(text, currentPosition, maxChunkSize, respectWordBoundaries);

            if (string.IsNullOrWhiteSpace(chunkText))
                break;

            var actualChunkLength = chunkText.TrimEnd().Length;
            if (actualChunkLength == 0)
                break;

            // 청크 생성
            var chunk = new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = chunkText.Trim(),
                Metadata = content.Metadata,
                StartPosition = currentPosition,
                EndPosition = currentPosition + actualChunkLength,
                ChunkIndex = chunkIndex++,
                Strategy = StrategyName,
                EstimatedTokens = EstimateTokenCount(chunkText),
                CreatedAt = DateTime.UtcNow
            };

            // 페이지 정보 추가 (단일 페이지 문서의 경우)
            if (content.Metadata.PageCount == 1)
            {
                chunk.PageNumber = 1;
            }

            chunks.Add(chunk);

            // 다음 시작 위치 계산 (겹침 고려)
            var nextStart = currentPosition + actualChunkLength - overlapSize;
            if (nextStart <= currentPosition)
            {
                nextStart = currentPosition + Math.Max(1, actualChunkLength / 2);
            }
            currentPosition = nextStart;
        }

        return await Task.FromResult(chunks);
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        var textLength = content.Text.Length;
        var chunkSize = options.MaxChunkSize;
        var overlapSize = Math.Min(options.OverlapSize, chunkSize / 2);
        var effectiveChunkSize = chunkSize - overlapSize;

        return (int)Math.Ceiling((double)textLength / effectiveChunkSize);
    }

    private static string ExtractChunk(string text, int startPosition, int maxChunkSize, bool respectWordBoundaries)
    {
        if (startPosition >= text.Length)
            return string.Empty;

        var endPosition = Math.Min(startPosition + maxChunkSize, text.Length);
        var chunkText = text.Substring(startPosition, endPosition - startPosition);

        // 단어 경계 존중
        if (respectWordBoundaries && endPosition < text.Length)
        {
            var lastSpaceIndex = chunkText.LastIndexOf(' ');
            var lastNewlineIndex = chunkText.LastIndexOf('\n');
            var lastBoundaryIndex = Math.Max(lastSpaceIndex, lastNewlineIndex);

            if (lastBoundaryIndex > chunkText.Length / 2) // 절반 이상인 경우만 단어 경계 적용
            {
                chunkText = chunkText.Substring(0, lastBoundaryIndex);
            }
        }

        return chunkText;
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // 간단한 토큰 수 추정 (실제로는 더 정교한 토큰화가 필요)
        var words = WordBoundaryRegex.Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Count();

        // 일반적으로 토큰 수는 단어 수보다 약간 많음
        return (int)(words * 1.3);
    }

    private static T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        // 옵션 단순화: 항상 기본값 사용 (최고 품질 기본 설정)
        return defaultValue;
    }
}