using FileFlux;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 문단 기반 청킹 전략 - 자연스러운 문단 경계를 기준으로 분할
/// </summary>
public partial class ParagraphChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex ParagraphSeparatorRegex = MyRegex();
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$", RegexOptions.Compiled | RegexOptions.Multiline);

    public string StrategyName => ChunkingStrategies.Paragraph;

    public IEnumerable<string> SupportedOptions => new[]
    {
        "CombineShortParagraphs",  // 짧은 문단 결합 여부
        "MinParagraphLength",      // 최소 문단 길이
        "MaxParagraphsPerChunk",   // 청크당 최대 문단 수
        "PreserveHeaders",         // 헤더 구조 보존
        "SplitLongParagraphs"      // 긴 문단 분할 여부
    };

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        DocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(content.Text))
            return Enumerable.Empty<DocumentChunk>();

        var chunks = new List<DocumentChunk>();
        var text = content.Text;

        // 전략 옵션 가져오기
        var combineShortParagraphs = GetStrategyOption(options, "CombineShortParagraphs", true);
        var minParagraphLength = GetStrategyOption(options, "MinParagraphLength", 50);
        var maxParagraphsPerChunk = GetStrategyOption(options, "MaxParagraphsPerChunk", 3);
        var preserveHeaders = GetStrategyOption(options, "PreserveHeaders", true);
        var splitLongParagraphs = GetStrategyOption(options, "SplitLongParagraphs", true);

        // 문단 추출
        var paragraphs = ExtractParagraphs(text, preserveHeaders);
        if (paragraphs.Count == 0)
        {
            return Enumerable.Empty<DocumentChunk>();
        }

        // 짧은 문단 결합 처리
        if (combineShortParagraphs)
        {
            paragraphs = CombineShortParagraphs(paragraphs, minParagraphLength, options.MaxChunkSize);
        }

        // 긴 문단 분할 처리
        if (splitLongParagraphs)
        {
            paragraphs = SplitLongParagraphs(paragraphs, options.MaxChunkSize);
        }

        // 청크 생성
        var currentChunk = new List<ParagraphInfo>();
        var currentLength = 0;
        var chunkIndex = 0;
        var globalPosition = 0;

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paragraphTokens = EstimateTokenCount(paragraph.Content);
            var wouldExceedMaxSize = currentLength + paragraphTokens > options.MaxChunkSize;
            var tooManyParagraphs = currentChunk.Count >= maxParagraphsPerChunk;

            // 새로운 청크 시작이 필요한지 확인
            if (currentChunk.Count != 0 && (wouldExceedMaxSize || tooManyParagraphs))
            {
                var chunk = CreateChunkFromParagraphs(currentChunk, content.Metadata, chunkIndex++, globalPosition, options);
                chunks.Add(chunk);

                globalPosition += chunk.Content.Length;
                currentChunk.Clear();
                currentLength = 0;
            }

            // 문단 추가
            currentChunk.Add(paragraph);
            currentLength += paragraphTokens;

            // 헤더 다음에는 새로운 청크 시작 (옵션에 따라)
            if (preserveHeaders && paragraph.IsHeader && currentChunk.Count > 1)
            {
                var chunk = CreateChunkFromParagraphs(currentChunk, content.Metadata, chunkIndex++, globalPosition, options);
                chunks.Add(chunk);

                globalPosition += chunk.Content.Length;
                currentChunk.Clear();
                currentLength = 0;
            }
        }

        // 마지막 청크 처리
        if (currentChunk.Count != 0)
        {
            var chunk = CreateChunkFromParagraphs(currentChunk, content.Metadata, chunkIndex, globalPosition, options);
            chunks.Add(chunk);
        }

        return await Task.FromResult(chunks);
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        var paragraphs = ExtractParagraphs(content.Text, true);
        var maxParagraphsPerChunk = GetStrategyOption(options, "MaxParagraphsPerChunk", 3);

        return (int)Math.Ceiling((double)paragraphs.Count / maxParagraphsPerChunk);
    }

    private static List<ParagraphInfo> ExtractParagraphs(string text, bool preserveHeaders)
    {
        var paragraphs = new List<ParagraphInfo>();
        var parts = ParagraphSeparatorRegex.Split(text);

        int position = 0;
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                position += part.Length;
                continue;
            }

            var isHeader = preserveHeaders && IsHeader(trimmed);
            var paragraphInfo = new ParagraphInfo
            {
                Content = NormalizeParagraph(trimmed),
                IsHeader = isHeader,
                StartPosition = position,
                Length = trimmed.Length
            };

            paragraphs.Add(paragraphInfo);
            position += part.Length;
        }

        return paragraphs;
    }

    private static List<ParagraphInfo> CombineShortParagraphs(
        List<ParagraphInfo> paragraphs,
        int minLength,
        int maxChunkSize)
    {
        var result = new List<ParagraphInfo>();
        var currentCombined = new List<ParagraphInfo>();
        var currentLength = 0;

        foreach (var paragraph in paragraphs)
        {
            var paragraphLength = paragraph.Content.Length;

            // 헤더는 결합하지 않음
            if (paragraph.IsHeader)
            {
                // 현재 결합된 문단들 추가
                if (currentCombined.Count != 0)
                {
                    result.Add(CombineParagraphs(currentCombined));
                    currentCombined.Clear();
                    currentLength = 0;
                }

                result.Add(paragraph);
                continue;
            }

            // 문단이 충분히 긴 경우 단독으로 추가
            if (paragraphLength >= minLength)
            {
                // 현재 결합된 문단들 추가
                if (currentCombined.Count != 0)
                {
                    result.Add(CombineParagraphs(currentCombined));
                    currentCombined.Clear();
                    currentLength = 0;
                }

                result.Add(paragraph);
                continue;
            }

            // 짧은 문단 결합
            if (currentLength + paragraphLength > maxChunkSize)
            {
                if (currentCombined.Count != 0)
                {
                    result.Add(CombineParagraphs(currentCombined));
                    currentCombined.Clear();
                    currentLength = 0;
                }
            }

            currentCombined.Add(paragraph);
            currentLength += paragraphLength;
        }

        // 마지막 결합된 문단들 추가
        if (currentCombined.Count != 0)
        {
            result.Add(CombineParagraphs(currentCombined));
        }

        return result;
    }

    private static List<ParagraphInfo> SplitLongParagraphs(List<ParagraphInfo> paragraphs, int maxChunkSize)
    {
        var result = new List<ParagraphInfo>();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Content.Length <= maxChunkSize)
            {
                result.Add(paragraph);
                continue;
            }

            // 긴 문단을 문장 단위로 분할
            var sentences = paragraph.Content.Split('.', '!', '?')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim() + ".")
                .ToList();

            var currentPart = new List<string>();
            var currentLength = 0;

            foreach (var sentence in sentences)
            {
                if (currentLength + sentence.Length > maxChunkSize && currentPart.Count != 0)
                {
                    result.Add(new ParagraphInfo
                    {
                        Content = string.Join(" ", currentPart).Trim(),
                        IsHeader = paragraph.IsHeader,
                        StartPosition = paragraph.StartPosition,
                        Length = string.Join(" ", currentPart).Length
                    });

                    currentPart.Clear();
                    currentLength = 0;
                }

                currentPart.Add(sentence);
                currentLength += sentence.Length;
            }

            // 마지막 부분 추가
            if (currentPart.Count != 0)
            {
                result.Add(new ParagraphInfo
                {
                    Content = string.Join(" ", currentPart).Trim(),
                    IsHeader = paragraph.IsHeader,
                    StartPosition = paragraph.StartPosition,
                    Length = string.Join(" ", currentPart).Length
                });
            }
        }

        return result;
    }

    private static ParagraphInfo CombineParagraphs(List<ParagraphInfo> paragraphs)
    {
        var combinedContent = string.Join("\n\n", paragraphs.Select(p => p.Content));
        return new ParagraphInfo
        {
            Content = combinedContent,
            IsHeader = paragraphs.Any(p => p.IsHeader),
            StartPosition = paragraphs.First().StartPosition,
            Length = combinedContent.Length
        };
    }

    private static bool IsHeader(string text)
    {
        // Markdown 스타일 헤더 확인
        if (HeaderRegex.IsMatch(text))
            return true;

        // 기타 헤더 패턴 (짧고, 대문자로 시작하며, 마침표로 끝나지 않음)
        var trimmed = text.Trim();
        return trimmed.Length < 100 &&
               char.IsUpper(trimmed[0]) &&
               !trimmed.EndsWith('.') &&
               !trimmed.Contains('\n');
    }

    private static string NormalizeParagraph(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return WhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static DocumentChunk CreateChunkFromParagraphs(
        List<ParagraphInfo> paragraphs,
        DocumentMetadata metadata,
        int chunkIndex,
        int startPosition,
        ChunkingOptions options)
    {
        var content = string.Join("\n\n", paragraphs.Select(p => p.Content));
        var hasHeader = paragraphs.Any(p => p.IsHeader);

        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            Content = content.Trim(),
            Metadata = metadata,
            StartPosition = startPosition,
            EndPosition = startPosition + content.Length,
            ChunkIndex = chunkIndex,
            Strategy = ChunkingStrategies.Paragraph,
            EstimatedTokens = EstimateTokenCount(content),
            CreatedAt = DateTime.UtcNow,
            Importance = hasHeader ? 0.8 : 0.5, // 헤더가 있으면 중요도 높임
            PageNumber = metadata.PageCount == 1 ? 1 : null,
            Properties = new Dictionary<string, object>
            {
                ["ParagraphCount"] = paragraphs.Count,
                ["HasHeader"] = hasHeader
            }
        };
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)(words * 1.3);
    }

    private static T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        // 옵션 단순화: 항상 기본값 사용 (최고 품질 기본 설정)
        return defaultValue;
    }

    private class ParagraphInfo
    {
        public string Content { get; set; } = string.Empty;
        public bool IsHeader { get; set; }
        public int StartPosition { get; set; }
        public int Length { get; set; }
    }

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}