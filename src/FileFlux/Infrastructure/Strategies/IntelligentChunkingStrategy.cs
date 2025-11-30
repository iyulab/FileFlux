using FileFlux;
using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Languages;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// 지능형 청킹 전략 - RAG 시스템에 최적화된 컨텍스트 인식 분할
/// </summary>
public partial class IntelligentChunkingStrategy : IChunkingStrategy
{
    private static readonly Regex SentenceEndRegex = MyRegex();
    private static readonly Regex ParagraphRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^#{1,6}\s+.+$|^.+\n[=\-]+\s*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ListItemRegex = new(@"^\s*[-*+]\s+|^\s*\d+\.\s+", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex ImportantKeywordRegex = new(@"\b(중요|핵심|요약|결론|참고|주의|경고|important|key|summary|conclusion|note|warning|attention)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // 테이블 감지를 위한 정규식 추가
    private static readonly Regex TableRegex = new(@"^\s*\|[^|]+\|[^|]+\|", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex TableSeparatorRegex = new(@"^\s*\|[\s\-\|]+\|", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex MarkdownSectionRegex = new(@"^#{1,6}\s+.*$", RegexOptions.Compiled | RegexOptions.Multiline);

    // Language profile provider for multilingual text segmentation
    private static readonly ILanguageProfileProvider _languageProfileProvider = new DefaultLanguageProfileProvider();

    // Phase 10: Context Preservation 강화를 위한 적응형 오버랩 매니저
    private static readonly AdaptiveOverlapManager _overlapManager = new();

    // Phase 10: Boundary Quality 일관성 개선을 위한 경계 품질 매니저
    private static readonly BoundaryQualityManager _boundaryQualityManager = new();

    // Phase 11: Vector Search Optimization components
    private static readonly VectorSearchOptimizer _vectorSearchOptimizer = new();
    private static readonly SearchMetadataEnricher _metadataEnricher = new();
    private static readonly HybridSearchPreprocessor _hybridPreprocessor = new();
    private static readonly SearchQualityEvaluator _qualityEvaluator = new();

    // Phase 12: Graph Search Optimization components
    private static readonly EntityExtractionSystem _entityExtractor = new();
    private static readonly GraphStructureGenerator _graphGenerator = new();
    private static readonly OntologyMapper _ontologyMapper = new();
    private static readonly GraphQualityAssurance _graphQualityAssurance = new();

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
    private static readonly char[] separator = new[] { ' ', '\n', '\r', '\t' };

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
        string? documentHeader = null;

        // 문서 헤더 분리 (옵션 활성화 시)
        if (options.SeparateDocumentHeader)
        {
            (documentHeader, text) = SeparateDocumentHeader(text, options);
        }

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

        // 1단계: 언어 프로파일 결정 (자동 감지 또는 명시적 지정)
        var languageProfile = GetLanguageProfile(text, options);

        // 2단계: 문서 구조 분석
        var documentStructure = AnalyzeDocumentStructure(text);

        // 3단계: 의미적 단위 추출
        var semanticUnits = ExtractSemanticUnits(text, documentStructure, languageProfile);

        // 테이블 감지 시 청킹 크기 동적 조정
        var effectiveWindowSize = ContainsAnyTable(semanticUnits) ? contextWindowSize * 2 : contextWindowSize;

        // 3단계: 컨텍스트 인식 청킹 (Phase 10: 적응형 오버랩 적용)
        var contextualChunks = CreateContextualChunks(
            semanticUnits,
            effectiveWindowSize,
            semanticCoherence,
            options, // 전체 옵션 전달하여 적응형 오버랩 계산
            adaptiveOverlap);

        // 4단계: 품질 평가 및 최적화 - 테이블 있을 때 동적 크기 적용
        var effectiveMaxSize = ContainsAnyTable(semanticUnits) ? options.MaxChunkSize * 2 : options.MaxChunkSize;
        var optimizedChunks = OptimizeChunks(contextualChunks, qualityThreshold, effectiveMaxSize, options, languageProfile);

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
                globalDocumentDomain,
                documentHeader);

            chunks.Add(chunk);
            globalPosition += chunkContent.Length;
        }

        // Finalize chunks with count and relationships
        ChunkingHelper.FinalizeChunks(chunks);

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
                Position = text.IndexOf(p.Trim(), StringComparison.Ordinal),
                Type = "Paragraph",
                Importance = CalculateParagraphImportance(p.Trim())
            })
            .ToList();

        structure.Paragraphs = paragraphs;

        return structure;
    }

    private static List<SemanticUnit> ExtractSemanticUnits(string text, DocumentStructure structure, ILanguageProfile languageProfile)
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

            // 섹션 헤더 감지 (마크다운 + 언어별 섹션 마커)
            var isHeader = MarkdownSectionRegex.IsMatch(line) || languageProfile.IsSectionMarker(line);

            // 일반 라인 처리
            var unit = new SemanticUnit
            {
                Content = line,
                Position = position,
                SemanticWeight = CalculateSemanticWeight(line),
                ContextualRelevance = CalculateContextualRelevance(line, structure),
                Importance = isHeader ? 1.0 : CalculateImportance(line),
                IsSectionHeader = isHeader
            };

            units.Add(unit);
            position += lines[i].Length + 1; // +1 for newline
        }

        return units;
    }

    /// <summary>
    /// 테이블 라인 또는 테이블 마커인지 확인
    /// </summary>
    private static bool IsTableLine(string line)
    {
        // Markdown 테이블 라인: |로 시작하거나 |를 2개 이상 포함하는 라인
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        // TABLE_START 마커인지 확인
        if (trimmed.Contains("<!-- TABLE_START -->")) return true;

        // |를 2개 이상 포함하는 라인이면 테이블 라인으로 판단
        return trimmed.Contains('|') && trimmed.Count(c => c == '|') >= 2;
    }

    /// <summary>
    /// 테이블 마커를 포함한 완전한 테이블 블록을 하나의 SemanticUnit으로 추출
    /// TABLE_START부터 TABLE_END까지의 모든 내용을 포함
    /// </summary>
    private static SemanticUnit? ExtractTableUnit(string[] lines, int startIndex, int startPosition, out int endIndex, out int nextPosition)
    {
        var tableLines = new List<string>();
        endIndex = startIndex;
        nextPosition = startPosition;
        // 현재 라인이 TABLE_START인지 확인
        if (lines[startIndex].Contains("<!-- TABLE_START -->"))
        {
            tableLines.Add(lines[startIndex]);
            nextPosition += lines[startIndex].Length + 1;

            // TABLE_END까지 모든 라인 수집
            for (int i = startIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i];
                tableLines.Add(line);
                endIndex = i;
                nextPosition += lines[i].Length + 1;

                if (line.Contains("<!-- TABLE_END -->"))
                {
                    break; // 테이블 블록 완료
                }
            }

            // 완전한 테이블 블록이 수집되었으면 SemanticUnit 생성
            if (tableLines.Count != 0 && tableLines.Last().Contains("<!-- TABLE_END -->"))
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
        }
        else
        {
            // 기존 로직: 연속된 테이블 라인 수집 (마커가 없는 경우)
            for (int i = startIndex; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();

                if (IsTableLine(line))
                {
                    tableLines.Add(line);
                    endIndex = i;
                    nextPosition += lines[i].Length + 1;
                }
                else if (string.IsNullOrWhiteSpace(line) && tableLines.Count != 0)
                {
                    // 테이블 내의 빈 줄은 허용하지만 테이블에 포함시키지 않음
                    nextPosition += lines[i].Length + 1;
                }
                else if (tableLines.Count != 0) // 테이블 라인이 있었는데 테이블이 아닌 라인을 만나면 종료
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
        }

        return null;
    }

    private static List<string> CreateContextualChunks(
        List<SemanticUnit> units,
        int maxSize,
        double coherenceThreshold,
        ChunkingOptions options,
        bool adaptiveOverlap)
    {
        var chunks = new List<string>();
        var currentChunk = new List<SemanticUnit>();
        var currentSize = 0;
        var previousChunkText = string.Empty; // Phase 10: 이전 청크 전체 텍스트 저장 (적응형 오버랩용)

        for (int i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            var unitSize = EstimateTokenCount(unit.Content);

            // 테이블 SemanticUnit 감지 - ExtractSemanticUnits에서 생성된 테이블 단위를 보존
            var isTableUnit = unit.SemanticWeight >= 1.0 && unit.ContextualRelevance >= 1.0 &&
                              unit.Content.Contains('|') && unit.Content.Count(c => c == '|') >= 4; // 완전한 테이블 판단


            if (isTableUnit)
            {
                // 현재 청크가 있고 테이블을 추가하면 크기 초과인 경우 현재 청크 완료
                if (currentChunk.Count != 0 && currentSize + unitSize > maxSize)
                {
                    var currentChunkText = string.Join(" ", currentChunk.Select(u => u.Content));

                    // Phase 10: 적응형 오버랩 적용
                    string overlapText = "";
                    if (adaptiveOverlap && !string.IsNullOrEmpty(previousChunkText))
                    {
                        var optimalOverlap = _overlapManager.CalculateOptimalOverlap(previousChunkText, currentChunkText, options);
                        overlapText = _overlapManager.CreateContextPreservingOverlap(previousChunkText, optimalOverlap);
                    }
                    else if (options.OverlapSize > 0)
                    {
                        var overlapUnits = GetOverlapUnits(currentChunk, options.OverlapSize, 1.0);
                        overlapText = string.Join(" ", overlapUnits.Select(u => u.Content));
                    }

                    var chunkContent = CreateCoherentChunk(currentChunk, options.OverlapSize, overlapText);
                    chunks.Add(chunkContent);

                    // 다음 청크를 위해 현재 청크 텍스트 저장
                    previousChunkText = currentChunkText;

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
                        if (currentChunk.Count != 0)
                        {
                            var currentChunkText = string.Join(" ", currentChunk.Select(u => u.Content));
                            chunks.Add(CreateCoherentChunk(currentChunk, options.OverlapSize, ""));
                            previousChunkText = currentChunkText;

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
            if (isSectionHeader && currentChunk.Count != 0 && currentSize > maxSize * 0.3) // 30% 이상일 때만 분할
            {
                var currentChunkText = string.Join(" ", currentChunk.Select(u => u.Content));

                // Phase 10: 적응형 오버랩 적용
                string overlapText = "";
                if (adaptiveOverlap && !string.IsNullOrEmpty(previousChunkText))
                {
                    var optimalOverlap = _overlapManager.CalculateOptimalOverlap(previousChunkText, currentChunkText, options);
                    overlapText = _overlapManager.CreateContextPreservingOverlap(previousChunkText, optimalOverlap);
                }

                var chunkContent = CreateCoherentChunk(currentChunk, options.OverlapSize, overlapText);
                chunks.Add(chunkContent);

                previousChunkText = currentChunkText;

                currentChunk.Clear();
                currentSize = 0;
            }

            // 테이블 행 보호 로직 - 테이블 행이 포함된 청크는 무조건 완성
            var containsTableRow = ContainsTableRow(currentChunk) || IsTableRow(unit.Content);

            // Phase 10: 경계 품질을 고려한 청킹 결정
            if (currentChunk.Count != 0 &&
                currentSize + unitSize > maxSize &&
                !isSectionHeader && // 섹션 헤더는 강제로 포함
                !containsTableRow && // 테이블 행은 보호
                ShouldStartNewChunk(currentChunk, unit, coherenceThreshold))
            {
                var currentChunkText = string.Join("\n", currentChunk.Select(u => u.Content));
                var entireText = string.Join("\n", units.Select(u => u.Content)); // 전체 텍스트 재구성
                var proposedSplitPosition = unit.Position; // 현재 분할 위치

                // 경계 품질 평가 및 개선
                var boundaryResult = _boundaryQualityManager.EvaluateAndImproveBoundary(entireText, proposedSplitPosition, options);

                // 품질이 개선되었다면 조정된 위치 사용
                if (boundaryResult.ImprovedPosition != proposedSplitPosition && boundaryResult.QualityScore > 0.7)
                {
                    // TODO: 실제로는 개선된 위치에 따라 currentChunk의 내용을 조정해야 함
                    // 현재는 로깅만 수행하고 기존 로직 유지
                    // 향후 정교한 위치 조정 로직 구현 예정
                }

                var optimalOverlapSize = _overlapManager.CalculateOptimalOverlap(
                    previousChunkText, currentChunkText, options);
                var chunkContent = CreateCoherentChunk(currentChunk, optimalOverlapSize, previousChunkText);
                chunks.Add(chunkContent);

                // Store full chunk text for context preservation
                previousChunkText = chunkContent;
                currentChunk = new List<SemanticUnit>(); // Start fresh, overlap will be added via previousChunkText
                currentSize = 0;
            }

            currentChunk.Add(unit);
            currentSize += unitSize;
        }

        // 마지막 청크 처리
        if (currentChunk.Count != 0)
        {
            var optimalOverlapSize = _overlapManager.CalculateOptimalOverlap(
                previousChunkText, string.Join("\n", currentChunk.Select(u => u.Content)), options);
            var chunkContent = CreateCoherentChunk(currentChunk, optimalOverlapSize, previousChunkText);
            chunks.Add(chunkContent);
        }

        return chunks;
    }

    private static List<string> OptimizeChunks(List<string> chunks, double qualityThreshold, int maxSize, ChunkingOptions options, ILanguageProfile languageProfile)
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

        // 작은 청크 병합 단계: MinChunkSize 이하인 청크를 인접 청크와 병합
        var minSize = Math.Max(options.MinChunkSize, 100); // Ensure reasonable minimum
        var merged = MergeSmallChunks(optimized, minSize, maxSize, languageProfile);

        // 중복 제거 단계: DeduplicateOverlaps 옵션이 활성화된 경우
        if (options.DeduplicateOverlaps)
        {
            merged = DeduplicateChunks(merged);
        }

        return merged;
    }

    /// <summary>
    /// Merge chunks smaller than minSize with adjacent chunks
    /// Uses language-aware sentence boundary detection
    /// </summary>
    private static List<string> MergeSmallChunks(List<string> chunks, int minSize, int maxSize, ILanguageProfile languageProfile)
    {
        var merged = new List<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var current = chunks[i];

            // Skip if already above minimum size
            if (current.Length >= minSize)
            {
                merged.Add(current);
                continue;
            }

            // Try to merge with next chunk
            if (i < chunks.Count - 1)
            {
                var next = chunks[i + 1];
                var combined = current + "\n\n" + next;

                // Check if combined chunk is within limits and ends properly
                if (combined.Length <= maxSize * 1.5 && languageProfile.EndsWithCompleteSentence(combined))
                {
                    merged.Add(combined);
                    i++; // Skip next chunk
                    continue;
                }
            }

            // Try to merge with previous chunk if this is the last small chunk
            if (current.Length < minSize && merged.Count > 0)
            {
                var previous = merged[^1];
                var combined = previous + "\n\n" + current;

                if (combined.Length <= maxSize * 1.5)
                {
                    merged[^1] = combined;
                    continue;
                }
            }

            // Add as-is if no merge possible
            merged.Add(current);
        }

        return merged;
    }

    /// <summary>
    /// Get language profile based on options or auto-detect from text
    /// </summary>
    private static ILanguageProfile GetLanguageProfile(string text, ChunkingOptions options)
    {
        // Use explicitly specified language if provided
        if (!string.IsNullOrEmpty(options.LanguageCode) &&
            !options.LanguageCode.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return _languageProfileProvider.GetProfile(options.LanguageCode);
        }

        // Auto-detect language from text content
        return _languageProfileProvider.DetectAndGetProfile(text);
    }

    /// <summary>
    /// Remove chunks with >50% duplicate content from previous chunk
    /// </summary>
    private static List<string> DeduplicateChunks(List<string> chunks)
    {
        if (chunks.Count <= 1) return chunks;

        var deduplicated = new List<string> { chunks[0] };

        for (int i = 1; i < chunks.Count; i++)
        {
            var current = chunks[i];
            var previous = deduplicated[^1];

            var similarity = CalculateContentSimilarity(current, previous);

            // Skip if >50% similar (likely duplicate from overlap)
            if (similarity > 0.5)
            {
                // Extract only the unique portion
                var uniqueContent = ExtractUniqueContent(current, previous);
                if (!string.IsNullOrWhiteSpace(uniqueContent) && uniqueContent.Length >= 100)
                {
                    deduplicated.Add(uniqueContent);
                }
                // Otherwise skip entirely
            }
            else
            {
                deduplicated.Add(current);
            }
        }

        return deduplicated;
    }

    /// <summary>
    /// Calculate content similarity between two chunks (0.0-1.0)
    /// </summary>
    private static double CalculateContentSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;

        var words1 = GetUniqueWords(text1);
        var words2 = GetUniqueWords(text2);

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// Extract content from current chunk that is not in previous chunk
    /// </summary>
    private static string ExtractUniqueContent(string current, string previous)
    {
        var currentLines = current.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var previousLines = new HashSet<string>(previous.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()));

        var uniqueLines = currentLines
            .Where(line => !previousLines.Contains(line.Trim()))
            .ToList();

        return string.Join("\n", uniqueLines);
    }

    private static bool ShouldStartNewChunk(List<SemanticUnit> currentChunk, SemanticUnit newUnit, double coherenceThreshold)
    {
        if (currentChunk.Count == 0)
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
        string globalDocumentDomain,
        string? documentHeader = null)
    {
        var importance = CalculateImportance(content) * importanceWeighting;
        var contextQuality = CalculateChunkQuality(content);

        var props = new Dictionary<string, object>
        {
            ["ContextQuality"] = contextQuality,
            ["SemanticCoherence"] = CalculateSemanticCoherence(content),
            ["StructuralElements"] = CountStructuralElements(content),
            ["ImportanceScore"] = importance
        };

        // 문서 헤더를 메타데이터로 저장 (첫 번째 청크에만 또는 모든 청크에)
        if (!string.IsNullOrEmpty(documentHeader))
        {
            props["DocumentHeader"] = documentHeader;
        }

        var chunk = new DocumentChunk
        {
            Id = Guid.NewGuid(),
            Content = content.Trim(),
            Metadata = metadata,
            Location = new SourceLocation
            {
                StartChar = startPosition,
                EndChar = startPosition + content.Length
            },
            Index = chunkIndex,
            Strategy = ChunkingStrategies.Intelligent,
            Tokens = EstimateTokenCount(content),
            CreatedAt = DateTime.UtcNow,
            Importance = importance,
            Props = props
        };

        // 품질 점수 실제 계산
        chunk.Quality = CalculateRealQualityScore(content, contextQuality);
        chunk.Props["RelevanceScore"] = CalculateRelevanceScore(content, globalTechKeywords, globalDocumentDomain);
        chunk.Density = CalculateInformationDensity(content);

        // LLM 최적화 메타데이터 자동 생성 (전역 컨텍스트 사용)
        EnhanceChunkForLlm(chunk, globalTechKeywords, globalDocumentDomain);

        // Phase 11: Vector Search Optimization
        ApplyVectorSearchOptimization(chunk, options);

        // Phase 12: Graph Search Optimization
        ApplyGraphSearchOptimization(chunk, options);

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

        // 청크별 고유 기술 키워드 추출 (청크 내용 기반)
        var chunkKeywords = DetectTechnicalKeywords(content);
        if (chunkKeywords.Count != 0)
        {
            contextParts.Add($"Tech: {string.Join(", ", chunkKeywords.Take(3))}");
            chunk.Props["TechnicalKeywords"] = chunkKeywords;
        }
        else if (globalTechKeywords.Count != 0)
        {
            // 청크별 키워드가 없으면 전역 키워드 사용
            contextParts.Add($"Tech: {string.Join(", ", globalTechKeywords.Take(3))}");
            chunk.Props["TechnicalKeywords"] = globalTechKeywords;
        }

        // 전역 문서 도메인 사용 (FileType과 일관성 확보)
        // FileType이 General이 아니면 FileType을 Domain으로 사용
        var domain = !string.IsNullOrEmpty(chunk.Metadata.FileType) && chunk.Metadata.FileType != "General"
            ? chunk.Metadata.FileType
            : globalDocumentDomain;
        chunk.Props["Domain"] = domain;
        if (domain != "General")
            contextParts.Add($"Domain: {domain}");

        // 청크 위치 정보 추가 (청크별 차별화)
        var positionHint = GetChunkPositionHint(chunk.Index);
        if (!string.IsNullOrEmpty(positionHint))
            contextParts.Add($"Pos: {positionHint}");

        // ContextualHeader 생성
        if (contextParts.Count != 0)
        {
            chunk.Props["ContextualHeader"] = $"[{string.Join(" | ", contextParts)}]";
        }

        // 구조적 역할 설정
        chunk.Props["StructuralRole"] = structuralRole;
    }

    /// <summary>
    /// 청크 위치에 대한 힌트 생성
    /// </summary>
    private static string GetChunkPositionHint(int index)
    {
        return index switch
        {
            0 => "intro",
            1 => "early",
            _ => "" // 중간 이후 청크는 위치 힌트 생략 (토큰 절약)
        };
    }

    /// <summary>
    /// 구조적 역할 자동 탐지
    /// </summary>
    private static string DetectStructuralRole(string content)
    {
        var trimmed = content.Trim();

        // 헤더 (마크다운 헤더)
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
            return "header";

        // 테이블
        if (trimmed.Contains('|') && trimmed.Count(c => c == '|') >= 2)
            return "table";

        // 코드 블록
        if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.Contains("```"))
            return "code_block";

        // 리스트
        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal) ||
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
        var regex = new Regex($@"\b{Regex.Escape(word)}\b",
            RegexOptions.IgnoreCase);
        return regex.IsMatch(text);
    }

    /// <summary>
    /// 문서 헤더(표지, 저작권 등)를 본문과 분리
    /// </summary>
    /// <param name="text">원본 문서 텍스트</param>
    /// <param name="options">청킹 옵션</param>
    /// <returns>분리된 헤더와 본문 텍스트 튜플</returns>
    private static (string? header, string body) SeparateDocumentHeader(string text, ChunkingOptions options)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, text);

        // 문단 분리
        var paragraphs = ParagraphRegex.Split(text)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        if (paragraphs.Count == 0)
            return (null, text);

        var headerParagraphs = new List<string>();
        var bodyStartIndex = 0;

        // 헤더 문단 감지: 짧은 문단들이 연속으로 나오는 경우 헤더로 처리
        for (int i = 0; i < Math.Min(options.MaxHeaderParagraphs, paragraphs.Count); i++)
        {
            var paragraph = paragraphs[i];

            // 헤더 문단 조건:
            // 1. 최대 길이 이하
            // 2. 헤더 패턴과 일치 (제목, 저작권, 날짜 등)
            if (paragraph.Length <= options.MaxHeaderParagraphLength && IsHeaderParagraph(paragraph))
            {
                headerParagraphs.Add(paragraph);
                bodyStartIndex = i + 1;
            }
            else
            {
                // 긴 문단이나 본문 시작 시 헤더 수집 종료
                break;
            }
        }

        // 헤더가 없으면 원본 반환
        if (headerParagraphs.Count == 0)
            return (null, text);

        // 헤더와 본문 분리
        var header = string.Join("\n", headerParagraphs);
        var bodyParagraphs = paragraphs.Skip(bodyStartIndex).ToList();
        var body = string.Join("\n\n", bodyParagraphs);

        return (header, body);
    }

    /// <summary>
    /// 문단이 헤더(제목, 저작권, 날짜 등)인지 판별
    /// </summary>
    private static bool IsHeaderParagraph(string paragraph)
    {
        // 빈 문단 제외
        if (string.IsNullOrWhiteSpace(paragraph))
            return false;

        var trimmed = paragraph.Trim();
        var lower = trimmed.ToLowerInvariant();

        // 저작권 관련 키워드
        if (lower.Contains("copyright") || lower.Contains("©") || lower.Contains("ⓒ") ||
            lower.Contains("all rights reserved") || lower.Contains("무단 복제") ||
            lower.Contains("복사") || lower.Contains("배포") || lower.Contains("금합니다"))
            return true;

        // 날짜 패턴 (연월일, 년도 등)
        if (Regex.IsMatch(trimmed, @"\d{4}[-./년]\d{1,2}[-./월]\d{1,2}") ||
            Regex.IsMatch(trimmed, @"\d{4}년") ||
            Regex.IsMatch(trimmed, @"\d{2}/\d{2}/\d{4}"))
            return true;

        // 마크다운 헤딩 (# 또는 Paragraph 표시)
        if (trimmed.StartsWith('#') || lower.StartsWith("paragraph"))
            return true;

        // 회사명 패턴 (주식회사, ㈜, Inc., Ltd., Corp.)
        if (Regex.IsMatch(trimmed, @"(주식회사|㈜|\(주\)|Inc\.|Ltd\.|Corp\.|LLC|Co\.,)"))
            return true;

        // 문서 타입 표시 (보고서, 분석, 제안서 등)
        if (lower.Contains("보고서") || lower.Contains("분석") || lower.Contains("제안서") ||
            lower.Contains("report") || lower.Contains("analysis") || lower.Contains("proposal"))
            return true;

        // 짧은 제목형 문단 (30자 이하, 마침표/물음표 없음)
        if (trimmed.Length <= 30 && !trimmed.EndsWith('.') && !trimmed.EndsWith('?') && !trimmed.EndsWith('!'))
            return true;

        return false;
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

    private static string CreateCoherentChunk(List<SemanticUnit> units, int overlapSize, string previousChunk = "")
    {
        var content = string.Join(" ", units.Select(u => u.Content));

        // Add adaptive overlap from previous chunk if provided
        if (!string.IsNullOrEmpty(previousChunk) && overlapSize > 0)
        {
            var contextPreservingOverlap = _overlapManager.CreateContextPreservingOverlap(previousChunk, overlapSize);

            // Ensure we don't duplicate content that's already in the units
            if (!string.IsNullOrEmpty(contextPreservingOverlap) && !content.StartsWith(contextPreservingOverlap, StringComparison.Ordinal))
            {
                content = contextPreservingOverlap + " " + content;
            }
        }

        return content;
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
        return content.Contains('|') && content.Count(c => c == '|') >= 2;
    }

    /// <summary>
    /// SemanticUnits 중에 테이블이 포함되어 있는지 확인
    /// </summary>
    private static bool ContainsAnyTable(List<SemanticUnit> units)
    {
        return units.Any(unit => IsTableRow(unit.Content));
    }

    private static List<SemanticUnit> GetOverlapUnits(List<SemanticUnit> currentChunk, int targetOverlapSize, double relevance)
    {
        if (targetOverlapSize <= 0 || currentChunk.Count == 0)
            return new List<SemanticUnit>();

        // Calculate overlap size based on characters/tokens, not unit count
        var adjustedOverlapSize = (int)(targetOverlapSize * relevance);
        var overlapUnits = new List<SemanticUnit>();
        var currentOverlapSize = 0;

        // Add units from the end until we reach the target overlap size
        for (int i = currentChunk.Count - 1; i >= 0; i--)
        {
            var unit = currentChunk[i];
            var unitSize = EstimateTokenCount(unit.Content);

            if (currentOverlapSize + unitSize > adjustedOverlapSize * 1.5) // Allow 50% overshoot
                break;

            overlapUnits.Insert(0, unit); // Insert at beginning to maintain order
            currentOverlapSize += unitSize;

            if (currentOverlapSize >= adjustedOverlapSize)
                break;
        }

        return overlapUnits;
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
        if (string.IsNullOrWhiteSpace(content))
            return 0.0;

        // 문장 단위로 분리
        var sentences = ExtractSentences(content);
        if (sentences.Count <= 1)
            return 0.5; // 단일 문장은 기본 응집성 부여

        // 단어 추출 (불용어 제외)
        var words = content.ToLowerInvariant()
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !IsStopWord(w))
            .ToList();

        if (words.Count == 0)
            return 0.0;

        // 1. 어휘 반복도 (Lexical Overlap) - 의미적 연결성 지표
        var wordFreq = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var repeatedWords = wordFreq.Count(kvp => kvp.Value > 1);
        var lexicalOverlap = words.Distinct().Count() > 0
            ? (double)repeatedWords / words.Distinct().Count()
            : 0.0;

        // 2. 문장 간 연결성 - 인접 문장 간 공통 단어 비율
        double sentenceConnectivity = 0.0;
        if (sentences.Count >= 2)
        {
            var connectivityScores = new List<double>();
            for (int i = 0; i < sentences.Count - 1; i++)
            {
                var words1 = sentences[i].ToLowerInvariant()
                    .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3)
                    .ToHashSet();
                var words2 = sentences[i + 1].ToLowerInvariant()
                    .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                    .Where(w => w.Length > 3)
                    .ToHashSet();

                if (words1.Count > 0 && words2.Count > 0)
                {
                    var overlap = words1.Intersect(words2).Count();
                    var unionCount = words1.Union(words2).Count();
                    connectivityScores.Add((double)overlap / unionCount);
                }
            }
            sentenceConnectivity = connectivityScores.Count > 0 ? connectivityScores.Average() : 0.0;
        }

        // 3. 구조적 일관성 - 문단/리스트/헤더 구조 유무
        var hasStructure = HeaderRegex.IsMatch(content) || ListItemRegex.IsMatch(content);
        var structureBonus = hasStructure ? 0.1 : 0.0;

        // 가중 평균으로 최종 응집성 계산
        var coherence = 0.4 * lexicalOverlap + 0.5 * sentenceConnectivity + structureBonus;

        return Math.Min(coherence, 1.0);
    }

    /// <summary>
    /// 불용어 판별
    /// </summary>
    private static bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string>
        {
            // 영어 불용어
            "the", "and", "for", "are", "but", "not", "you", "all", "can", "had", "her", "was", "one", "our", "out",
            "that", "this", "with", "from", "have", "been", "will", "they", "were", "their", "what", "when", "where",
            "which", "there", "would", "could", "should", "about", "after", "these", "those", "being", "other",
            // 한국어 불용어
            "그리고", "하지만", "그러나", "또한", "그래서", "따라서", "그러므로", "입니다", "있습니다", "됩니다",
            "합니다", "것입니다", "대한", "위한", "통해", "이런", "저런", "어떤"
        };
        return stopWords.Contains(word.ToLowerInvariant());
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
            if (currentLength + sentence.Length > maxSize && currentPart.Count != 0)
            {
                result.Add(string.Join(" ", currentPart));
                currentPart.Clear();
                currentLength = 0;
            }

            currentPart.Add(sentence);
            currentLength += sentence.Length;
        }

        if (currentPart.Count != 0)
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

        // 🔥 테이블 보호 로직: TABLE_START/END가 포함된 경우 특별 처리
        if (chunk.Contains("<!-- TABLE_START -->") && chunk.Contains("<!-- TABLE_END -->"))
        {
            return EnforceMaxSizeForTable(chunk, maxSize);
        }

        // 🔥 다중 테이블 행 보호: | 문자가 많은 경우 (테이블로 추정)
        var tableRowCount = chunk.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains('|') && line.Count(c => c == '|') >= 2);

        if (tableRowCount >= 3) // 3개 이상의 테이블 행이 있으면 테이블로 간주
        {
            return EnforceMaxSizeForTable(chunk, maxSize);
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
                if (currentPart.Count != 0)
                {
                    result.Add(string.Join(" ", currentPart));
                    currentPart.Clear();
                    currentLength = 0;
                }

                // 긴 문장을 단어 단위로 분할
                var wordChunks = SplitByWords(sentence, maxSize);
                result.AddRange(wordChunks);
            }
            else if (currentLength + sentence.Length + 1 > maxSize && currentPart.Count != 0) // +1 for space
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

    /// <summary>
    /// 테이블 전용 크기 강제 분할 - 테이블 구조와 헤더 보존에 최적화
    /// </summary>
    private static List<string> EnforceMaxSizeForTable(string tableChunk, int maxSize)
    {
        var result = new List<string>();

        // 테이블이 maxSize 이하면 그대로 유지
        // 참고: 호출자(OptimizeChunks)가 이미 테이블에 대해 maxSize를 2배로 설정함
        if (tableChunk.Length <= maxSize)
        {
            result.Add(tableChunk);
            return result;
        }

        // 테이블 마커를 기준으로 분할
        if (tableChunk.Contains("<!-- TABLE_START -->") && tableChunk.Contains("<!-- TABLE_END -->"))
        {
            var parts = SplitTableByMarkers(tableChunk, maxSize);
            foreach (var part in parts)
            {
                if (part.Length > maxSize)
                {
                    // maxSize 초과 시 단어 단위로 분할
                    result.AddRange(SplitByWords(part, maxSize));
                }
                else
                {
                    result.Add(part);
                }
            }
            return result;
        }

        // 테이블 행 단위로 분할
        var rowParts = SplitTableByRows(tableChunk, maxSize);
        foreach (var part in rowParts)
        {
            if (part.Length > maxSize)
            {
                // maxSize 초과 시 단어 단위로 분할
                result.AddRange(SplitByWords(part, maxSize));
            }
            else
            {
                result.Add(part);
            }
        }
        return result;
    }

    /// <summary>
    /// 테이블 마커를 기준으로 테이블을 분할
    /// </summary>
    private static List<string> SplitTableByMarkers(string tableChunk, int maxSize)
    {
        var result = new List<string>();
        var lines = tableChunk.Split('\n', StringSplitOptions.None);

        var headerLines = new List<string>();
        var currentTableLines = new List<string>();
        var isInTable = false;
        var tableStartFound = false;

        foreach (var line in lines)
        {
            if (line.Contains("<!-- TABLE_START -->"))
            {
                isInTable = true;
                tableStartFound = true;
                currentTableLines.Add(line);
                continue;
            }

            if (line.Contains("<!-- TABLE_END -->"))
            {
                currentTableLines.Add(line);

                // 전체 테이블 크기 확인
                var completeTable = string.Join("\n", currentTableLines);
                if (completeTable.Length <= maxSize * 2) // 2배까지 허용
                {
                    result.Add(completeTable);
                }
                else
                {
                    // 테이블이 너무 크면 행 단위로 분할
                    var splitTables = SplitLargeTableContent(currentTableLines, maxSize);
                    result.AddRange(splitTables);
                }

                currentTableLines.Clear();
                isInTable = false;
                continue;
            }

            if (isInTable)
            {
                currentTableLines.Add(line);
            }
            else if (!tableStartFound)
            {
                // 테이블 시작 전의 내용
                headerLines.Add(line);
            }
        }

        // 테이블이 완료되지 않은 경우 처리
        if (currentTableLines.Any())
        {
            var incompleteTable = string.Join("\n", currentTableLines);
            result.Add(incompleteTable);
        }

        return result;
    }

    /// <summary>
    /// 마커 없는 테이블을 행 단위로 분할
    /// </summary>
    private static List<string> SplitTableByRows(string tableChunk, int maxSize)
    {
        var result = new List<string>();
        var lines = tableChunk.Split('\n', StringSplitOptions.None);

        // 헤더와 구분자 식별
        var headerLine = "";
        var separatorLine = "";
        var dataLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains('|') && line.Count(c => c == '|') >= 2)
            {
                if (string.IsNullOrEmpty(headerLine))
                {
                    headerLine = line;
                }
                else if (string.IsNullOrEmpty(separatorLine) && (line.Contains("---") || line.Contains(":-")))
                {
                    separatorLine = line;
                }
                else
                {
                    dataLines.Add(line);
                }
            }
        }

        if (string.IsNullOrEmpty(headerLine))
        {
            // 테이블이 아닌 것으로 판단하고 일반 분할
            return SplitByWords(tableChunk, maxSize);
        }

        // 헤더 + 구분자 크기
        var headerPart = string.IsNullOrEmpty(separatorLine) 
            ? headerLine 
            : headerLine + "\n" + separatorLine;
        var headerSize = headerPart.Length;
        var currentLines = new List<string>();
        var currentSize = headerSize;

        foreach (var dataLine in dataLines)
        {
            var lineLength = dataLine.Length + 1; // +1 for newline

            // 단일 행이 maxSize - headerSize보다 큰 경우 단어 단위 분할 필요
            if (lineLength > maxSize - headerSize)
            {
                // 현재 누적된 행이 있으면 먼저 청크로 저장
                if (currentLines.Any())
                {
                    var tableContent = headerPart + "\n" + string.Join("\n", currentLines);
                    result.Add(tableContent);
                    currentLines.Clear();
                    currentSize = headerSize;
                }

                // 긴 행을 단어 단위로 분할하여 별도 청크로 처리
                var splitRows = SplitByWords(dataLine, maxSize - headerSize);
                foreach (var splitRow in splitRows)
                {
                    result.Add(headerPart + "\n" + splitRow);
                }
                continue;
            }

            if (currentSize + lineLength > maxSize && currentLines.Any())
            {
                // 현재 테이블 파트 완성
                var tableContent = headerPart + "\n" + string.Join("\n", currentLines);
                result.Add(tableContent);

                // 다음 파트 시작
                currentLines.Clear();
                currentSize = headerSize;
            }

            currentLines.Add(dataLine);
            currentSize += lineLength;
        }

        // 마지막 파트
        if (currentLines.Any())
        {
            var tableContent = headerPart + "\n" + string.Join("\n", currentLines);
            result.Add(tableContent);
        }

        // 결과가 없으면 원본 반환 (빈 테이블 등)
        if (!result.Any())
        {
            result.Add(tableChunk);
        }

        return result;
    }

    /// <summary>
    /// 큰 테이블 내용을 분할
    /// </summary>
    private static List<string> SplitLargeTableContent(List<string> tableLines, int maxSize)
    {
        var result = new List<string>();

        // TABLE_START와 TABLE_END를 찾기
        var startIndex = tableLines.FindIndex(line => line.Contains("<!-- TABLE_START -->"));
        var endIndex = tableLines.FindIndex(line => line.Contains("<!-- TABLE_END -->"));

        if (startIndex == -1 || endIndex == -1)
        {
            // 마커를 찾을 수 없으면 전체 반환
            result.Add(string.Join("\n", tableLines));
            return result;
        }

        var headerAndStart = new List<string>();
        var dataLines = new List<string>();
        var endMarker = tableLines[endIndex];

        // 헤더와 구분자 수집 (처음 2-3줄)
        for (int i = startIndex; i < Math.Min(startIndex + 3, endIndex); i++)
        {
            headerAndStart.Add(tableLines[i]);
        }

        // 데이터 행들 수집
        for (int i = startIndex + 3; i < endIndex; i++)
        {
            if (i < tableLines.Count)
            {
                dataLines.Add(tableLines[i]);
            }
        }

        var headerSize = string.Join("\n", headerAndStart).Length;
        var currentLines = new List<string>();
        var currentSize = headerSize;

        foreach (var dataLine in dataLines)
        {
            if (currentSize + dataLine.Length + 1 > maxSize && currentLines.Any())
            {
                // 현재 테이블 파트 완성
                var tablePart = new List<string>();
                tablePart.AddRange(headerAndStart);
                tablePart.AddRange(currentLines);
                tablePart.Add(endMarker);

                result.Add(string.Join("\n", tablePart));

                // 다음 파트 시작
                currentLines.Clear();
                currentSize = headerSize;
            }

            currentLines.Add(dataLine);
            currentSize += dataLine.Length + 1;
        }

        // 마지막 파트
        if (currentLines.Any())
        {
            var tablePart = new List<string>();
            tablePart.AddRange(headerAndStart);
            tablePart.AddRange(currentLines);
            tablePart.Add(endMarker);

            result.Add(string.Join("\n", tablePart));
        }

        return result;
    }

    private static T GetStrategyOption<T>(ChunkingOptions options, string key, T defaultValue)
    {
        // 옵션 단순화: 항상 기본값 사용 (최고 품질 기본 설정)
        return defaultValue;
    }

    /// <summary>
    /// 테이블 시작 여부 확인
    /// </summary>
    private static bool IsTableStart(string content)
    {
        return TableRegex.IsMatch(content) || content.Contains('|') && content.Count(c => c == '|') >= 3;
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
            if (content.Contains('|') && (content.Count(c => c == '|') >= 2 || TableSeparatorRegex.IsMatch(content)))
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
        public bool IsSectionHeader { get; set; }
    }

    /// <summary>
    /// 실제 품질 점수 계산 (Mock이 아닌 실제 알고리즘)
    /// </summary>
    private static double CalculateRealQualityScore(string content, double contextQuality)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var scores = new List<double>();

        // 1. 텍스트 완성도 점수 (0.0-1.0)
        var completenessScore = CalculateCompletenessScore(content);
        scores.Add(completenessScore);

        // 2. 구조적 일관성 점수 (0.0-1.0) 
        var structuralScore = CalculateStructuralConsistency(content);
        scores.Add(structuralScore);

        // 3. 정보 밀도 적합성 점수 (0.0-1.0)
        var densityScore = Math.Min(CalculateInformationDensity(content), 1.0);
        scores.Add(densityScore);

        // 4. 컨텍스트 품질 점수 (이미 계산된 값)
        scores.Add(Math.Min(contextQuality, 1.0));

        // 가중 평균 계산
        var qualityScore = scores.Average();

        return Math.Max(0.0, Math.Min(1.0, qualityScore));
    }

    /// <summary>
    /// 문서 맥락 관련성 점수 계산
    /// </summary>
    private static double CalculateRelevanceScore(string content, List<string> globalTechKeywords, string documentDomain)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var scores = new List<double>();

        // 1. 기술 키워드 관련성 (0.0-1.0)
        var keywordRelevance = CalculateKeywordRelevance(content, globalTechKeywords);
        scores.Add(keywordRelevance);

        // 2. 도메인 적합성 (0.0-1.0)
        var domainRelevance = CalculateDomainRelevance(content, documentDomain);
        scores.Add(domainRelevance);

        // 3. 의미적 일관성 (0.0-1.0)
        var semanticCoherence = CalculateSemanticCoherence(content);
        scores.Add(semanticCoherence);

        return scores.Average();
    }

    /// <summary>
    /// 정보 밀도 계산 (단위 길이당 정보량)
    /// </summary>
    private static double CalculateInformationDensity(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var contentLength = content.Length;
        if (contentLength == 0) return 0.0;

        // 정보 요소 계산
        var sentences = ExtractSentences(content).Count;
        var technicalTerms = CountTechnicalTerms(content);
        var structuralElements = CountStructuralElements(content);
        var uniqueWords = content.ToLowerInvariant()
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .Distinct()
            .Count();

        // 정보 밀도 = (문장수 + 기술용어 + 구조요소 + 고유단어수) / 문자수 * 1000
        var informationScore = (sentences + technicalTerms + structuralElements + uniqueWords) * 1000.0 / contentLength;

        // 0.0-1.0 범위로 정규화 (경험적 기준: 50 이상은 1.0)
        return Math.Min(informationScore / 50.0, 1.0);
    }

    /// <summary>
    /// 텍스트 완성도 점수 계산
    /// </summary>
    private static double CalculateCompletenessScore(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var scores = new List<double>();

        // 1. 문장 완성도 - 문장이 완전한지 확인
        var sentences = ExtractSentences(content);
        var completeSentences = sentences.Count(s => s.Trim().EndsWith(".", StringComparison.Ordinal) || s.Trim().EndsWith("!", StringComparison.Ordinal) || s.Trim().EndsWith("?", StringComparison.Ordinal));
        var sentenceCompleteness = sentences.Count > 0 ? (double)completeSentences / sentences.Count : 0.0;
        scores.Add(sentenceCompleteness);

        // 2. 구조적 완성도 - 표, 리스트 등이 완전한지 확인
        var structuralCompleteness = CalculateStructuralCompleteness(content);
        scores.Add(structuralCompleteness);

        // 3. 길이 적합성 - 너무 짧거나 길지 않은지 확인
        var lengthApproppriateness = CalculateLengthAppropriateness(content.Length);
        scores.Add(lengthApproppriateness);

        return scores.Average();
    }

    /// <summary>
    /// 구조적 일관성 점수 계산
    /// </summary>
    private static double CalculateStructuralConsistency(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0.0;

        var scores = new List<double>();

        // 1. 헤더 계층 일관성
        var headerConsistency = CalculateHeaderConsistency(content);
        scores.Add(headerConsistency);

        // 2. 리스트 구조 일관성  
        var listConsistency = CalculateListConsistency(content);
        scores.Add(listConsistency);

        // 3. 테이블 구조 일관성
        var tableConsistency = CalculateTableConsistency(content);
        scores.Add(tableConsistency);

        return scores.Average();
    }

    // 보조 메서드들
    private static double CalculateKeywordRelevance(string content, List<string> keywords)
    {
        if (keywords == null || keywords.Count == 0) return 0.5;

        var contentLower = content.ToLowerInvariant();
        var matchedKeywords = keywords.Count(k => contentLower.Contains(k.ToLowerInvariant()));

        return (double)matchedKeywords / keywords.Count;
    }

    private static double CalculateDomainRelevance(string content, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return 0.5;

        var domainKeywords = GetDomainSpecificKeywords(domain);
        return CalculateKeywordRelevance(content, domainKeywords);
    }

    private static List<string> GetDomainSpecificKeywords(string domain)
    {
        return domain.ToLowerInvariant() switch
        {
            "technical" => new List<string> { "system", "technology", "implementation", "architecture", "design", "development" },
            "business" => new List<string> { "requirement", "process", "workflow", "business", "stakeholder", "objective" },
            "academic" => new List<string> { "research", "analysis", "methodology", "conclusion", "hypothesis", "evidence" },
            _ => new List<string> { "information", "content", "data", "process", "system" }
        };
    }

    private static int CountTechnicalTerms(string content)
    {
        var technicalPatterns = new[] {
            @"\b[A-Z]{2,}\b", // 약어 (API, HTTP 등)
            @"\b\w+\.\w+\b", // 네임스페이스/도메인 형태
            @"\b\w*(?:Service|Manager|Controller|Repository|Factory|Builder|Strategy)\b" // 일반적인 패턴
        };

        return technicalPatterns.Sum(pattern => Regex.Matches(content, pattern).Count);
    }

    private static double CalculateStructuralCompleteness(string content)
    {
        var scores = new List<double>();

        // 테이블 완성도
        if (content.Contains('|'))
        {
            var tableComplete = content.Contains("TABLE_START") && content.Contains("TABLE_END") ? 1.0 : 0.8;
            scores.Add(tableComplete);
        }

        // 리스트 완성도
        if (content.Contains('•') || content.Contains('-') || Regex.IsMatch(content, @"^\d+\."))
        {
            var listComplete = content.Contains("LIST_START") && content.Contains("LIST_END") ? 1.0 : 0.8;
            scores.Add(listComplete);
        }

        // 코드 블록 완성도
        if (content.Contains("```"))
        {
            var codeComplete = content.Contains("CODE_START") && content.Contains("CODE_END") ? 1.0 : 0.8;
            scores.Add(codeComplete);
        }

        return scores.Count != 0 ? scores.Average() : 1.0; // 구조적 요소가 없으면 완전하다고 간주
    }

    private static double CalculateLengthAppropriateness(int length)
    {
        // Phase 15: 적절한 청크 길이 범위 최적화: 300-1500자
        if (length < 150) return 0.3; // 너무 짧음
        if (length < 300) return 0.6; // 짧음
        if (length <= 1500) return 1.0; // 적절함
        if (length <= 2000) return 0.8; // 다소 길음
        return 0.6; // 너무 길음
    }

    /// <summary>
    /// Phase 15: 강화된 헤더 계층 구조 일관성 계산
    /// 더 정교한 계층 구조 분석 및 번호 체계 인식 포함
    /// </summary>
    private static double CalculateHeaderConsistency(string content)
    {
        var markdownHeaderMatches = Regex.Matches(content, @"^#{1,6}\s+", RegexOptions.Multiline);
        var numberedHeaderMatches = Regex.Matches(content, @"^\d+[\.\)]\s+\w+", RegexOptions.Multiline);
        var hierarchicalHeaderMatches = Regex.Matches(content, @"^\d+\.\d+[\.\)]*\s+\w+", RegexOptions.Multiline);

        var totalHeaders = markdownHeaderMatches.Count + numberedHeaderMatches.Count + hierarchicalHeaderMatches.Count;
        if (totalHeaders <= 1) return 1.0;

        var consistency = 0.0;
        var weights = new List<double>();

        // 1. 마크다운 헤더 일관성 분석
        if (markdownHeaderMatches.Count > 1)
        {
            var mdLevels = markdownHeaderMatches.Cast<Match>()
                .Select(m => m.Value.Count(c => c == '#'))
                .ToList();

            var mdConsistency = CalculateMarkdownLevelConsistency(mdLevels);
            consistency += mdConsistency * markdownHeaderMatches.Count;
            weights.Add(markdownHeaderMatches.Count);
        }

        // 2. 번호 체계 헤더 일관성 분석
        if (numberedHeaderMatches.Count > 1)
        {
            var numberedConsistency = AnalyzeNumberedSequence(numberedHeaderMatches);
            consistency += numberedConsistency * numberedHeaderMatches.Count;
            weights.Add(numberedHeaderMatches.Count);
        }

        // 3. 계층적 번호 체계 일관성 분석 (1.1, 1.2, 2.1 등)
        if (hierarchicalHeaderMatches.Count > 1)
        {
            var hierarchicalConsistency = AnalyzeHierarchicalSequence(hierarchicalHeaderMatches);
            consistency += hierarchicalConsistency * hierarchicalHeaderMatches.Count;
            weights.Add(hierarchicalHeaderMatches.Count);
        }

        // 4. 혼합 체계 패널티 (서로 다른 번호 체계가 섞여있으면 일관성 감소)
        var mixedSystemsPenalty = CalculateMixedSystemsPenalty(
            markdownHeaderMatches.Count,
            numberedHeaderMatches.Count,
            hierarchicalHeaderMatches.Count);

        var weightedConsistency = weights.Count > 0 ? consistency / weights.Sum() : 1.0;
        return Math.Max(0.0, weightedConsistency - mixedSystemsPenalty);
    }

    /// <summary>
    /// 마크다운 헤더 레벨 일관성 계산
    /// </summary>
    private static double CalculateMarkdownLevelConsistency(List<int> levels)
    {
        var levelJumps = levels.Zip(levels.Skip(1), (a, b) => Math.Abs(a - b)).ToList();
        var avgJump = levelJumps.Count != 0 ? levelJumps.Average() : 0;

        // 레벨 점프가 1 이하면 좋은 일관성, 2 이상이면 일관성 저하
        return Math.Max(0.0, 1.0 - (avgJump - 1.0) * 0.3);
    }

    /// <summary>
    /// 번호 체계 순서 일관성 분석
    /// </summary>
    private static double AnalyzeNumberedSequence(MatchCollection matches)
    {
        var numbers = new List<int>();
        foreach (Match match in matches)
        {
            var numberText = Regex.Match(match.Value, @"^\d+").Value;
            if (int.TryParse(numberText, out var number))
            {
                numbers.Add(number);
            }
        }

        if (numbers.Count <= 1) return 1.0;

        var isSequential = true;
        var expectedNext = numbers[0] + 1;

        for (int i = 1; i < numbers.Count; i++)
        {
            if (numbers[i] != expectedNext)
            {
                isSequential = false;
                break;
            }
            expectedNext = numbers[i] + 1;
        }

        // 순차적이면 1.0, 그렇지 않으면 부분 점수
        return isSequential ? 1.0 : 0.6;
    }

    /// <summary>
    /// 계층적 번호 체계 일관성 분석 (1.1, 1.2, 2.1 형태)
    /// </summary>
    private static double AnalyzeHierarchicalSequence(MatchCollection matches)
    {
        var hierarchicalNumbers = new List<(int major, int minor)>();

        foreach (Match match in matches)
        {
            var parts = match.Value.Split('.');
            if (parts.Length >= 2 &&
                int.TryParse(parts[0], out var major) &&
                int.TryParse(parts[1].TrimEnd('.', ')', ' ').Split(' ')[0], out var minor))
            {
                hierarchicalNumbers.Add((major, minor));
            }
        }

        if (hierarchicalNumbers.Count <= 1) return 1.0;

        var consistency = 0.0;
        var groupedByMajor = hierarchicalNumbers.GroupBy(h => h.major).ToList();

        foreach (var group in groupedByMajor)
        {
            var minors = group.Select(g => g.minor).OrderBy(m => m).ToList();

            // 각 주요 번호 그룹 내에서 부번호가 순차적인지 확인
            var isSequential = true;
            for (int i = 1; i < minors.Count; i++)
            {
                if (minors[i] != minors[i - 1] + 1)
                {
                    isSequential = false;
                    break;
                }
            }

            consistency += isSequential ? 1.0 : 0.7;
        }

        return consistency / groupedByMajor.Count;
    }

    /// <summary>
    /// 혼합 헤더 체계 패널티 계산
    /// </summary>
    private static double CalculateMixedSystemsPenalty(int markdownCount, int numberedCount, int hierarchicalCount)
    {
        var systemsUsed = 0;
        if (markdownCount > 0) systemsUsed++;
        if (numberedCount > 0) systemsUsed++;
        if (hierarchicalCount > 0) systemsUsed++;

        // 하나의 체계만 사용하면 패널티 없음, 여러 체계 혼용 시 패널티
        return systemsUsed switch
        {
            1 => 0.0,  // 단일 체계 - 패널티 없음
            2 => 0.1,  // 2개 체계 혼용 - 약간의 패널티
            3 => 0.2,  // 3개 체계 혼용 - 더 큰 패널티
            _ => 0.0
        };
    }

    private static double CalculateListConsistency(string content)
    {
        var lines = content.Split('\n');
        var listLines = lines.Where(line =>
            Regex.IsMatch(line.Trim(), @"^[-•*]\s+") ||
            Regex.IsMatch(line.Trim(), @"^\d+\.\s+")
        ).ToList();

        if (listLines.Count <= 1) return 1.0;

        // 리스트 마커의 일관성 확인
        var hasUnorderedMarkers = listLines.Any(line => Regex.IsMatch(line.Trim(), @"^[-•*]\s+"));
        var hasOrderedMarkers = listLines.Any(line => Regex.IsMatch(line.Trim(), @"^\d+\.\s+"));

        // 혼재하지 않으면 일관성 높음
        return (hasUnorderedMarkers && hasOrderedMarkers) ? 0.7 : 1.0;
    }

    private static double CalculateTableConsistency(string content)
    {
        var tableLines = content.Split('\n').Where(line => line.Contains('|')).ToList();
        if (tableLines.Count <= 1) return 1.0;

        // 테이블 행의 컬럼 수 일관성 확인
        var columnCounts = tableLines.Select(line => line.Split('|', StringSplitOptions.RemoveEmptyEntries).Length).ToList();
        var avgColumns = columnCounts.Average();
        var maxDeviation = columnCounts.Max(c => Math.Abs(c - avgColumns));

        return Math.Max(0.0, 1.0 - (maxDeviation / avgColumns));
    }

    /// <summary>
    /// Phase 11: Apply vector search optimization to chunk
    /// </summary>
    private static void ApplyVectorSearchOptimization(DocumentChunk chunk, ChunkingOptions options)
    {
        try
        {
            // 1. Vector search optimization
            var vectorOptions = new VectorSearchOptions
            {
                MinSemanticDensity = 0.3,
                MaxSemanticDensity = 0.8,
                LowercaseNormalization = false, // Preserve case for technical terms
                ReplaceUrls = true,
                NormalizeNumbers = false, // Keep exact numbers for technical content
                RemoveSpecialCharacters = false,
                MaxTokensPerChunk = options.MaxChunkSize / 4 // Estimate tokens
            };

            var optimizedChunk = _vectorSearchOptimizer.OptimizeForVectorSearch(chunk, vectorOptions);

            // Store optimization results in chunk properties
            chunk.Props["VectorOptimized"] = true;
            chunk.Props["SemanticDensity"] = optimizedChunk.SemanticDensity;
            chunk.Props["OptimizationQuality"] = optimizedChunk.OptimizationMetrics.QualityScore;
            chunk.Props["EmbeddingHints"] = optimizedChunk.EmbeddingHints;

            // 언어 감지 일관성 확보: EmbeddingHints의 감지된 언어를 Metadata에 동기화
            if (optimizedChunk.EmbeddingHints != null &&
                !string.IsNullOrEmpty(optimizedChunk.EmbeddingHints.PrimaryLanguage))
            {
                chunk.Metadata.Language = optimizedChunk.EmbeddingHints.PrimaryLanguage;
            }

            // 2. Search metadata enrichment
            var enrichmentOptions = new EnrichmentOptions
            {
                MaxKeywordsPerChunk = 15,
                MaxPhrasesPerChunk = 8,
                EnableSemanticTagging = true,
                EnableRelationshipMapping = false // Skip for performance
            };

            var enrichedChunk = _metadataEnricher.EnrichWithSearchMetadata(chunk, enrichmentOptions);

            // Store enrichment results
            chunk.Props["SearchKeywords"] = enrichedChunk.ExtractedKeywords.TfIdfKeywords.Take(10).Select(k => k.Keyword).ToList();
            chunk.Props["SemanticTags"] = enrichedChunk.SemanticTags.Topics;
            chunk.Props["ContentType"] = enrichedChunk.SemanticTags.ContentType;
            chunk.Props["SearchScores"] = enrichedChunk.SearchScores;

            // 3. Hybrid search preprocessing
            var hybridOptions = new HybridSearchOptions
            {
                BM25Options = new BM25Options
                {
                    RemoveStopWords = true,
                    ApplyStemming = false, // Keep original terms
                    MinTokenLength = 2,
                    K1 = 1.2,
                    B = 0.75
                },
                EnableSynonymMapping = false, // Skip for performance
                EnableQueryExpansion = true,
                DefaultKeywordWeight = 0.6,
                DefaultSemanticWeight = 0.4
            };

            var hybridResult = _hybridPreprocessor.PreprocessForHybridSearch(chunk, hybridOptions);

            // Store hybrid preprocessing results
            chunk.Props["HybridPreprocessed"] = true;
            // 청크별 고유 BM25Terms: 빈도순 정렬 후 상위 10개 추출
            var bm25Terms = hybridResult.BM25Preprocessing.TermFrequencies
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .Take(10)
                .ToList();
            chunk.Props["BM25Terms"] = bm25Terms;
            chunk.Props["HybridRatio"] = hybridResult.WeightCalculationInfo.RecommendedHybridRatio;
            chunk.Props["RerankingHints"] = hybridResult.RerankingHints;

            // 4. Search quality evaluation
            var qualityOptions = new SearchQualityOptions
            {
                MinQualityThreshold = 0.6,
                EnableABTesting = false, // Skip for performance
                EvaluationSampleSize = 100,
                EvaluationPeriod = TimeSpan.FromDays(1)
            };

            var qualityResult = _qualityEvaluator.EvaluateSearchQuality(chunk, qualityOptions);

            // Store quality evaluation results
            chunk.Props["SearchQualityScore"] = qualityResult.OverallQualityScore;
            chunk.Props["RetrievalRecall"] = qualityResult.RetrievalRecall?.PredictedRecallScore ?? 0.5;
            chunk.Props["DistinctivenessScore"] = qualityResult.DistinctivenessScore?.OverallDistinctiveness ?? 0.5;
            chunk.Props["SemanticCompleteness"] = qualityResult.SemanticCompleteness?.SelfContainment ?? 0.5;

            // 5. Update overall quality score with search optimization
            var originalQuality = chunk.Quality;
            var searchQuality = qualityResult.OverallQualityScore;

            // Weighted combination: 70% original quality + 30% search quality
            chunk.Quality = (originalQuality * 0.7) + (searchQuality * 0.3);

            // Mark as search-optimized
            chunk.Props["SearchOptimized"] = true;
            chunk.Props["OptimizationTimestamp"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // Log error but don't fail chunk creation
            chunk.Props["SearchOptimizationError"] = ex.Message;
            chunk.Props["SearchOptimized"] = false;
        }
    }

    /// <summary>
    /// Phase 12: Apply graph search optimization to chunk
    /// </summary>
    private static void ApplyGraphSearchOptimization(DocumentChunk chunk, ChunkingOptions options)
    {
        try
        {
            // 1. Entity extraction
            var entityOptions = new EntityExtractionOptions
            {
                EnableNER = true,
                EnableRelationshipExtraction = true,
                EnableCoreferenceResolution = true,
                MaxEntitiesPerChunk = 50,
                MinEntityConfidence = 0.5,
                ExtractPersons = true,
                ExtractOrganizations = true,
                ExtractLocations = true,
                ExtractDates = true,
                ExtractConcepts = true
            };

            var entityResult = _entityExtractor.ExtractEntitiesAndRelationships(chunk, entityOptions);

            // Store entity extraction results
            chunk.Props["EntitiesExtracted"] = entityResult.NamedEntities.Count;
            chunk.Props["RelationshipsExtracted"] = entityResult.ExtractedRelationships.Count;
            chunk.Props["CoreferenceChains"] = entityResult.CoreferenceChains.Count;
            chunk.Props["EntityTypes"] = entityResult.NamedEntities.Select(e => e.Type).Distinct().ToList();

            // 2. Graph structure generation
            var graphOptions = new GraphGenerationOptions
            {
                EnableRDFGeneration = true,
                EnableHierarchicalStructures = true,
                EnableTemporalAnalysis = true,
                EnableSpatialAnalysis = true,
                EnableCausalAnalysis = false, // Skip for performance
                MinConfidenceThreshold = 0.6,
                MaxTriplesPerChunk = 100
            };

            var graphResult = _graphGenerator.GenerateGraphStructure(entityResult, graphOptions);

            // Store graph structure results
            chunk.Props["GraphTriples"] = graphResult.Triples.Count;
            chunk.Props["HierarchicalStructures"] = graphResult.HierarchicalStructures.Count;
            chunk.Props["TemporalRelationships"] = graphResult.TemporalRelationships.Count;
            chunk.Props["SpatialRelationships"] = graphResult.SpatialRelationships.Count;
            chunk.Props["GraphMetrics"] = graphResult.GraphMetrics;

            // 3. Ontology mapping
            var ontologyOptions = new OntologyMappingOptions
            {
                MinDomainConfidence = 0.7,
                EnableSchemaInference = true,
                ApplyPropertyMappings = true,
                ValidateConsistency = true
            };

            var ontologyResult = _ontologyMapper.MapToOntology(graphResult, ontologyOptions);

            // Store ontology mapping results
            chunk.Props["OntologyDomain"] = ontologyResult.DomainOntology.Domain;
            chunk.Props["SchemaEntityTypes"] = ontologyResult.InferredSchema.EntityTypes.Count;
            chunk.Props["MappedTriples"] = ontologyResult.MappedTriples.Count;
            chunk.Props["TypedEntities"] = ontologyResult.TypedEntities.Count;
            chunk.Props["OntologyQuality"] = ontologyResult.QualityMetrics;

            // 4. Graph quality assurance
            var qualityOptions = new GraphQualityOptions
            {
                AutoFix = false, // Skip auto-fix for performance
                MinConnectionThreshold = 2,
                ValidateSemanticConsistency = true,
                DetectCycles = true,
                IdentifyOrphans = true
            };

            var qualityResult = _graphQualityAssurance.AssessGraphQuality(ontologyResult, qualityOptions);

            // Store quality assessment results
            chunk.Props["GraphQualityGrade"] = qualityResult.QualityScores.QualityGrade;
            chunk.Props["ConsistencyScore"] = qualityResult.ConsistencyReport.ConsistencyScore;
            chunk.Props["CompletenessScore"] = qualityResult.CompletenessReport.OverallCompletenessScore;
            chunk.Props["StructuralIntegrityScore"] = qualityResult.StructuralIntegrityReport.IntegrityScore;
            chunk.Props["HasCycles"] = qualityResult.CyclicReferenceReport.HasCycles;
            chunk.Props["OrphanNodesCount"] = qualityResult.OrphanNodeReport.OrphanCount;

            // 5. Generate improvement recommendations
            if (qualityResult.ImprovementRecommendations.Any())
            {
                chunk.Props["GraphImprovementRecommendations"] = qualityResult.ImprovementRecommendations
                    .Take(3) // Top 3 recommendations
                    .Select(r => new { r.Priority, r.Category, r.Description })
                    .ToList();
            }

            // 6. Update chunk properties with graph-specific metadata
            if (entityResult.NamedEntities.Any())
            {
                // Extract key entities for search optimization
                var keyEntities = entityResult.NamedEntities
                    .Where(e => e.Confidence > 0.7)
                    .OrderByDescending(e => e.Confidence)
                    .Take(10)
                    .Select(e => e.Value)
                    .ToList();

                chunk.Props["GraphKeyEntities"] = keyEntities;

                // Add entity-based keywords to existing search keywords
                if (chunk.Props.ContainsKey("SearchKeywords"))
                {
                    var existingKeywords = (List<string>)chunk.Props["SearchKeywords"];
                    var combinedKeywords = existingKeywords.Union(keyEntities).Take(15).ToList();
                    chunk.Props["SearchKeywords"] = combinedKeywords;
                }
            }

            // 7. Enhance content type classification
            if (ontologyResult.DomainOntology.Domain != "general")
            {
                chunk.Props["GraphDomain"] = ontologyResult.DomainOntology.Domain;

                // Update document domain if graph provides more specific classification
                var currentDomain = chunk.Props.ContainsKey("Domain") ? chunk.Props["Domain"]?.ToString() : "General";
                if (currentDomain == "General" || string.IsNullOrEmpty(currentDomain))
                {
                    chunk.Props["Domain"] = ontologyResult.DomainOntology.Domain.Substring(0, 1).ToUpper() +
                                         ontologyResult.DomainOntology.Domain.Substring(1).ToLower();
                }
            }

            // 8. Calculate graph-enhanced relevance score
            var graphRelevance = CalculateGraphRelevanceScore(entityResult, graphResult, qualityResult);

            // Update overall relevance score: 70% original + 30% graph-based
            var originalRelevance = chunk.Props.ContainsKey("RelevanceScore")
                ? Convert.ToDouble(chunk.Props["RelevanceScore"])
                : 0.5;
            chunk.Props["RelevanceScore"] = (originalRelevance * 0.7) + (graphRelevance * 0.3);

            // 9. Update overall quality score with graph quality
            var originalQuality = chunk.Quality;
            var graphQuality = qualityResult.QualityScores.OverallQualityScore;

            // Weighted combination: 80% current quality + 20% graph quality
            chunk.Quality = (originalQuality * 0.8) + (graphQuality * 0.2);

            // Mark as graph-optimized
            chunk.Props["GraphOptimized"] = true;
            chunk.Props["GraphOptimizationTimestamp"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // Log error but don't fail chunk creation
            chunk.Props["GraphOptimizationError"] = ex.Message;
            chunk.Props["GraphOptimized"] = false;
        }
    }

    /// <summary>
    /// Calculate relevance score based on graph structure quality
    /// </summary>
    private static double CalculateGraphRelevanceScore(EntityExtractionResult entityResult,
        GraphStructureResult graphResult, GraphQualityResult qualityResult)
    {
        var scores = new List<double>();

        // Entity richness score (0.0-1.0)
        var entityScore = Math.Min(1.0, entityResult.NamedEntities.Count / 10.0); // 10+ entities = max score
        scores.Add(entityScore);

        // Relationship density score (0.0-1.0)
        var relationshipScore = entityResult.NamedEntities.Count > 0
            ? Math.Min(1.0, entityResult.ExtractedRelationships.Count / (double)entityResult.NamedEntities.Count)
            : 0.0;
        scores.Add(relationshipScore);

        // Graph connectivity score (0.0-1.0)
        var connectivityScore = graphResult.Triples.Count > 0
            ? Math.Min(1.0, graphResult.Triples.Count / 20.0) // 20+ triples = max score
            : 0.0;
        scores.Add(connectivityScore);

        // Quality score (0.0-1.0)
        var qualityScore = qualityResult.QualityScores.OverallQualityScore;
        scores.Add(qualityScore);

        return scores.Average();
    }

    [GeneratedRegex(@"[.!?]+\s+", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
