using FileFlux;
using FileFlux.Domain;
using FileFlux.Infrastructure.Parsers;
using System.Collections.Concurrent;

namespace FileFlux.Infrastructure.Factories;

/// <summary>
/// Document Parser 팩토리 구현체
/// 문서 유형에 따라 적절한 Parser를 제공
/// </summary>
public class DocumentParserFactory : IDocumentParserFactory
{
    private readonly ConcurrentDictionary<string, IDocumentParser> _parsers = new();
    private readonly IDocumentParser _defaultParser;

    public DocumentParserFactory(ITextCompletionService? textCompletionService = null)
    {
        _defaultParser = new BasicDocumentParser(textCompletionService);
        RegisterDefaultParsers(textCompletionService);
    }

    public IEnumerable<IDocumentParser> GetAvailableParsers()
    {
        return _parsers.Values.ToList();
    }

    public IDocumentParser GetParser(RawContent rawContent)
    {
        if (rawContent == null)
            return _defaultParser;

        // 파일 확장자 기반 Parser 선택
        var extension = rawContent.File.Extension.ToLowerInvariant();
        var documentType = InferDocumentType(rawContent, extension);

        return GetParser(documentType);
    }

    public IDocumentParser GetParser(string documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return _defaultParser;

        // 문서 유형별 특화된 Parser가 있다면 사용
        if (_parsers.TryGetValue(documentType.ToLowerInvariant(), out var parser))
            return parser;

        // 없으면 기본 Parser 사용
        return _defaultParser;
    }

    public void RegisterParser(IDocumentParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);

        _parsers.AddOrUpdate(parser.ParserType.ToLowerInvariant(), parser, (key, existingParser) => parser);
    }

    public bool UnregisterParser(string parserType)
    {
        if (string.IsNullOrWhiteSpace(parserType))
            return false;

        return _parsers.TryRemove(parserType.ToLowerInvariant(), out _);
    }

    private void RegisterDefaultParsers(ITextCompletionService? textCompletionService)
    {
        // 기본 Parser 등록
        RegisterParser(new BasicDocumentParser(textCompletionService));
    }

    private static string InferDocumentType(RawContent rawContent, string extension)
    {
        var text = rawContent.Text.ToLowerInvariant();
        var hints = rawContent.Hints;

        // 확장자 기반 추론
        var typeFromExtension = extension switch
        {
            ".md" => "Technical",
            ".pdf" => "Business",
            ".docx" => "Business",
            ".txt" => "General",
            ".json" => "Technical",
            ".csv" => "Business",
            ".html" => "Technical",
            _ => "General"
        };

        // 내용 기반 세밀 조정
        if (ContainsAcademicKeywords(text))
            return "Academic";

        if (ContainsBusinessKeywords(text))
            return "Business";

        if (ContainsTechnicalKeywords(text))
            return "Technical";

        return typeFromExtension;
    }

    private static bool ContainsAcademicKeywords(string text)
    {
        var academicKeywords = new[]
        {
            "abstract", "논문", "연구", "hypothesis", "methodology", "conclusion",
            "research", "study", "analysis", "experiment", "결론", "가설", "실험"
        };

        return academicKeywords.Any(keyword => text.Contains(keyword));
    }

    private static bool ContainsBusinessKeywords(string text)
    {
        var businessKeywords = new[]
        {
            "contract", "agreement", "invoice", "proposal", "budget", "revenue",
            "계약", "제안서", "예산", "매출", "계획", "전략", "보고서", "계획서"
        };

        return businessKeywords.Any(keyword => text.Contains(keyword));
    }

    private static bool ContainsTechnicalKeywords(string text)
    {
        var technicalKeywords = new[]
        {
            "function", "class", "method", "algorithm", "implementation", "code",
            "api", "database", "server", "클래스", "함수", "알고리즘", "구현", "데이터베이스"
        };

        return technicalKeywords.Any(keyword => text.Contains(keyword));
    }
}
