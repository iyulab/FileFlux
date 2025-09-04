using FileFlux.Core;
using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 지능형 청킹 전략 - RAG 시스템에 최적화된 컨텍스트 인식 분할
/// </summary>
public class IntelligentChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex SentenceEndRegex = new(@"[.!?]+\s+", RegexOptions.Compiled);
    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$|^.+\n[=\-]+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ListItemRegex = new(@"^\s*[-*+]\s+|^\s*\d+\.\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ImportantKeywordRegex = new(@"\b(중요|핵심|요약|결론|참고|주의|경고|important|key|summary|conclusion|note|warning|attention)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 테이블 감지를 위한 정규식 추가
    private static readonly Regex TableRegex = new(@"^\s*\|[^|]+\|[^|]+\|", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex TableSeparatorRegex = new(@"^\s*\|[\s\-\|]+\|", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownSectionRegex = new(@"^#{1,6}\s+.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    public string StrategyName => ChunkingStrategies.Intelligent;

    public IEnumerable<string> SupportedOptions => new[]
    {
        "ContextWindowSize",      // 컨텍스트 윈도우 크기
        "SemanticCoherence",      // 의미적 응집성 가중치
        "ImportanceWeighting",    // 중요도 가중치
        "StructuralAwareness",    // 구조적 인식 여부
        "AdaptiveOverlap",        // 적응형 겹침 크기
        "QualityThreshold"        // 품질 임계값
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

        // 전체 문서의 LLM 최적화 컨텍스트를 먼저 분석
        var globalTechKeywords = DetectTechnicalKeywords(text);
        var globalDocumentDomain = DetectDocumentDomain(text, globalTechKeywords);

        // 전략 옵션 가져오기
        var contextWindowSize = GetStrategyOption(options, "ContextWindowSize", options.MaxChunkSize);
        var semanticCoherence = GetStrategyOption(options, "SemanticCoherence", 0.7);
        var importanceWeighting = GetStrategyOption(options, "ImportanceWeighting", 0.8);
        var structuralAwareness = GetStrategyOption(options, "StructuralAwareness", true);
        var adaptiveOverlap = GetStrategyOption(options, "AdaptiveOverlap", true);
        var qualityThreshold = GetStrategyOption(options, "QualityThreshold", 0.6);

        // 1단계: 문서 구조 분석
        var documentStructure = AnalyzeDocumentStructure(text);

        // 2단계: 의미적 단위 추출
        var semanticUnits = ExtractSemanticUnits(text, documentStructure);

        // 테이블 감지 시 청킹 크기 동적 조정
        var effectiveWindowSize = ContainsAnyTable(semanticUnits) ? contextWindowSize * 2 : contextWindowSize;

        // 3단계: 컨텍스트 인식 청킹
        var contextualChunks = CreateContextualChunks(
            semanticUnits,
            effectiveWindowSize,
            semanticCoherence,
            adaptiveOverlap ? options.OverlapSize : 0);

        // 4단계: 품질 평가 및 최적화 - 테이블 있을 때 동적 크기 적용
        var effectiveMaxSize = ContainsAnyTable(semanticUnits) ? options.MaxChunkSize * 2 : options.MaxChunkSize;
        var optimizedChunks = OptimizeChunks(contextualChunks, qualityThreshold, effectiveMaxSize);

        // 5단계: 최종 청크 생성
        var chunkIndex = 0;
        var globalPosition = 0;

        foreach (var chunkContent in optimizedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunk = CreateIntelligentChunk(
                chunkContent,
                content.Metadata,
                chunkIndex++,
                globalPosition,
                options,
                importanceWeighting,
                globalTechKeywords,
                globalDocumentDomain);

            chunks.Add(chunk);
            globalPosition += chunkContent.Length;
        }

        return await Task.FromResult(chunks);
    }

    public int EstimateChunkCount(DocumentContent content, ChunkingOptions options)
    {
        if (content == null || string.IsNullOrWhiteSpace(content.Text))
            return 0;

        var contextWindowSize = GetStrategyOption(options, "ContextWindowSize", options.MaxChunkSize);
        var avgChunkSize = (int)(contextWindowSize * 0.8); // 평균적으로 80% 사용

        return (int)Math.Ceiling((double)content.Text.Length / avgChunkSize);
    }

    private static DocumentStructure AnalyzeDocumentStructure(string text)
    {
        var structure = new DocumentStructure();

        // 헤더 찾기
        structure.Headers = HeaderRegex.Matches(text)
            .Cast<Match>()
            .Select(m => new StructuralElement
            {
                Content = m.Value.Trim(),
                Position = m.Index,
                Type = "Header",
                Importance = CalculateHeaderImportance(m.Value)
            })
            .ToList();

        // 리스트 항목 찾기
        structure.ListItems = ListItemRegex.Matches(text)
            .Cast<Match>()
            .Select(m => new StructuralElement
            {
                Content = ExtractListItem(text, m.Index),
                Position = m.Index,
                Type = "ListItem",
                Importance = 0.5
            })
            .ToList();

        // 문단 경계 찾기
        var paragraphs = ParagraphRegex.Split(text)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select((p, i) => new StructuralElement
            {
                Content = p.Trim(),
                Position = text.IndexOf(p.Trim()),
                Type = "Paragraph",
                Importance = CalculateParagraphImportance(p.Trim())
            })
            .ToList();

        structure.Paragraphs = paragraphs;

        return structure;
    }

    private static List<SemanticUnit> ExtractSemanticUnits(string text, DocumentStructure structure)
    {
        var units = new List<SemanticUnit>();
        var lines = text.Split('\n');
        var position = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                position += lines[i].Length + 1; // +1 for newline
                continue;
            }

            // 테이블 행 감지 - 테이블 행들을 하나의 단위로 묶기
            if (IsTableLine(line))
            {
                var tableUnit = ExtractTableUnit(lines, i, position, out var endIndex, out var nextPosition);
                if (tableUnit != null)
                {
                    units.Add(tableUnit);
                    i = endIndex;
                    position = nextPosition;
                    continue;
                }
            }

            // 섹션 헤더 감지
            var isHeader = MarkdownSectionRegex.IsMatch(line);

            // 일반 라인 처리
            var unit = new SemanticUnit
            {
                Content = line,
                Position = position,
                SemanticWeight = CalculateSemanticWeight(line),
                ContextualRelevance = CalculateContextualRelevance(line, structure),
                Importance = isHeader ? 1.0 : CalculateImportance(line)
            };

            units.Add(unit);
            position += lines[i].Length + 1; // +1 for newline
        }

        return units;
    }

    /// <summary>
    /// 테이블 라인인지 확인
    /// </summary>
    private static bool IsTableLine(string line)
    {
        // Markdown 테이블 라인: |로 시작하거나 |를 2개 이상 포함하는 라인
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        // |를 2개 이상 포함하는 라인이면 테이블 라인으로 판단
        return trimmed.Contains("|") && trimmed.Count(c => c == '|') >= 2;
    }

    /// <summary>
    /// 연속된 테이블 라인들을 하나의 SemanticUnit으로 추출
    /// </summary>
    private static SemanticUnit? ExtractTableUnit(string[] lines, int startIndex, int startPosition, out int endIndex, out int nextPosition)
    {
        var tableLines = new List<string>();
        endIndex = startIndex;
        nextPosition = startPosition;

        // 연속된 테이블 라인 수집
        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (IsTableLine(line))
            {
                tableLines.Add(line);
                endIndex = i;
                nextPosition += lines[i].Length + 1;
            }
            else if (string.IsNullOrWhiteSpace(line) && tableLines.Any())
            {
                // 테이블 내의 빈 줄은 허용하지만 테이블에 포함시키지 않음
                nextPosition += lines[i].Length + 1;
            }
            else if (tableLines.Any()) // 테이블 라인이 있었는데 테이블이 아닌 라인을 만나면 종료
            {
                break;
            }
            else // 첫 번째 라인이 테이블이 아니면 null 반환
            {
                return null;
            }
        }

        if (tableLines.Count >= 2) // 헤더 + 최소 1개 행 이상일 때만 테이블로 처리
        {
            return new SemanticUnit
            {
                Content = string.Join("\n", tableLines),
                Position = startPosition,
                SemanticWeight = 1.0, // 테이블은 높은 가중치
                ContextualRelevance = 1.0,
                Importance = 0.9
            };
        }

        return null;
    }

    private static List<string> CreateContextualChunks(
        List<SemanticUnit> units,
        int maxSize,
        double coherenceThreshold,
        int baseOverlap)
    {
        var chunks = new List<string>();
        var currentChunk = new List<SemanticUnit>();
        var currentSize = 0;

        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            var unitSize = EstimateTokenCount(unit.Content);

            // 테이블 SemanticUnit 감지 - ExtractSemanticUnits에서 생성된 테이블 단위를 보존
            var isTableUnit = unit.SemanticWeight >= 1.0 && unit.ContextualRelevance >= 1.0 &&
                              unit.Content.Contains("|") && unit.Content.Count(c => c == '|') >= 4; // 완전한 테이블 판단


            if (isTableUnit)
            {
                // 현재 청크가 있고 테이블을 추가하면 크기 초과인 경우 현재 청크 완료
                if (currentChunk.Any() && currentSize + unitSize > maxSize)
                {
                    var chunkContent = CreateCoherentChunk(currentChunk, baseOverlap);
                    chunks.Add(chunkContent);
                    currentChunk.Clear();
                    currentSize = 0;
                }

                // 테이블 SemanticUnit은 최대 2.5배까지 허용하여 완전성 보장
                if (unitSize <= maxSize * 2.5)
                {
                    currentChunk.Add(unit);
                    currentSize += unitSize;
                }
                else
                {
                    // 매우 큰 테이블만 의미적 경계에서 분할 (기존 로직 유지)
                    var singleTableUnits = new List<SemanticUnit> { unit };
                    var tableParts = SplitLargeTable(singleTableUnits, maxSize);
                    foreach (var part in tableParts)
                    {
                        if (currentChunk.Any())
                        {
                            chunks.Add(CreateCoherentChunk(currentChunk, baseOverlap));
                            currentChunk.Clear();
                            currentSize = 0;
                        }
                        chunks.Add(part);
                    }
                }
                continue;
            }

            // 섹션 헤더 감지 - 섹션 경계에서 청킹
            var isSectionHeader = MarkdownSectionRegex.IsMatch(unit.Content);
            if (isSectionHeader && currentChunk.Any() && currentSize > maxSize * 0.3) // 30% 이상일 때만 분할
            {
                var chunkContent = CreateCoherentChunk(currentChunk, baseOverlap);
                chunks.Add(chunkContent);
                currentChunk.Clear();
                currentSize = 0;
            }

            // 테이블 행 보호 로직 - 테이블 행이 포함된 청크는 무조건 완성
            var containsTableRow = ContainsTableRow(currentChunk) || IsTableRow(unit.Content);

            // 일반적인 크기 기반 청킹 (기존 로직)
            if (currentChunk.Any() &&
                currentSize + unitSize > maxSize &&
                !isSectionHeader && // 섹션 헤더는 강제로 포함
                !containsTableRow && // 테이블 행은 보호
                ShouldStartNewChunk(currentChunk, unit, coherenceThreshold))
            {
                var chunkContent = CreateCoherentChunk(currentChunk, baseOverlap);
                chunks.Add(chunkContent);

                var overlapUnits = GetOverlapUnits(currentChunk, baseOverlap, unit.ContextualRelevance);
                currentChunk = overlapUnits;
                currentSize = overlapUnits.Sum(u => EstimateTokenCount(u.Content));
            }

            currentChunk.Add(unit);
            currentSize += unitSize;
        }

        // 마지막 청크 처리
        if (currentChunk.Any())
        {
            var chunkContent = CreateCoherentChunk(currentChunk, baseOverlap);
            chunks.Add(chunkContent);
        }

        return chunks;
    }

    private static List<string> OptimizeChunks(List<string> chunks, double qualityThreshold, int maxSize)
    {
        var optimized = new List<string>();

        foreach (var chunk in chunks)
        {
            var quality = CalculateChunkQuality(chunk);

            // HARD LIMIT: 크기 초과 청크는 품질에 관계없이 강제 분할
            if (chunk.Length > maxSize)
            {
                var forceSplit = EnforceMaxSize(chunk, maxSize);
                optimized.AddRange(forceSplit);
            }
            else if (quality >= qualityThreshold)
            {
                optimized.Add(chunk);
            }
            else
            {
                // 품질이 낮은 청크는 다시 분할하거나 인접 청크와 병합
                var reprocessed = ReprocessLowQualityChunk(chunk, maxSize);
                optimized.AddRange(reprocessed);
            }
        }

        return optimized;
    }

    private static bool ShouldStartNewChunk(List<SemanticUnit> currentChunk, SemanticUnit newUnit, double coherenceThreshold)
    {
        if (!currentChunk.Any())
            return false;

        var lastUnit = currentChunk.Last();
        var semanticDistance = CalculateSemanticDistance(lastUnit, newUnit);

        return semanticDistance > (1.0 - coherenceThreshold);
    }

    private static double CalculateSemanticDistance(SemanticUnit unit1, SemanticUnit unit2)
    {
        // 간단한 의미적 거리 계산 (실제로는 더 정교한 NLP 기법 필요)
        var commonWords = GetCommonWords(unit1.Content, unit2.Content);
        var totalWords = GetUniqueWords(unit1.Content).Union(GetUniqueWords(unit2.Content)).Count();

        return totalWords == 0 ? 0.0 : 1.0 - (double)commonWords / totalWords;
    }

    private static DocumentChunk CreateIntelligentChunk(
        string content,
        DocumentMetadata metadata,
        int chunkIndex,
        int startPosition,
        ChunkingOptions options,
        double importanceWeighting,
        List<string> globalTechKeywords,
        string globalDocumentDomain)
    {
        var importance = CalculateImportance(content) * importanceWeighting;
        var contextQuality = CalculateChunkQuality(content);

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid().ToString(),
            Content = content.Trim(),
            Metadata = metadata,
            StartPosition = startPosition,
            EndPosition = startPosition + content.Length,
            ChunkIndex = chunkIndex,
            Strategy = ChunkingStrategies.Intelligent,
            EstimatedTokens = EstimateTokenCount(content),
            CreatedAt = DateTime.UtcNow,
            Importance = importance,
            PageNumber = metadata.PageCount == 1 ? 1 : null,
            Properties = new Dictionary<string, object>
            {
                ["ContextQuality"] = contextQuality,
                ["SemanticCoherence"] = CalculateSemanticCoherence(content),
                ["StructuralElements"] = CountStructuralElements(content),
                ["ImportanceScore"] = importance
            }
        };

        // LLM 최적화 메타데이터 자동 생성 (전역 컨텍스트 사용)
        EnhanceChunkForLlm(chunk, globalTechKeywords, globalDocumentDomain);

        return chunk;
    }

    /// <summary>
    /// 청크를 LLM에 최적화된 형태로 자동 강화 (전역 컨텍스트 사용)
    /// </summary>
    private static void EnhanceChunkForLlm(DocumentChunk chunk, List<string> globalTechKeywords, string globalDocumentDomain)
    {
        var content = chunk.Content;

        // 1. 구조적 컨텍스트 헤더 생성
        var contextParts = new List<string>();

        // 문서 타입 추가
        if (!string.IsNullOrEmpty(chunk.Metadata.FileType))
            contextParts.Add($"Type: {chunk.Metadata.FileType}");

        // 구조적 역할 추가 (테이블, 코드, 리스트 등)
        var structuralRole = DetectStructuralRole(content);
        if (structuralRole != "content")
            contextParts.Add($"Structure: {structuralRole}");

        // 전역 기술 키워드 사용 (전체 문서 기반)
        if (globalTechKeywords.Any())
        {
            contextParts.Add($"Tech: {string.Join(", ", globalTechKeywords.Take(3))}");
            chunk.TechnicalKeywords = globalTechKeywords;
        }

        // 전역 문서 도메인 사용 (전체 문서 기반)
        chunk.DocumentDomain = globalDocumentDomain;
        if (chunk.DocumentDomain != "General")
            contextParts.Add($"Domain: {chunk.DocumentDomain}");

        // ContextualHeader 생성
        if (contextParts.Any())
        {
            chunk.ContextualHeader = $"[{string.Join(" | ", contextParts)}]";
        }

        // 구조적 역할 설정
        chunk.StructuralRole = structuralRole;
    }

    /// <summary>
    /// 구조적 역할 자동 탐지
    /// </summary>
    private static string DetectStructuralRole(string content)
    {
        var trimmed = content.Trim();

        // 헤더 (마크다운 헤더)
        if (trimmed.StartsWith('#'))
            return "header";

        // 테이블
        if (trimmed.Contains('|') && trimmed.Count(c => c == '|') >= 2)
            return "table";

        // 코드 블록
        if (trimmed.StartsWith("```") || trimmed.Contains("```"))
            return "code_block";

        // 리스트
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") ||
            Regex.IsMatch(trimmed, @"^\d+\.\s"))
            return "list";

        return "content";
    }

    /// <summary>
    /// 기술 키워드 자동 탐지
    /// </summary>
    private static List<string> DetectTechnicalKeywords(string content)
    {
        var keywords = new List<string>();
        var text = content.ToLowerInvariant();

        var techPatterns = new Dictionary<string, string[]>
        {
            ["API"] = new[] { "api", "endpoint", "rest", "graphql" },
            ["Database"] = new[] { "database", "sql", "nosql", "query" },
            ["Frontend"] = new[] { "ui", "frontend", "react", "vue", "angular" },
            ["Backend"] = new[] { "server", "backend", "service", "microservice" },
            ["DevOps"] = new[] { "docker", "kubernetes", "deployment", "pipeline" },
            ["AI/ML"] = new[] { "ai", "ml", "model", "embedding", "vector" }
        };

        foreach (var (category, patterns) in techPatterns)
        {
            if (patterns.Any(pattern => ContainsWholeWord(text, pattern)))
                keywords.Add(category);
        }

        return keywords.Take(5).ToList(); // 최대 5개로 제한
    }

    /// <summary>
    /// 단어 경계를 고려한 키워드 검출
    /// </summary>
    private static bool ContainsWholeWord(string text, string word)
    {
        var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return regex.IsMatch(text);
    }

    /// <summary>
    /// 문서 도메인 자동 탐지
    /// </summary>
    private static string DetectDocumentDomain(string content, List<string> techKeywords)
    {
        var text = content.ToLowerInvariant();

        // Academic: 학술 관련 키워드 우선 체크 (특정성이 높음)
        if (ContainsWholeWord(text, "research") || ContainsWholeWord(text, "study") ||
            ContainsWholeWord(text, "abstract") || ContainsWholeWord(text, "methodology") ||
            ContainsWholeWord(text, "논문") || ContainsWholeWord(text, "literature") ||
            ContainsWholeWord(text, "theoretical") ||
            (ContainsWholeWord(text, "analysis") && (ContainsWholeWord(text, "research") || ContainsWholeWord(text, "data"))))
            return "Academic";

        // Business: 비즈니스 관련 키워드 (더 구체적인 키워드만 사용)
        if (ContainsWholeWord(text, "business") || ContainsWholeWord(text, "stakeholder") ||
            ContainsWholeWord(text, "strategy") || ContainsWholeWord(text, "strategic") ||
            ContainsWholeWord(text, "planning") || ContainsWholeWord(text, "timeline") ||
            ContainsWholeWord(text, "milestone") || ContainsWholeWord(text, "objective") ||
            (ContainsWholeWord(text, "requirement") && (ContainsWholeWord(text, "business") || ContainsWholeWord(text, "project"))) ||
            (ContainsWholeWord(text, "analysis") && ContainsWholeWord(text, "requirement")))
            return "Business";

        // Technical: 기술 키워드가 1개 이상이거나 기술적 내용
        if (techKeywords.Count >= 1 ||
            ContainsWholeWord(text, "api") || ContainsWholeWord(text, "endpoint") ||
            ContainsWholeWord(text, "database") || ContainsWholeWord(text, "schema") ||
            ContainsWholeWord(text, "react") || ContainsWholeWord(text, "component") ||
            ContainsWholeWord(text, "function") || ContainsWholeWord(text, "class") || ContainsWholeWord(text, "method"))
            return "Technical";

        return "General";
    }

    // 헬퍼 메서드들
    private static List<string> ExtractSentences(string text)
    {
        return SentenceEndRegex.Split(text)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Where(s => s.Length > 10) // 너무 짧은 문장 제외
            .ToList();
    }

    private static double CalculateHeaderImportance(string header)
    {
        var level = header.TrimStart().TakeWhile(c => c == '#').Count();
        return level switch
        {
            1 => 1.0,
            2 => 0.9,
            3 => 0.8,
            4 => 0.7,
            5 => 0.6,
            6 => 0.5,
            _ => 0.8
        };
    }

    private static double CalculateParagraphImportance(string paragraph)
    {
        var hasKeywords = ImportantKeywordRegex.IsMatch(paragraph);
        var length = paragraph.Length;

        var importance = 0.5;
        if (hasKeywords) importance += 0.3;
        if (length > 200) importance += 0.1;
        if (length > 500) importance += 0.1;

        return Math.Min(importance, 1.0);
    }

    private static double CalculateSemanticWeight(string text)
    {
        // 키워드 밀도, 문장 복잡도 등을 고려한 의미적 가중치
        var keywordCount = ImportantKeywordRegex.Matches(text).Count;
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        return wordCount == 0 ? 0.0 : Math.Min(1.0, 0.5 + (keywordCount * 0.1));
    }

    private static double CalculateContextualRelevance(string sentence, DocumentStructure structure)
    {
        // 문서 구조와의 연관성 계산
        var relevance = 0.5;

        if (structure.Headers.Any(h => sentence.Contains(h.Content.Replace("#", "").Trim(), StringComparison.OrdinalIgnoreCase)))
            relevance += 0.3;

        return Math.Min(relevance, 1.0);
    }

    private static double CalculateImportance(string content)
    {
        var importance = 0.5;

        if (ImportantKeywordRegex.IsMatch(content))
            importance += 0.2;

        if (content.Length > 100)
            importance += 0.1;

        if (HeaderRegex.IsMatch(content))
            importance += 0.2;

        return Math.Min(importance, 1.0);
    }

    private static string ExtractListItem(string text, int position)
    {
        var lines = text.Substring(position).Split('\n');
        return lines.FirstOrDefault()?.Trim() ?? string.Empty;
    }

    private static string CreateCoherentChunk(List<SemanticUnit> units, int baseOverlap)
    {
        return string.Join(" ", units.Select(u => u.Content));
    }

    /// <summary>
    /// 주어진 청크에 테이블 행이 포함되어 있는지 확인
    /// </summary>
    private static bool ContainsTableRow(List<SemanticUnit> chunk)
    {
        return chunk.Any(unit => IsTableRow(unit.Content));
    }

    /// <summary>
    /// 주어진 콘텐츠가 테이블 행인지 확인
    /// </summary>
    private static bool IsTableRow(string content)
    {
        return content.Contains("|") && content.Count(c => c == '|') >= 2;
    }

    /// <summary>
    /// SemanticUnits 중에 테이블이 포함되어 있는지 확인
    /// </summary>
    private static bool ContainsAnyTable(List<SemanticUnit> units)
    {
        return units.Any(unit => IsTableRow(unit.Content));
    }

    private static List<SemanticUnit> GetOverlapUnits(List<SemanticUnit> currentChunk, int baseOverlap, double relevance)
    {
        var overlapSize = Math.Max(1, (int)(baseOverlap * relevance));
        return currentChunk.TakeLast(overlapSize).ToList();
    }

    private static double CalculateChunkQuality(string chunk)
    {
        var sentences = ExtractSentences(chunk);
        if (sentences.Count == 0) return 0.0;

        var avgSentenceLength = sentences.Average(s => s.Length);
        var hasKeywords = ImportantKeywordRegex.IsMatch(chunk);
        var coherence = CalculateSemanticCoherence(chunk);

        var quality = 0.3 * (avgSentenceLength > 50 ? 1.0 : avgSentenceLength / 50.0) +
                     0.2 * (hasKeywords ? 1.0 : 0.0) +
                     0.5 * coherence;

        return Math.Min(quality, 1.0);
    }

    private static double CalculateSemanticCoherence(string content)
    {
        // 간단한 응집성 측정 - 반복되는 키워드의 비율
        var words = content.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToList();

        if (words.Count == 0) return 0.0;

        var wordFreq = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var repeatedWords = wordFreq.Where(kvp => kvp.Value > 1).Count();

        return (double)repeatedWords / words.Distinct().Count();
    }

    private static int CountStructuralElements(string content)
    {
        return HeaderRegex.Matches(content).Count +
               ListItemRegex.Matches(content).Count +
               ParagraphRegex.Matches(content).Count;
    }

    private static List<string> ReprocessLowQualityChunk(string chunk, int maxSize)
    {
        // 저품질 청크 재처리 로직
        if (chunk.Length <= maxSize / 2)
        {
            return new List<string> { chunk }; // 너무 짧으면 그대로 유지
        }

        // 문장 단위로 재분할
        var sentences = ExtractSentences(chunk);
        var result = new List<string>();
        var currentPart = new List<string>();
        var currentLength = 0;

        foreach (var sentence in sentences)
        {
            if (currentLength + sentence.Length > maxSize && currentPart.Any())
            {
                result.Add(string.Join(" ", currentPart));
                currentPart.Clear();
                currentLength = 0;
            }

            currentPart.Add(sentence);
            currentLength += sentence.Length;
        }

        if (currentPart.Any())
        {
            result.Add(string.Join(" ", currentPart));
        }

        return result;
    }

    private static int GetCommonWords(string text1, string text2)
    {
        var words1 = GetUniqueWords(text1);
        var words2 = GetUniqueWords(text2);
        return words1.Intersect(words2).Count();
    }

    private static HashSet<string> GetUniqueWords(string text)
    {
        return text.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToHashSet();
    }

    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return (int)(words * 1.3);
    }

    /// <summary>
    /// 크기 초과 청크를 강제로 MaxSize 이하로 분할
    /// 의미적 경계(문장, 단어)를 고려하여 최대한 자연스럽게 분할
    /// </summary>
    private static List<string> EnforceMaxSize(string chunk, int maxSize)
    {
        var result = new List<string>();

        if (chunk.Length <= maxSize)
        {
            result.Add(chunk);
            return result;
        }

        // 1단계: 문장 단위로 분할 시도
        var sentences = ExtractSentences(chunk);
        var currentPart = new List<string>();
        var currentLength = 0;

        foreach (var sentence in sentences)
        {
            // 단일 문장이 maxSize를 초과하는 경우 단어 단위로 분할
            if (sentence.Length > maxSize)
            {
                // 현재 누적된 부분이 있으면 먼저 추가
                if (currentPart.Any())
                {
                    result.Add(string.Join(" ", currentPart));
                    currentPart.Clear();
                    currentLength = 0;
                }

                // 긴 문장을 단어 단위로 분할
                var wordChunks = SplitByWords(sentence, maxSize);
                result.AddRange(wordChunks);
            }
            else if (currentLength + sentence.Length + 1 > maxSize && currentPart.Any()) // +1 for space
            {
                // 현재 누적 분량이 초과될 경우
                result.Add(string.Join(" ", currentPart));
                currentPart.Clear();
                currentPart.Add(sentence);
                currentLength = sentence.Length;
            }
            else
            {
                // 정상적으로 추가
                currentPart.Add(sentence);
                currentLength += sentence.Length + (currentPart.Count > 1 ? 1 : 0); // space
            }
        }

        // 마지막 부분 추가
        if (currentPart.Any())
        {
            result.Add(string.Join(" ", currentPart));
        }

        return result;
    }

    /// <summary>
    /// 긴 문장을 단어 단위로 maxSize 이하의 청크로 분할
    /// </summary>
    private static List<string> SplitByWords(string text, int maxSize)
    {
        var result = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var word in words)
        {
            if (currentLength + word.Length + 1 > maxSize && currentChunk.Any()) // +1 for space
            {
                result.Add(string.Join(" ", currentChunk));
                currentChunk.Clear();
                currentChunk.Add(word);
                currentLength = word.Length;
            }
            else
            {
                currentChunk.Add(word);
                currentLength += word.Length + (currentChunk.Count > 1 ? 1 : 0); // space
            }
        }

        if (currentChunk.Any())
        {
            result.Add(string.Join(" ", currentChunk));
        }

        return result;
    }

    private static T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        if (options.StrategyOptions.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// 테이블 시작 여부 확인
    /// </summary>
    private static bool IsTableStart(string content)
    {
        return TableRegex.IsMatch(content) || content.Contains("|") && content.Count(c => c == '|') >= 3;
    }

    /// <summary>
    /// 완전한 테이블을 추출 (헤더 + 구분자 + 데이터 행들)
    /// </summary>
    private static List<SemanticUnit> ExtractCompleteTable(List<SemanticUnit> units, int startIndex, out int endIndex)
    {
        var tableUnits = new List<SemanticUnit>();
        endIndex = startIndex;

        for (int i = startIndex; i < units.Count; i++)
        {
            var content = units[i].Content;

            // 테이블 행인지 확인 (|로 구분되는 구조)
            if (content.Contains("|") && (content.Count(c => c == '|') >= 2 || TableSeparatorRegex.IsMatch(content)))
            {
                tableUnits.Add(units[i]);
                endIndex = i;
            }
            else if (tableUnits.Any()) // 테이블이 시작된 후 테이블이 아닌 행을 만나면 종료
            {
                break;
            }
        }

        return tableUnits;
    }

    /// <summary>
    /// 대용량 테이블을 의미적 경계에서 분할
    /// </summary>
    private static List<string> SplitLargeTable(List<SemanticUnit> tableUnits, int maxSize)
    {
        var result = new List<string>();

        if (!tableUnits.Any())
            return result;

        // 헤더와 구분자 식별
        var headerUnit = tableUnits.FirstOrDefault();
        var separatorUnit = tableUnits.Skip(1).FirstOrDefault();
        var dataRows = tableUnits.Skip(2).ToList(); // 실제 데이터 행들만

        // 헤더 + 구분자 크기
        var headerSize = EstimateTokenCount(headerUnit?.Content ?? "") + EstimateTokenCount(separatorUnit?.Content ?? "");

        var currentRows = new List<SemanticUnit>();
        var currentSize = headerSize; // 헤더 크기부터 시작

        foreach (var row in dataRows)
        {
            var rowSize = EstimateTokenCount(row.Content);

            // 헤더 포함 시 크기 초과하면 새로운 테이블 파트 생성 (최소 1행은 포함)
            if (currentRows.Any() && currentSize + rowSize > maxSize)
            {
                // 현재 파트 완성: 헤더 + 구분자 + 데이터 행들
                var partUnits = new List<SemanticUnit>();
                if (headerUnit != null) partUnits.Add(headerUnit);
                if (separatorUnit != null) partUnits.Add(separatorUnit);
                partUnits.AddRange(currentRows);

                result.Add(CreateCoherentChunk(partUnits, 0));

                // 다음 파트 시작
                currentRows.Clear();
                currentSize = headerSize;
            }

            currentRows.Add(row);
            currentSize += rowSize;
        }

        // 마지막 파트 처리
        if (currentRows.Any())
        {
            var partUnits = new List<SemanticUnit>();
            if (headerUnit != null) partUnits.Add(headerUnit);
            if (separatorUnit != null) partUnits.Add(separatorUnit);
            partUnits.AddRange(currentRows);

            result.Add(CreateCoherentChunk(partUnits, 0));
        }

        return result;
    }

    // 내부 클래스들
    private class DocumentStructure
    {
        public List<StructuralElement> Headers { get; set; } = new();
        public List<StructuralElement> ListItems { get; set; } = new();
        public List<StructuralElement> Paragraphs { get; set; } = new();
    }

    private class StructuralElement
    {
        public string Content { get; set; } = string.Empty;
        public int Position { get; set; }
        public string Type { get; set; } = string.Empty;
        public double Importance { get; set; }
    }

    private class SemanticUnit
    {
        public string Content { get; set; } = string.Empty;
        public int Position { get; set; }
        public double SemanticWeight { get; set; }
        public double ContextualRelevance { get; set; }
        public double Importance { get; set; }
    }
}