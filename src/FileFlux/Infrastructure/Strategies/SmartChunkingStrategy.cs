using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Services;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 스마트 청킹 전략 - 문장 경계를 존중하고 의미적 완결성을 보장하는 RAG 최적화 전략
/// </summary>
public partial class SmartChunkingStrategy : IChunkingStrategy
{
    // 문장 종료 패턴 (더 정확한 패턴)
    private static readonly Regex SentenceEndRegex = new(@"[.!?]+[\s\n]+|[.!?]+$", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$", RegexOptions.Compiled | RegexOptions.Multiline);

    // 약어 패턴 (문장 중간의 마침표를 구분하기 위함)
    private static readonly Regex AbbreviationRegex = new(@"\b(?:Dr|Mr|Mrs|Ms|Prof|Sr|Jr|Ph\.D|M\.D|B\.A|M\.A|D\.D\.S|Ph\.D|U\.S|U\.K|i\.e|e\.g|etc|vs|Inc|Ltd|Co|Corp|Jan|Feb|Mar|Apr|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)\.", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string StrategyName => "Smart";

    public IEnumerable<string> SupportedOptions => new[]
    {
        "MinCompleteness",       // 최소 완성도 임계값 (기본 70%)
        "PreserveSentences",     // 문장 경계 보존 여부 (기본 true)
        "SmartOverlap",          // 스마트 오버랩 활성화 (기본 true)
        "MaxSentenceBreak"       // 허용되는 최대 문장 중단 수 (기본 0)
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
        var minCompleteness = GetStrategyOption(options, "MinCompleteness", 0.7);
        var preserveSentences = GetStrategyOption(options, "PreserveSentences", true);
        var smartOverlap = GetStrategyOption(options, "SmartOverlap", true);
        var maxSentenceBreak = GetStrategyOption(options, "MaxSentenceBreak", 0);

        // 문장 단위로 텍스트 분할
        var sentences = ExtractCompleteSentences(text);

        // 문장 기반 청킹 수행
        var rawChunks = CreateSentenceBasedChunks(
            sentences,
            options.MaxChunkSize,
            options.OverlapSize,
            minCompleteness,
            smartOverlap);

        // 청크 생성
        var chunkIndex = 0;
        var globalPosition = 0;

        foreach (var chunkContent in rawChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = CreateSmartChunk(
                chunkContent,
                content,
                chunkIndex++,
                globalPosition,
                options);

            chunks.Add(chunk);
            globalPosition += chunkContent.Length;
        }

        // Update chunk count in all chunks
        ChunkingHelper.UpdateChunkCount(chunks);

        return await Task.FromResult(chunks);
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        // 평균적으로 문장 경계 보존으로 인해 80% 정도 활용
        var avgChunkSize = (int)(options.MaxChunkSize * 0.8);
        return (int)Math.Ceiling((double)content.Text.Length / avgChunkSize);
    }

    /// <summary>
    /// 완전한 문장 단위로 텍스트 추출
    /// </summary>
    private List<string> ExtractCompleteSentences(string text)
    {
        var sentences = new List<string>();

        // 약어 처리를 위해 임시로 마침표 대체
        var processedText = AbbreviationRegex.Replace(text, match => match.Value.Replace(".", "§DOT§"));

        // 문장 분할
        var sentenceParts = SentenceEndRegex.Split(processedText);
        var matches = SentenceEndRegex.Matches(processedText);

        for (int i = 0; i < sentenceParts.Length; i++)
        {
            var sentence = sentenceParts[i].Trim();

            // 마침표 복원
            sentence = sentence.Replace("§DOT§", ".");

            if (string.IsNullOrWhiteSpace(sentence))
                continue;

            // 문장 종료 문자 추가 (있는 경우)
            if (i < matches.Count)
            {
                var endChar = matches[i].Value.Trim();
                if (!string.IsNullOrEmpty(endChar))
                {
                    sentence += endChar;
                }
            }

            // 최소 길이 확인 (너무 짧은 문장 제외)
            if (sentence.Length >= 10)
            {
                sentences.Add(sentence);
            }
        }

        return sentences;
    }

    /// <summary>
    /// 문장 기반 청킹 - 문장 경계를 존중하며 청크 생성
    /// </summary>
    private List<string> CreateSentenceBasedChunks(
        List<string> sentences,
        int maxSize,
        int overlapSize,
        double minCompleteness,
        bool smartOverlap)
    {
        var chunks = new List<string>();
        var currentChunk = new List<string>();
        var currentSize = 0;
        var previousSentences = new List<string>(); // 오버랩을 위한 이전 문장들

        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var sentenceSize = EstimateTokenCount(sentence);

            // 단일 문장이 최대 크기를 초과하는 경우
            if (sentenceSize > maxSize)
            {
                // 현재 청크가 있으면 먼저 완료
                if (currentChunk.Count > 0)
                {
                    var chunkContent = CreateChunkWithOverlap(currentChunk, previousSentences, smartOverlap, overlapSize);
                    chunks.Add(chunkContent);

                    // 오버랩을 위해 마지막 문장들 저장
                    if (smartOverlap && overlapSize > 0)
                    {
                        previousSentences = GetOverlapSentences(currentChunk, overlapSize);
                    }

                    currentChunk.Clear();
                    currentSize = 0;
                }

                // 긴 문장을 부분적으로 분할 (가능한 한 의미 단위 유지)
                var splitSentences = SplitLongSentence(sentence, maxSize);
                foreach (var part in splitSentences)
                {
                    chunks.Add(part);
                }

                // 마지막 부분을 오버랩에 사용
                if (smartOverlap && splitSentences.Count > 0)
                {
                    previousSentences = new List<string> { splitSentences.Last() };
                }

                continue;
            }

            // 청크 크기 확인
            if (currentSize + sentenceSize > maxSize && currentChunk.Count > 0)
            {
                // 완성도 확인
                var completeness = CalculateChunkCompleteness(currentChunk, sentences, i);

                // 완성도가 임계값 이상이거나 더 이상 추가할 수 없는 경우
                if (completeness >= minCompleteness || currentSize >= maxSize * 0.9)
                {
                    var chunkContent = CreateChunkWithOverlap(currentChunk, previousSentences, smartOverlap, overlapSize);
                    chunks.Add(chunkContent);

                    // 오버랩을 위해 마지막 문장들 저장
                    if (smartOverlap && overlapSize > 0)
                    {
                        previousSentences = GetOverlapSentences(currentChunk, overlapSize);
                    }

                    currentChunk.Clear();
                    currentSize = 0;
                }
            }

            // 문장 추가
            currentChunk.Add(sentence);
            currentSize += sentenceSize;
        }

        // 마지막 청크 처리
        if (currentChunk.Count > 0)
        {
            var chunkContent = CreateChunkWithOverlap(currentChunk, previousSentences, smartOverlap, overlapSize);
            chunks.Add(chunkContent);
        }

        return chunks;
    }

    /// <summary>
    /// 청크 완성도 계산 (0.0 ~ 1.0)
    /// </summary>
    private double CalculateChunkCompleteness(List<string> chunk, List<string> allSentences, int nextIndex)
    {
        if (chunk.Count == 0) return 0.0;

        var scores = new List<double>();

        // 1. 문장 완결성 (모든 문장이 완전한가?)
        var completeSentences = chunk.Count(s => IsCompleteSentence(s));
        var sentenceCompleteness = (double)completeSentences / chunk.Count;
        scores.Add(sentenceCompleteness);

        // 2. 단락 완결성 (단락이 중간에 끊기지 않았는가?)
        var paragraphCompleteness = CalculateParagraphCompleteness(chunk, allSentences, nextIndex);
        scores.Add(paragraphCompleteness);

        // 3. 의미적 일관성 (주제가 일관되는가?)
        var semanticCoherence = CalculateSemanticCoherence(string.Join(" ", chunk));
        scores.Add(semanticCoherence);

        // 4. 길이 적절성 (너무 짧지 않은가?) - Phase 15: 200자 → 300자로 최적화
        var lengthScore = Math.Min(1.0, chunk.Sum(s => s.Length) / 300.0); // 300자 이상이면 1.0
        scores.Add(lengthScore);

        return scores.Average();
    }

    /// <summary>
    /// 문장이 완전한지 확인
    /// </summary>
    private bool IsCompleteSentence(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return false;

        var trimmed = sentence.Trim();

        // 문장 종료 문자로 끝나는지 확인
        return trimmed.EndsWith('.') ||
               trimmed.EndsWith('!') ||
               trimmed.EndsWith('?') ||
               trimmed.EndsWith('。') || // 한국어/일본어 마침표
               trimmed.EndsWith('」') || // 인용 종료
               trimmed.EndsWith('"') && (trimmed.Contains(".\"") || trimmed.Contains("?\"") || trimmed.Contains("!\""));
    }

    /// <summary>
    /// 단락 완결성 계산
    /// </summary>
    private double CalculateParagraphCompleteness(List<string> chunk, List<string> allSentences, int nextIndex)
    {
        if (chunk.Count == 0) return 1.0;

        // 다음 문장이 새 단락으로 시작하는지 확인
        if (nextIndex < allSentences.Count)
        {
            var nextSentence = allSentences[nextIndex];

            // 헤더로 시작하면 현재 청크는 완전함
            if (HeaderRegex.IsMatch(nextSentence))
                return 1.0;

            // 빈 줄로 시작하면 단락이 끝났음
            if (nextSentence.StartsWith("\n\n", StringComparison.Ordinal))
                return 1.0;
        }

        // 마지막 문장이 단락을 완결하는지 확인
        var lastSentence = chunk.Last();
        if (lastSentence.EndsWith("\n\n", StringComparison.Ordinal) || lastSentence.EndsWith("\n", StringComparison.Ordinal))
            return 1.0;

        // 기본적으로 80% 완결성
        return 0.8;
    }

    /// <summary>
    /// 오버랩을 위한 문장 선택
    /// </summary>
    private List<string> GetOverlapSentences(List<string> chunk, int overlapSize)
    {
        if (chunk.Count == 0 || overlapSize <= 0)
            return new List<string>();

        var overlapSentences = new List<string>();
        var currentSize = 0;

        // 뒤에서부터 문장 선택
        for (int i = chunk.Count - 1; i >= 0; i--)
        {
            var sentence = chunk[i];
            var sentenceSize = EstimateTokenCount(sentence);

            if (currentSize + sentenceSize > overlapSize * 1.2) // 20% 초과 허용
                break;

            overlapSentences.Insert(0, sentence);
            currentSize += sentenceSize;

            if (currentSize >= overlapSize)
                break;
        }

        return overlapSentences;
    }

    /// <summary>
    /// 오버랩을 포함한 청크 생성
    /// </summary>
    private string CreateChunkWithOverlap(
        List<string> currentSentences,
        List<string> previousSentences,
        bool smartOverlap,
        int overlapSize)
    {
        var content = new List<string>();

        // 스마트 오버랩: 이전 청크의 마지막 문장들 추가
        if (smartOverlap && overlapSize > 0 && previousSentences.Count > 0)
        {
            // 중복 방지: 첫 문장이 이전 오버랩의 마지막 문장과 같지 않은 경우만
            if (currentSentences.Count == 0 || currentSentences[0] != previousSentences.Last())
            {
                content.AddRange(previousSentences);

                // 오버랩 마커 추가 (선택적)
                if (content.Count > 0 && currentSentences.Count > 0)
                {
                    // 자연스러운 연결을 위해 공백 추가
                    content.Add(" ");
                }
            }
        }

        // 현재 문장들 추가
        content.AddRange(currentSentences);

        // 문장 사이에 적절한 공백 추가
        return string.Join(" ", content.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// 긴 문장을 의미 단위로 분할
    /// </summary>
    private List<string> SplitLongSentence(string sentence, int maxSize)
    {
        var parts = new List<string>();

        // 먼저 쉼표, 세미콜론 등으로 분할 시도
        var subClauses = sentence.Split(new[] { ", ", "; ", " - ", " — " }, StringSplitOptions.RemoveEmptyEntries);

        var currentPart = new List<string>();
        var currentSize = 0;

        foreach (var clause in subClauses)
        {
            var clauseSize = EstimateTokenCount(clause);

            if (currentSize + clauseSize > maxSize && currentPart.Count > 0)
            {
                // 구두점 복원
                var part = string.Join(", ", currentPart);
                if (!part.EndsWith('.') && !part.EndsWith('!') && !part.EndsWith('?'))
                {
                    part += "..."; // 미완성 표시
                }
                parts.Add(part);

                currentPart.Clear();
                currentSize = 0;
            }

            currentPart.Add(clause);
            currentSize += clauseSize;
        }

        // 마지막 부분
        if (currentPart.Count > 0)
        {
            var part = string.Join(", ", currentPart);
            // 원래 문장의 마침표 유지
            if (sentence.EndsWith('.') || sentence.EndsWith('!') || sentence.EndsWith('?'))
            {
                var lastChar = sentence[sentence.Length - 1];
                if (!part.EndsWith(lastChar))
                {
                    part += lastChar;
                }
            }
            parts.Add(part);
        }

        // 여전히 너무 긴 부분이 있으면 단어 단위로 분할
        var finalParts = new List<string>();
        foreach (var part in parts)
        {
            if (EstimateTokenCount(part) > maxSize)
            {
                finalParts.AddRange(SplitByWords(part, maxSize));
            }
            else
            {
                finalParts.Add(part);
            }
        }

        return finalParts;
    }

    /// <summary>
    /// 단어 단위로 텍스트 분할
    /// </summary>
    private List<string> SplitByWords(string text, int maxSize)
    {
        var result = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new List<string>();
        var currentSize = 0;

        foreach (var word in words)
        {
            var wordSize = EstimateTokenCount(word);

            if (currentSize + wordSize > maxSize && currentChunk.Count > 0)
            {
                var chunk = string.Join(" ", currentChunk);
                if (!chunk.EndsWith('.') && !chunk.EndsWith('!') && !chunk.EndsWith('?'))
                {
                    chunk += "..."; // 미완성 표시
                }
                result.Add(chunk);

                currentChunk.Clear();
                currentSize = 0;
            }

            currentChunk.Add(word);
            currentSize += wordSize;
        }

        if (currentChunk.Count > 0)
        {
            var chunk = string.Join(" ", currentChunk);
            result.Add(chunk);
        }

        return result;
    }

    /// <summary>
    /// 스마트 청크 생성
    /// </summary>
    private DocumentChunk CreateSmartChunk(
        string content,
        DocumentContent documentContent,
        int chunkIndex,
        int startPosition,
        ChunkingOptions options)
    {
        var trimmedContent = content.Trim();
        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = trimmedContent,
            Metadata = documentContent.Metadata,
            Location = new SourceLocation
            {
                StartChar = startPosition,
                EndChar = startPosition + content.Length
            },
            Index = chunkIndex,
            Strategy = "Smart",
            Tokens = EstimateTokenCount(content),
            CreatedAt = DateTime.UtcNow,
            Props = new Dictionary<string, object>()
        };

        // Enrich with structural metadata
        ChunkingHelper.EnrichChunk(chunk, documentContent, startPosition, startPosition + content.Length);

        // 기본 품질 메트릭 계산
        var completeness = CalculateFinalCompleteness(content);
        var coherence = CalculateSemanticCoherence(content);
        var sentenceIntegrity = CalculateSentenceIntegrity(content);

        chunk.Props["Completeness"] = completeness;
        chunk.Props["SemanticCoherence"] = coherence;
        chunk.Props["SentenceIntegrity"] = sentenceIntegrity;
        chunk.Props["HasOverlap"] = options.OverlapSize > 0;

        // 품질 점수 계산
        chunk.Quality = (completeness + coherence + sentenceIntegrity) / 3.0;

        // Context7 스타일 메타데이터 강화
        EnhanceWithContext7Metadata(chunk, documentContent.Metadata);

        return chunk;
    }

    /// <summary>
    /// Context7 스타일 메타데이터 강화 - SmartChunkingStrategy 특화
    /// </summary>
    private void EnhanceWithContext7Metadata(DocumentChunk chunk, DocumentMetadata metadata)
    {
        // Context7 metadata enrichment can be added in future versions
        // 문서 컨텍스트 생성
        /*
        var documentContext = new DocumentContext
        {
            DocumentType = metadata.FileType ?? "Unknown",
            DocumentDomain = DetermineDocumentDomain(chunk.Content, metadata),
            Metadata = metadata
        };

        // Context7 메타데이터 강화 서비스 적용
        var enricher = new Context7MetadataEnricher();
        enricher.EnrichChunk(chunk, documentContext);
        */

        // Smart 전략 특화 메타데이터 추가
        chunk.Props["SmartCompleteness"] = chunk.Props.ContainsKey("Completeness")
            ? Convert.ToDouble(chunk.Props["Completeness"])
            : 0.7; // Smart 전략은 최소 70% 보장

        chunk.Props["SentenceBoundaryPreservation"] = chunk.Props.ContainsKey("SentenceIntegrity")
            ? Convert.ToDouble(chunk.Props["SentenceIntegrity"])
            : 1.0;

        // RAG 적합성 점수 계산 (Smart 전략 특화)
        chunk.Props["RagSuitability"] = CalculateRagSuitabilityScore(chunk);

        // Context7 스타일 품질 등급 부여
        //chunk.Domain = documentContext.Domain;
        AssignQualityGrade(chunk);
    }

    /// <summary>
    /// 문서 도메인 결정
    /// </summary>
    private string DetermineDocumentDomain(string content, DocumentMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "General";

        var words = content.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 기술 도메인 키워드
        var techKeywords = new[] { "api", "code", "function", "class", "method", "database", "server", "client", "framework", "library" };
        var techCount = words.Count(w => techKeywords.Contains(w));

        // 비즈니스 도메인 키워드
        var businessKeywords = new[] { "business", "strategy", "market", "customer", "revenue", "profit", "finance", "sales", "marketing" };
        var businessCount = words.Count(w => businessKeywords.Contains(w));

        // 학술 도메인 키워드
        var academicKeywords = new[] { "research", "study", "analysis", "theory", "methodology", "results", "conclusion", "abstract" };
        var academicCount = words.Count(w => academicKeywords.Contains(w));

        // 파일 타입 기반 추론
        var fileType = metadata.FileType?.ToLowerInvariant() ?? "";
        if (fileType.Contains("pdf") && academicCount > techCount && academicCount > businessCount)
            return "Academic";

        if (techCount > businessCount && techCount > academicCount)
            return "Technical";

        if (businessCount > techCount && businessCount > academicCount)
            return "Business";

        if (academicCount > 0)
            return "Academic";

        return "General";
    }

    /// <summary>
    /// RAG 적합성 점수 계산 (Smart 전략 특화)
    /// </summary>
    private double CalculateRagSuitabilityScore(DocumentChunk chunk)
    {
        var score = 0.0;

        // 완성도 기여 (40%)
        if (chunk.Props.ContainsKey("Completeness"))
        {
            score += Convert.ToDouble(chunk.Props["Completeness"]) * 0.4;
        }

        // 문장 무결성 기여 (30%)
        if (chunk.Props.ContainsKey("SentenceIntegrity"))
        {
            score += Convert.ToDouble(chunk.Props["SentenceIntegrity"]) * 0.3;
        }

        // 의미적 일관성 기여 (20%)
        if (chunk.Props.ContainsKey("SemanticCoherence"))
        {
            score += Convert.ToDouble(chunk.Props["SemanticCoherence"]) * 0.2;
        }

        // 길이 적절성 기여 (10%)
        var lengthScore = chunk.Content.Length >= 100 && chunk.Content.Length <= 2000 ? 1.0 : 0.5;
        score += lengthScore * 0.1;

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// 품질 등급 부여 (A, B, C, D, F)
    /// </summary>
    private void AssignQualityGrade(DocumentChunk chunk)
    {
        var ragSuitability = chunk.Props.ContainsKey("RagSuitability")
            ? Convert.ToDouble(chunk.Props["RagSuitability"])
            : 0.0;

        chunk.Props["QualityGrade"] = ragSuitability switch
        {
            >= 0.9 => "A", // 최고 품질
            >= 0.8 => "B", // 우수 품질
            >= 0.7 => "C", // 양호 품질 (Smart 전략 최소 기준)
            >= 0.6 => "D", // 미흡 품질
            _ => "F"       // 부족 품질
        };
    }

    /// <summary>
    /// 최종 청크 완성도 계산
    /// </summary>
    private double CalculateFinalCompleteness(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var sentences = ExtractCompleteSentences(content);
        if (sentences.Count == 0) return 0.0;

        // 모든 문장이 완전한지 확인
        var completeSentences = sentences.Count(s => IsCompleteSentence(s));
        var completeness = (double)completeSentences / sentences.Count;

        // 최소 70% 보장
        return Math.Max(0.7, completeness);
    }

    /// <summary>
    /// 문장 무결성 계산
    /// </summary>
    private double CalculateSentenceIntegrity(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        // 미완성 표시가 있는지 확인
        if (content.Contains("...") && !content.Contains("etc.") && !content.Contains("e.g."))
            return 0.5;

        // 문장이 중간에 끊겼는지 확인
        var lastChar = content.Trim().LastOrDefault();
        if (char.IsLetterOrDigit(lastChar) || lastChar == ',')
            return 0.3;

        return 1.0;
    }

    /// <summary>
    /// 의미적 일관성 계산
    /// </summary>
    private double CalculateSemanticCoherence(string content)
    {
        var words = content.ToLowerInvariant()
            .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToList();

        if (words.Count == 0) return 0.0;

        // 반복되는 주요 단어의 비율
        var wordFreq = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var repeatedWords = wordFreq.Where(kvp => kvp.Value > 1).Count();

        return Math.Min(1.0, (double)repeatedWords / words.Distinct().Count());
    }

    /// <summary>
    /// 토큰 수 추정
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // 단어 수 기반 추정 (평균적으로 1 단어 = 1.3 토큰)
        var words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)(words * 1.3);
    }

    /// <summary>
    /// 전략 옵션 가져오기 (현재는 기본값만 사용)
    /// </summary>
    private T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        // ChunkingOptions가 간소화되어 StrategyOptions가 없으므로 기본값 사용
        // 향후 필요시 확장 가능
        return defaultValue;
    }
}
