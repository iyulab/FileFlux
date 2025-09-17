using FileFlux.Infrastructure.Factories;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Factories;

/// <summary>
/// DocumentReaderFactory의 지원 확장자 조회 기능 테스트
/// </summary>
public class DocumentReaderFactoryExtensionTests
{
    private readonly ITestOutputHelper _output;
    private readonly DocumentReaderFactory _factory;

    public DocumentReaderFactoryExtensionTests(ITestOutputHelper output)
    {
        _output = output;
        _factory = new DocumentReaderFactory();
    }

    [Fact]
    public void GetSupportedExtensions_ShouldReturnAllRegisteredExtensions()
    {
        // Act
        var supportedExtensions = _factory.GetSupportedExtensions().ToList();

        // Assert
        Assert.NotEmpty(supportedExtensions);

        // 기본적으로 지원하는 8가지 파일 형식의 확장자들이 포함되어야 함
        var expectedExtensions = new[]
        {
            ".pdf",     // PDF
            ".docx",    // Word
            ".xlsx",    // Excel
            ".pptx",    // PowerPoint
            ".txt",     // Text
            ".md",      // Markdown
            ".markdown", // Markdown
            ".html",    // HTML
            ".htm",     // HTML
            ".tmp"      // Temporary text files
        };

        foreach (var expectedExt in expectedExtensions)
        {
            Assert.Contains(expectedExt, supportedExtensions);
        }

        _output.WriteLine($"지원하는 확장자 목록 ({supportedExtensions.Count}개):");
        foreach (var ext in supportedExtensions)
        {
            _output.WriteLine($"  {ext}");
        }
    }

    [Theory]
    [InlineData(".pdf", true)]
    [InlineData(".docx", true)]
    [InlineData(".xlsx", true)]
    [InlineData(".pptx", true)]
    [InlineData(".txt", true)]
    [InlineData(".md", true)]
    [InlineData(".html", true)]
    [InlineData(".htm", true)]
    [InlineData(".json", false)]  // JSON은 아직 지원하지 않음
    [InlineData(".csv", false)]   // CSV는 아직 지원하지 않음
    [InlineData(".xml", false)]   // XML은 지원하지 않음
    [InlineData("", false)]       // 빈 문자열
    [InlineData(null, false)]     // null
    public void IsExtensionSupported_ShouldReturnCorrectResult(string extension, bool expected)
    {
        // Act
        var result = _factory.IsExtensionSupported(extension);

        // Assert
        Assert.Equal(expected, result);

        _output.WriteLine($"확장자 '{extension}' 지원 여부: {result}");
    }

    [Theory]
    [InlineData("pdf", true)]     // 점 없이도 지원해야 함
    [InlineData("PDF", true)]     // 대소문자 구분 없이 지원해야 함
    [InlineData("DOCX", true)]
    [InlineData("TXT", true)]
    [InlineData("md", true)]
    public void IsExtensionSupported_ShouldHandleVariousFormats(string extension, bool expected)
    {
        // Act
        var result = _factory.IsExtensionSupported(extension);

        // Assert
        Assert.Equal(expected, result);

        _output.WriteLine($"확장자 '{extension}' (정규화 전) 지원 여부: {result}");
    }

    [Fact]
    public void GetExtensionReaderMapping_ShouldReturnCorrectMapping()
    {
        // Act
        var mapping = _factory.GetExtensionReaderMapping();

        // Assert
        Assert.NotEmpty(mapping);

        // 확장자별 Reader 타입 검증
        var expectedMappings = new Dictionary<string, string>
        {
            { ".pdf", "PdfReader" },
            { ".docx", "WordReader" },
            { ".xlsx", "ExcelReader" },
            { ".pptx", "PowerPointReader" },
            { ".txt", "TextReader" },
            { ".md", "MarkdownReader" },
            { ".markdown", "MarkdownReader" },
            { ".html", "HtmlReader" },
            { ".htm", "HtmlReader" },
            { ".tmp", "TextReader" }
        };

        foreach (var expectedMapping in expectedMappings)
        {
            Assert.True(mapping.ContainsKey(expectedMapping.Key),
                $"확장자 '{expectedMapping.Key}'가 매핑에 없습니다.");

            Assert.Equal(expectedMapping.Value, mapping[expectedMapping.Key]);
        }

        _output.WriteLine($"확장자-Reader 매핑 ({mapping.Count}개):");
        foreach (var kvp in mapping.OrderBy(x => x.Key))
        {
            _output.WriteLine($"  {kvp.Key} → {kvp.Value}");
        }
    }

    [Fact]
    public void GetAvailableReaders_ShouldIncludeAllDefaultReaders()
    {
        // Act
        var readers = _factory.GetAvailableReaders().ToList();

        // Assert
        Assert.NotEmpty(readers);

        var readerTypes = readers.Select(r => r.ReaderType).ToList();
        var expectedReaderTypes = new[]
        {
            "PdfReader",
            "WordReader",
            "ExcelReader",
            "PowerPointReader",
            "TextReader",
            "MarkdownReader",
            "HtmlReader"
        };

        foreach (var expectedType in expectedReaderTypes)
        {
            Assert.Contains(expectedType, readerTypes);
        }

        _output.WriteLine($"등록된 Reader 목록 ({readers.Count}개):");
        foreach (var reader in readers)
        {
            var extensions = string.Join(", ", reader.SupportedExtensions);
            _output.WriteLine($"  {reader.ReaderType}: {extensions}");
        }
    }

    [Fact]
    public void GetSupportedExtensions_ShouldReturnSortedUniqueList()
    {
        // Act
        var extensions = _factory.GetSupportedExtensions().ToList();

        // Assert
        // 정렬 확인
        var sortedExtensions = extensions.OrderBy(x => x).ToList();
        Assert.Equal(sortedExtensions, extensions);

        // 중복 제거 확인 (.md가 TextReader와 MarkdownReader 모두에 있어도 한 번만 나와야 함)
        var distinctExtensions = extensions.Distinct().ToList();
        Assert.Equal(distinctExtensions.Count, extensions.Count);

        _output.WriteLine("정렬되고 중복 제거된 확장자 목록:");
        for (int i = 0; i < extensions.Count; i++)
        {
            _output.WriteLine($"  {i + 1:D2}. {extensions[i]}");
        }
    }
}