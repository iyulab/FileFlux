using FileFlux;
using FileFlux.Core;
using FileFlux.Infrastructure.Services;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Parsers;

/// <summary>
/// 기본 문서 Parser - 텍스트 완성 서비스를 필수로 사용하는 구조화 파서
/// </summary>
public partial class BasicDocumentParser : IDocumentParser
{
    private readonly ITextCompletionService? _textCompletionService;

    public BasicDocumentParser(ITextCompletionService? textCompletionService = null)
    {
        _textCompletionService = textCompletionService;
    }

    public IEnumerable<string> SupportedDocumentTypes =>
        ["Technical", "Business", "Academic", "General", "Markdown"];

    public string ParserType => "BasicParser";
    private static readonly string[] separator = new[] { "\n\n", "\r\n\r\n" };

    public bool CanParse(RawContent rawContent)
    {
        if (rawContent == null)
            return false;

        // 빈 텍스트도 허용 (이미지만 있는 문서 등)
        // null이 아니면 처리 가능
        return rawContent.Text != null;
    }

    public async Task<ParsedContent> ParseAsync(
        RawContent rawContent,
        DocumentParsingOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!CanParse(rawContent))
            throw new ArgumentException("Cannot parse the provided raw content", nameof(rawContent));

        var startTime = DateTime.UtcNow;
        var warnings = new List<string>(rawContent.Warnings);

        try
        {
            ParsedContent result;

            if (options.UseLlmParsing && _textCompletionService != null)
            {
                // 텍스트 완성 서비스 기반 고도화 구조화
                result = await ParseWithTextCompletionAsync(rawContent, options, cancellationToken);
            }
            else
            {
                // 규칙 기반 기본 구조화 (LLM 없거나 UseLlmParsing=false)
                result = ParseWithRules(rawContent, options);
            }

            // 파싱 메타데이터 설정
            result.Info = new ParsingInfo
            {
                ParserType = ParserType,
                UsedLlm = options.UseLlmParsing && _textCompletionService != null,
                Warnings = warnings
            };
            result.Duration = DateTime.UtcNow - startTime;

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse document: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 규칙 기반 기본 구조화 (LLM 없음)
    /// </summary>
    private ParsedContent ParseWithRules(RawContent rawContent, DocumentParsingOptions options)
    {
        var text = rawContent.Text;
        var hints = rawContent.Hints;

        // 문서 유형 추론
        var documentType = InferType(rawContent);

        // 섹션 분할
        var sections = ExtractSections(text, hints);

        // 기본 메타데이터 생성
        var metadata = CreateBasicMetadata(rawContent, documentType);

        // 키워드 추출 (단순 빈도 기반)
        var keywords = ExtractKeywords(text, 10);

        // 간단한 요약 (첫 번째 문단 또는 제목)
        var summary = GenerateBasicSummary(text, sections);

        var formattedText = FormatStructuredText(sections);

        return new ParsedContent
        {
            Text = formattedText,
            Metadata = metadata,
            Structure = new DocumentStructure
            {
                Type = documentType,
                Topic = keywords.FirstOrDefault() ?? "Unknown",
                Summary = summary,
                Keywords = keywords,
                Sections = sections,
                Entities = [] // 규칙 기반에서는 엔티티 추출 제한적
            },
            Quality = CalculateQualityMetrics(text, sections, false)
        };
    }

    /// <summary>
    /// 텍스트 완성 서비스 기반 고도화 구조화
    /// </summary>
    private async Task<ParsedContent> ParseWithTextCompletionAsync(
        RawContent rawContent,
        DocumentParsingOptions options,
        CancellationToken cancellationToken)
    {

        // 기본 규칙 기반 구조화 먼저 수행
        var basicResult = ParseWithRules(rawContent, options);

        // 텍스트 완성 서비스를 통한 구조화 개선
        var enhancedStructure = await EnhanceWithTextCompletionAsync(rawContent.Text, basicResult.Structure, options, cancellationToken);

        // 품질 재계산
        var enhancedQuality = CalculateQualityMetrics(rawContent.Text, enhancedStructure.Sections, true);

        return new ParsedContent
        {
            Text = FormatStructuredText(enhancedStructure.Sections),
            Metadata = basicResult.Metadata,
            Structure = enhancedStructure,
            Quality = enhancedQuality
        };
    }

    private static string InferType(RawContent rawContent)
    {
        var text = rawContent.Text.ToLowerInvariant();
        var extension = rawContent.File.Extension.ToLowerInvariant();

        // 확장자 기반 추론
        if (extension == ".md") return "Technical";

        // 내용 기반 추론 (키워드 매칭)
        if (text.Contains("abstract") || text.Contains("논문") || text.Contains("연구"))
            return "Academic";

        if (text.Contains("contract") || text.Contains("agreement") || text.Contains("계약"))
            return "Business";

        return "General";
    }

    private List<Section> ExtractSections(string text, Dictionary<string, object> hints)
    {
        List<Section> flatSections;

        // 마크다운 헤더 기반 섹션 분할
        if (hints.TryGetValue("has_headers", out object? value) && (bool)value)
        {
            flatSections = ExtractMarkdownSections(text);
            // Build hierarchical structure for header-based sections
            return BuildHierarchy(flatSections);
        }
        else
        {
            // 단락 기반 섹션 분할 (flat structure)
            return ExtractParagraphSections(text);
        }
    }

    /// <summary>
    /// Converts a flat list of sections into a hierarchical structure based on Level.
    /// Sections with lower Level values become parents of sections with higher Level values.
    /// </summary>
    /// <param name="flatSections">Flat list of sections with Level information</param>
    /// <returns>Hierarchical list of top-level sections with Children populated</returns>
    private static List<Section> BuildHierarchy(List<Section> flatSections)
    {
        if (flatSections.Count == 0)
            return flatSections;

        var rootSections = new List<Section>();
        var parentStack = new Stack<Section>();

        foreach (var section in flatSections)
        {
            // Clear out any parent sections that are at the same or deeper level
            while (parentStack.Count > 0 && parentStack.Peek().Level >= section.Level)
            {
                parentStack.Pop();
            }

            if (parentStack.Count == 0)
            {
                // No parent - this is a root section
                rootSections.Add(section);
            }
            else
            {
                // Add as child of the current parent
                var parent = parentStack.Peek();
                parent.Children.Add(section);
            }

            // Push current section as potential parent for subsequent sections
            parentStack.Push(section);
        }

        return rootSections;
    }

    private static List<Section> ExtractMarkdownSections(string text)
    {
        var sections = new List<Section>();
        var lines = text.Split('\n');
        var currentSection = new Section();
        var sectionId = 0;
        var currentPosition = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineStart = currentPosition;
            var lineEnd = currentPosition + line.Length;

            // 헤더 감지
            if (line.TrimStart().StartsWith('#'))
            {
                // 이전 섹션의 End 설정 및 저장
                if (!string.IsNullOrEmpty(currentSection.Content))
                {
                    currentSection.End = lineStart > 0 ? lineStart - 1 : 0;
                    sections.Add(currentSection);
                }

                // 새 섹션 시작
                var headerMatch = MyRegex().Match(line);
                if (headerMatch.Success)
                {
                    currentSection = new Section
                    {
                        Id = $"section_{++sectionId}",
                        Title = headerMatch.Groups[3].Value.Trim(),
                        Type = "Header",
                        Level = headerMatch.Groups[2].Value.Length,
                        Start = lineStart,
                        Content = line
                    };
                }
            }
            else
            {
                // 내용 추가
                currentSection.Content += (string.IsNullOrEmpty(currentSection.Content) ? "" : "\n") + line;
            }

            // 다음 줄 위치 계산 (+1 for newline character)
            currentPosition = lineEnd + 1;
        }

        // 마지막 섹션의 End 설정 및 추가
        if (!string.IsNullOrEmpty(currentSection.Content))
        {
            currentSection.End = text.Length - 1;
            sections.Add(currentSection);
        }

        return sections;
    }

    private static List<Section> ExtractParagraphSections(string text)
    {
        var sections = new List<Section>();
        var paragraphs = text.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var searchStart = 0;

        for (int i = 0; i < paragraphs.Length; i++)
        {
            var paragraph = paragraphs[i].Trim();
            if (string.IsNullOrEmpty(paragraph)) continue;

            var start = text.IndexOf(paragraph, searchStart, StringComparison.Ordinal);
            if (start < 0) start = text.IndexOf(paragraph, StringComparison.Ordinal);

            var end = start + paragraph.Length - 1;

            sections.Add(new Section
            {
                Id = $"paragraph_{i + 1}",
                Title = $"Paragraph {i + 1}",
                Type = "Paragraph",
                Content = paragraph,
                Level = 1,
                Start = start,
                End = end
            });

            searchStart = end + 1;
        }

        return sections;
    }

    private static DocumentMetadata CreateBasicMetadata(RawContent rawContent, string documentType)
    {
        // Get word count from hints or calculate
        var wordCount = 0;
        if (rawContent.Hints.TryGetValue("WordCount", out var wc) && wc is int count)
        {
            wordCount = count;
        }
        else
        {
            // Fallback: calculate from text
            wordCount = string.IsNullOrWhiteSpace(rawContent.Text) ? 0
                : rawContent.Text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        }

        // Get page count from hints (supports various key names from different readers)
        var pageCount = 1;
        if (rawContent.Hints.TryGetValue("page_count", out var pc) && pc is int pages)
        {
            pageCount = pages;
        }
        else if (rawContent.Hints.TryGetValue("PageCount", out var pc2) && pc2 is int pages2)
        {
            pageCount = pages2;
        }
        else if (rawContent.Hints.TryGetValue("slide_count", out var sc) && sc is int slides)
        {
            pageCount = slides;
        }

        // Get document title from hints (set by document readers like Word, PDF, Excel, PowerPoint)
        // Support multiple key names: document_title (DOCX/PPTX/XLSX), Title (PDF)
        string? title = null;
        if (rawContent.Hints.TryGetValue("document_title", out var dt) && dt is string docTitle && !string.IsNullOrWhiteSpace(docTitle))
        {
            title = docTitle;
        }
        else if (rawContent.Hints.TryGetValue("Title", out var pdfTitle) && pdfTitle is string pdfTitleStr && !string.IsNullOrWhiteSpace(pdfTitleStr))
        {
            title = pdfTitleStr;
        }

        // Get author from hints (set by document readers)
        // Support multiple key names: author (DOCX), Author (PDF), Creator (PDF alternative)
        string? author = null;
        if (rawContent.Hints.TryGetValue("author", out var auth) && auth is string docAuthor && !string.IsNullOrWhiteSpace(docAuthor))
        {
            author = docAuthor;
        }
        else if (rawContent.Hints.TryGetValue("Author", out var pdfAuth) && pdfAuth is string pdfAuthor && !string.IsNullOrWhiteSpace(pdfAuthor))
        {
            author = pdfAuthor;
        }
        else if (rawContent.Hints.TryGetValue("Creator", out var creator) && creator is string creatorStr && !string.IsNullOrWhiteSpace(creatorStr))
        {
            author = creatorStr;
        }

        // 언어 감지 (NTextCat 기반 - 언어 코드 + 신뢰도)
        var (language, languageConfidence) = LanguageDetector.Detect(rawContent.Text);

        return new DocumentMetadata
        {
            FileName = rawContent.File.Name,
            FileType = documentType,
            FileSize = rawContent.File.Size,
            Title = title,
            Author = author,
            CreatedAt = rawContent.File.CreatedAt,
            ModifiedAt = rawContent.File.ModifiedAt,
            ProcessedAt = DateTime.UtcNow,
            Language = language,
            LanguageConfidence = languageConfidence,
            WordCount = wordCount,
            PageCount = pageCount
        };
    }

    /// <summary>
    /// 텍스트에서 기술 키워드 자동 추출
    /// </summary>
    private static List<string> ExtractTechnicalKeywords(string text)
    {
        var keywords = new List<string>();
        var content = text.ToLowerInvariant();

        // 기술 스택 키워드
        var techKeywords = new Dictionary<string, string[]>
        {
            [".NET"] = new[] { ".net", "dotnet", "asp.net", "entity framework" },
            ["React"] = new[] { "react", "jsx", "tsx", "react hook" },
            ["Vue"] = new[] { "vue", "vuejs", "vue.js" },
            ["Angular"] = new[] { "angular", "typescript" },
            ["Python"] = new[] { "python", "django", "flask", "fastapi" },
            ["Java"] = new[] { "java", "spring", "maven", "gradle" },
            ["Docker"] = new[] { "docker", "container", "dockerfile" },
            ["Kubernetes"] = new[] { "kubernetes", "k8s", "kubectl" },
            ["AI/ML"] = new[] { "ai", "ml", "machine learning", "llm", "gpt", "embedding" },
            ["Database"] = new[] { "postgresql", "mysql", "mongodb", "redis", "sqlite" },
            ["Cloud"] = new[] { "aws", "azure", "gcp", "cloud" }
        };

        foreach (var (category, patterns) in techKeywords)
        {
            if (patterns.Any(pattern => content.Contains(pattern)))
            {
                keywords.Add(category);
            }
        }

        return keywords;
    }

    /// <summary>
    /// 문서 도메인 자동 탐지 (Technical, Business, Academic, General)
    /// </summary>
    private static string DetectDocumentDomain(string text, List<string> technicalKeywords)
    {
        var content = text.ToLowerInvariant();

        // Technical 문서 판별
        if (technicalKeywords.Count >= 2 ||
            content.Contains("api") || content.Contains("code") || content.Contains("function") ||
            content.Contains("requirements") || content.Contains("architecture"))
        {
            return "Technical";
        }

        // Business 문서 판별
        if (content.Contains("business") || content.Contains("requirement") || content.Contains("project") ||
            content.Contains("strategy") || content.Contains("plan"))
        {
            return "Business";
        }

        // Academic 문서 판별
        if (content.Contains("research") || content.Contains("abstract") || content.Contains("논문") ||
            content.Contains("study") || content.Contains("analysis"))
        {
            return "Academic";
        }

        return "General";
    }

    private static List<string> ExtractKeywords(string text, int maxKeywords)
    {
        // 단순 빈도 기반 키워드 추출
        var words = text.ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3) // 3글자 이상
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .Take(maxKeywords)
            .Select(g => g.Key)
            .ToList();

        return words;
    }

    private static string GenerateBasicSummary(string text, List<Section> sections)
    {
        // 첫 번째 문단이나 제목 기반 요약
        if (sections.Count != 0)
        {
            var firstSection = sections.First();
            var summary = firstSection.Content.Length > 200
                ? string.Concat(firstSection.Content.AsSpan(0, 200), "...")
                : firstSection.Content;
            return summary.Replace('\n', ' ').Trim();
        }

        // 전체 텍스트의 첫 200자
        return text.Length > 200
            ? text.Substring(0, 200).Replace('\n', ' ').Trim() + "..."
            : text.Replace('\n', ' ').Trim();
    }

    private static string FormatStructuredText(List<Section> sections)
    {
        var sb = new System.Text.StringBuilder();
        FormatSectionsRecursively(sections, sb);
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Recursively formats sections including all children in hierarchical order.
    /// </summary>
    private static void FormatSectionsRecursively(List<Section> sections, System.Text.StringBuilder sb)
    {
        foreach (var s in sections)
        {
            if (sb.Length > 0)
                sb.Append("\n\n");

            // Content that already has markdown headers - use as-is
            var contentLines = s.Content.Split('\n');
            var firstLine = contentLines.FirstOrDefault()?.TrimStart() ?? "";

            if (firstLine.StartsWith('#'))
            {
                sb.Append(s.Content);
            }
            else if (s.Type == "Paragraph" && s.Title.StartsWith("Paragraph ", StringComparison.OrdinalIgnoreCase))
            {
                // For paragraph sections without real titles, just return content without adding artificial headers
                sb.Append(s.Content);
            }
            else
            {
                // Only add headers for sections with meaningful titles
                sb.Append($"{new string('#', s.Level)} {s.Title}\n{s.Content}");
            }

            // Recursively process children
            if (s.Children.Count > 0)
            {
                FormatSectionsRecursively(s.Children, sb);
            }
        }
    }

    private static QualityMetrics CalculateQualityMetrics(string originalText, List<Section> sections, bool usedLlm)
    {
        var structureConfidence = sections.Count != 0 ? 0.8 : 0.3;
        var completenessScore = Math.Min(1.0, sections.Sum(s => s.Content.Length) / (double)originalText.Length);
        var metadataAccuracy = usedLlm ? 0.9 : 0.6;

        var overallScore = (structureConfidence + completenessScore + metadataAccuracy) / 3.0;

        return new QualityMetrics
        {
            ConfidenceScore = structureConfidence,
            CompletenessScore = completenessScore,
            ConsistencyScore = metadataAccuracy,
            Details = new Dictionary<string, object>
            {
                ["section_count"] = sections.Count,
                ["avg_section_length"] = sections.Count != 0 ? sections.Average(s => s.Content.Length) : 0,
                ["used_llm"] = usedLlm
            }
        };
    }

    private async Task<DocumentStructure> EnhanceWithTextCompletionAsync(
        string text,
        DocumentStructure basicStructure,
        DocumentParsingOptions options,
        CancellationToken cancellationToken)
    {
        // 구조화 프롬프트 구성
        var prompt = BuildStructuringPrompt(text, basicStructure, options);

        try
        {
            if (_textCompletionService == null)
            {
                return basicStructure;
            }

            // 텍스트 완성 서비스 호출
            var response = await _textCompletionService.GenerateAsync(prompt, cancellationToken);

            // 응답을 구조화된 데이터로 파싱
            return ParseTextCompletionResponse(response, basicStructure);
        }
        catch (Exception)
        {
            // 텍스트 완성 서비스 실패 시 기본 구조 반환
            return basicStructure;
        }
    }

    private static string BuildStructuringPrompt(string text, DocumentStructure basicStructure, DocumentParsingOptions options)
    {
        return $"""
        문서 구조화 작업:
        
        원본 문서:
        {text}
        
        기본 분석 결과:
        - 문서 유형: {basicStructure.Type}
        - 섹션 수: {basicStructure.Sections.Count}
        
        요청사항:
        1. 문서의 주요 주제와 핵심 키워드 5개 추출
        2. 문서 요약 (2-3 문장)
        3. 개선된 섹션 구조 제안
        
        응답 형식:
        TOPIC: [주제]
        KEYWORDS: [키워드1, 키워드2, ...]
        SUMMARY: [요약]
        SECTIONS: [개선된 섹션 구조]
        """;
    }

    private static DocumentStructure ParseTextCompletionResponse(string response, DocumentStructure fallback)
    {
        try
        {
            var enhanced = new DocumentStructure
            {
                Type = fallback.Type,
                Sections = fallback.Sections
            };

            // 간단한 파싱 (실제로는 더 정교한 파싱 필요)
            if (response.Contains("TOPIC:"))
            {
                var topicMatch = Regex.Match(response, @"TOPIC:\s*(.+)");
                if (topicMatch.Success)
                    enhanced.Topic = topicMatch.Groups[1].Value.Trim();
            }

            if (response.Contains("KEYWORDS:"))
            {
                var keywordsMatch = Regex.Match(response, @"KEYWORDS:\s*(.+)");
                if (keywordsMatch.Success)
                {
                    enhanced.Keywords = keywordsMatch.Groups[1].Value
                        .Split(',')
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrEmpty(k))
                        .ToList();
                }
            }

            if (response.Contains("SUMMARY:"))
            {
                var summaryMatch = Regex.Match(response, @"SUMMARY:\s*(.+)");
                if (summaryMatch.Success)
                    enhanced.Summary = summaryMatch.Groups[1].Value.Trim();
            }

            return enhanced;
        }
        catch
        {
            return fallback;
        }
    }

    [GeneratedRegex(@"^(\s*)(#+)\s*(.*)")]
    private static partial Regex MyRegex();
}
