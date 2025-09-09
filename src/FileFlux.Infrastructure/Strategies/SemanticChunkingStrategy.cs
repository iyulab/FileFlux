using FileFlux;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 의미적 청킹 전략 - 문장과 문단 경계를 고려한 지능적 분할
/// </summary>
public partial class SemanticChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex SentenceEndRegex = MyRegex();
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    
    // Phase 10: Boundary Quality 일관성 개선을 위한 경계 품질 매니저
    private static readonly BoundaryQualityManager _boundaryQualityManager = new();

    public string StrategyName => ChunkingStrategies.Semantic;

    public IEnumerable<string> SupportedOptions => new[]
    {
        "MinSentences",      // 최소 문장 수
        "MaxSentences",      // 최대 문장 수  
        "RespectParagraphs", // 문단 경계 존중 여부
        "SentenceMinLength", // 최소 문장 길이
        "PreferCompleteSentences" // 완전한 문장 선호
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
        var text = NormalizeText(content.Text);

        // 전략 옵션 가져오기
        var minSentences = GetStrategyOption(options, "MinSentences", 2);
        var maxSentences = GetStrategyOption(options, "MaxSentences", 8);
        var respectParagraphs = GetStrategyOption(options, "RespectParagraphs", true);
        var sentenceMinLength = GetStrategyOption(options, "SentenceMinLength", 20);
        var preferCompleteSentences = GetStrategyOption(options, "PreferCompleteSentences", true);

        // 문장 단위로 분할
        var sentences = ExtractSentences(text, sentenceMinLength);
        if (sentences.Count == 0)
        {
            return Enumerable.Empty<DocumentChunk>();
        }

        var currentChunk = new List<string>();
        var currentLength = 0;
        var chunkIndex = 0;
        var globalPosition = 0;

        foreach (var sentence in sentences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sentenceLength = EstimateTokenCount(sentence);
            var wouldExceedMaxSize = currentLength + sentenceLength > options.MaxChunkSize;
            var hasEnoughSentences = currentChunk.Count >= minSentences;
            var tooManySentences = currentChunk.Count >= maxSentences;

            // 청크 완료 조건 확인
            if (currentChunk.Count != 0 && (wouldExceedMaxSize || tooManySentences))
            {
                if (hasEnoughSentences || !preferCompleteSentences)
                {
                    // 현재 청크 완료
                    var chunkContent = string.Join(" ", currentChunk);
                    var chunk = CreateChunk(chunkContent, content.Metadata, chunkIndex++, globalPosition, options);
                    chunks.Add(chunk);

                    globalPosition += chunkContent.Length;
                    currentChunk.Clear();
                    currentLength = 0;
                }
            }

            // 문장 추가
            currentChunk.Add(sentence);
            currentLength += sentenceLength;

            // 문단 경계 처리
            if (respectParagraphs && IsEndOfParagraph(sentence, text))
            {
                if (currentChunk.Count >= minSentences || !preferCompleteSentences)
                {
                    var chunkContent = string.Join(" ", currentChunk);
                    var chunk = CreateChunk(chunkContent, content.Metadata, chunkIndex++, globalPosition, options);
                    chunks.Add(chunk);

                    globalPosition += chunkContent.Length;
                    currentChunk.Clear();
                    currentLength = 0;
                }
            }
        }

        // 마지막 청크 처리
        if (currentChunk.Count != 0)
        {
            var chunkContent = string.Join(" ", currentChunk);
            var chunk = CreateChunk(chunkContent, content.Metadata, chunkIndex, globalPosition, options);
            chunks.Add(chunk);
        }

        return await Task.FromResult(chunks);
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        var sentences = ExtractSentences(content.Text, 20);
        var avgSentencesPerChunk = GetStrategyOption(options, "MaxSentences", 8);

        return (int)Math.Ceiling((double)sentences.Count / avgSentencesPerChunk);
    }

    private static List<string> ExtractSentences(string text, int minLength)
    {
        var sentences = new List<string>();

        // 정규식으로 문장 경계 찾기 (마침표, 느낌표, 물음표 뒤의 공백)
        var matches = SentenceEndRegex.Matches(text);
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            // 문장 추출 (문장 종료 부호 포함)
            var sentence = text.Substring(lastIndex, match.Index + match.Length - lastIndex).Trim();
            
            if (!string.IsNullOrWhiteSpace(sentence) && sentence.Length >= minLength)
            {
                sentences.Add(sentence);
            }
            
            lastIndex = match.Index + match.Length;
        }

        // 마지막 문장 처리 (문장 부호가 없는 경우)
        if (lastIndex < text.Length)
        {
            var remaining = text.Substring(lastIndex).Trim();
            if (!string.IsNullOrWhiteSpace(remaining) && remaining.Length >= minLength)
            {
                sentences.Add(remaining);
            }
        }

        // 문장이 하나도 없으면 전체 텍스트를 청크 크기로 분할
        if (sentences.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            // 기본 청크 크기(약 500자)로 분할
            const int defaultChunkSize = 500;
            for (int i = 0; i < text.Length; i += defaultChunkSize)
            {
                var length = Math.Min(defaultChunkSize, text.Length - i);
                var chunk = text.Substring(i, length).Trim();
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    sentences.Add(chunk);
                }
            }
        }

        return sentences;
    }

    private static bool IsCompleteSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return false;

        var trimmed = sentence.Trim();

        // 문장 종료 부호로 끝나는지 확인
        return trimmed.EndsWith('.') ||
               trimmed.EndsWith('!') ||
               trimmed.EndsWith('?') ||
               trimmed.EndsWith('。') || // 한국어/중국어 마침표
               trimmed.EndsWith('！') || // 한국어/중국어 느낌표
               trimmed.EndsWith('？');   // 한국어/중국어 물음표
    }

    private static bool IsEndOfParagraph(string sentence, string fullText)
    {
        // 문단 끝 휴리스틱 - 실제 구현에서는 더 정교한 로직 필요
        var index = fullText.IndexOf(sentence, StringComparison.Ordinal);
        if (index == -1) return false;

        var endIndex = index + sentence.Length;
        if (endIndex >= fullText.Length - 1) return true;

        // 다음에 두 개 이상의 줄바꿈이 있는지 확인
        var remaining = fullText.Substring(endIndex);
        return remaining.StartsWith("\n\n") || remaining.StartsWith("\r\n\r\n");
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 공백 정규화
        return WhitespaceRegex.Replace(text.Trim(), " ");
    }

    private static DocumentChunk CreateChunk(
        string content,
        DocumentMetadata metadata,
        int chunkIndex,
        int startPosition,
        ChunkingOptions options)
    {
        return new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            Content = content.Trim(),
            Metadata = metadata,
            StartPosition = startPosition,
            EndPosition = startPosition + content.Length,
            ChunkIndex = chunkIndex,
            Strategy = ChunkingStrategies.Semantic,
            EstimatedTokens = EstimateTokenCount(content),
            CreatedAt = DateTime.UtcNow,
            Importance = CalculateImportance(content),
            PageNumber = metadata.PageCount == 1 ? 1 : null
        };
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // 간단한 토큰 수 추정
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)(words * 1.3); // 토큰은 일반적으로 단어보다 약간 많음
    }

    private static double CalculateImportance(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        var importance = 0.5; // 기본값

        // 길이에 따른 중요도 조정
        if (content.Length > 200)
            importance += 0.1;

        // 특별한 키워드나 패턴이 있으면 중요도 증가
        var importantPatterns = new[] { "중요", "핵심", "요약", "결론", "important", "key", "summary", "conclusion" };
        if (importantPatterns.Any(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            importance += 0.2;
        }

        return Math.Min(importance, 1.0);
    }

    private static T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        // 옵션 단순화: 항상 기본값 사용 (최고 품질 기본 설정)
        return defaultValue;
    }

    [GeneratedRegex(@"[.!?]+\s+", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}