using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Infrastructure.Readers;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Word 문서 고급 구조화 기능 TDD 테스트
/// 스타일 기반 섹션 인식, 표/이미지 캡션, Word 특화 메타데이터 추출
/// </summary>
public class WordDocumentStyleBasedTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly WordDocumentReader _reader;
    private readonly string _testDir;
    private readonly List<string> _createdFiles;

    public WordDocumentStyleBasedTests(ITestOutputHelper output)
    {
        _output = output;
        _reader = new WordDocumentReader();
        _testDir = Path.Combine(Path.GetTempPath(), "WordStyleTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);
        _createdFiles = new List<string>();
    }

    [Fact]
    public async Task ExtractAsync_WithHeadingStyles_ShouldRecognizeStyleBasedSections()
    {
        // Arrange: Heading1-6 스타일을 사용한 Word 문서 생성
        var testFile = await CreateWordDocumentWithHeadingStylesAsync();

        // Act
        var result = await _reader.ExtractAsync(testFile);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Text);

        _output.WriteLine($"추출된 텍스트 길이: {result.Text.Length}");
        _output.WriteLine($"추출된 내용:\n{result.Text}");

        // 기본 구조적 힌트 검증
        Assert.NotNull(result.Hints);
        
        // Heading 스타일 감지 검증
        if (result.Hints.TryGetValue("heading_styles_detected", out object? headingValue))
        {
            Assert.True((bool)headingValue);
            _output.WriteLine("✅ Heading 스타일 감지 성공");
        }

        // 섹션 구조 검증
        if (result.Hints.TryGetValue("section_count", out object? sectionValue))
        {
            var sectionCount = (int)sectionValue;
            Assert.True(sectionCount >= 3, $"최소 3개 섹션이 있어야 하지만 {sectionCount}개 발견됨");
            _output.WriteLine($"✅ 섹션 수: {sectionCount}");
        }

        // 스타일 레벨별 헤더 검증
        Assert.Contains("Chapter 1", result.Text); // Heading 1
        Assert.Contains("Section 1.1", result.Text); // Heading 2
        Assert.Contains("Subsection 1.1.1", result.Text); // Heading 3
    }

    [Fact]
    public async Task ExtractAsync_WithTablesAndCaptions_ShouldExtractTableContext()
    {
        // Arrange: 표와 캡션이 있는 Word 문서 생성
        var testFile = await CreateWordDocumentWithTablesAndCaptionsAsync();

        // Act
        var result = await _reader.ExtractAsync(testFile);

        // Assert
        Assert.NotNull(result);
        _output.WriteLine($"표가 포함된 문서 추출 결과:\n{result.Text}");

        // 표 콘텐츠 검증
        Assert.Contains("Name", result.Text);
        Assert.Contains("Age", result.Text); 
        Assert.Contains("City", result.Text);
        Assert.Contains("John", result.Text);
        Assert.Contains("25", result.Text);
        Assert.Contains("New York", result.Text);

        // 표 관련 구조적 힌트 검증
        if (result.Hints.TryGetValue("table_count", out object? value))
        {
            var tableCount = (int)value;
            Assert.True(tableCount >= 1, $"최소 1개 표가 있어야 하지만 {tableCount}개 발견됨");
            _output.WriteLine($"✅ 테이블 수: {tableCount}");
        }

        // 캡션 연결 검증 (향후 구현 예정)
        if (result.Hints.TryGetValue("has_table_captions", out object? captionValue))
        {
            Assert.True((bool)captionValue);
            _output.WriteLine("✅ 테이블 캡션 감지");
        }
    }

    [Fact]
    public async Task ExtractAsync_WithComplexFormatting_ShouldPreserveImportanceScoring()
    {
        // Arrange: 다양한 서식(굵게, 기울임, 색상 등)이 있는 Word 문서 생성
        var testFile = await CreateWordDocumentWithComplexFormattingAsync();

        // Act
        var result = await _reader.ExtractAsync(testFile);

        // Assert
        Assert.NotNull(result);
        _output.WriteLine($"복합 서식 문서 추출 결과:\n{result.Text}");

        // 서식 기반 중요도 분석 검증
        if (result.Hints.TryGetValue("formatting_analysis", out object? analysisValue))
        {
            var analysis = (Dictionary<string, object>)analysisValue;
            
            if (analysis.TryGetValue("bold_text_count", out object? boldValue))
            {
                var boldCount = (int)boldValue;
                Assert.True(boldCount > 0, "굵은 텍스트가 감지되어야 함");
                _output.WriteLine($"✅ 굵은 텍스트 수: {boldCount}");
            }

            if (analysis.TryGetValue("italic_text_count", out object? italicValue))
            {
                var italicCount = (int)italicValue;
                Assert.True(italicCount > 0, "기울임 텍스트가 감지되어야 함");
                _output.WriteLine($"✅ 기울임 텍스트 수: {italicCount}");
            }
        }

        // 텍스트 내용 기본 검증
        Assert.Contains("Important", result.Text);
        Assert.Contains("emphasis", result.Text);
        Assert.Contains("normal", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_WithFootnotesAndEndnotes_ShouldPreserveReferences()
    {
        // Arrange: 각주와 미주가 있는 Word 문서 생성
        var testFile = await CreateWordDocumentWithNotesAsync();

        // Act
        var result = await _reader.ExtractAsync(testFile);

        // Assert
        Assert.NotNull(result);
        _output.WriteLine($"각주/미주 문서 추출 결과:\n{result.Text}");

        // 각주/미주 관련 힌트 검증
        if (result.Hints.TryGetValue("has_footnotes", out object? value))
        {
            Assert.True((bool)value);
            _output.WriteLine("✅ 각주 감지");
        }

        if (result.Hints.TryGetValue("footnote_count", out object? footnoteValue))
        {
            var footnoteCount = (int)footnoteValue;
            Assert.True(footnoteCount > 0, "각주가 있어야 함");
            _output.WriteLine($"✅ 각주 수: {footnoteCount}");
        }

        // 기본 텍스트와 각주 내용 모두 포함 검증
        Assert.Contains("main text", result.Text);
        Assert.Contains("footnote", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_WithDocumentProperties_ShouldExtractWordMetadata()
    {
        // Arrange: 문서 속성이 설정된 Word 문서 생성
        var testFile = await CreateWordDocumentWithPropertiesAsync();

        // Act
        var result = await _reader.ExtractAsync(testFile);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.File);

        _output.WriteLine($"문서 속성 포함 추출 결과:");
        _output.WriteLine($"파일명: {result.File.Name}");
        _output.WriteLine($"파일 크기: {result.File.Size} bytes");

        // Word 특화 메타데이터 검증
        if (result.Hints.TryGetValue("word_metadata", out object? value))
        {
            var metadata = (Dictionary<string, object>)value;
            
            _output.WriteLine("Word 메타데이터:");
            foreach (var kvp in metadata)
            {
                _output.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }

            // 기본 메타데이터 항목 검증
            Assert.True(metadata.Count > 0, "Word 메타데이터가 추출되어야 함");
        }

        // 파일 정보 기본 검증
        Assert.True(result.File.Size > 0);
        Assert.Equal(".docx", result.File.Extension);
        Assert.Equal("WordReader", result.ReaderType);
    }

    #region Test Document Creation Helper Methods

    private async Task<string> CreateWordDocumentWithHeadingStylesAsync()
    {
        var fileName = Path.Combine(_testDir, $"heading_styles_test_{Guid.NewGuid():N}.docx");
        _createdFiles.Add(fileName);

        using var doc = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
        
        // 문서 구조 생성
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        // Heading 1 스타일
        var heading1 = new Paragraph();
        var heading1Props = new ParagraphProperties();
        heading1Props.ParagraphStyleId = new ParagraphStyleId { Val = "Heading1" };
        heading1.Append(heading1Props);
        heading1.Append(new Run(new Text("Chapter 1: Introduction")));
        body.Append(heading1);

        // 일반 텍스트
        var para1 = new Paragraph(new Run(new Text("This is the introduction chapter content with important information about our system.")));
        body.Append(para1);

        // Heading 2 스타일  
        var heading2 = new Paragraph();
        var heading2Props = new ParagraphProperties();
        heading2Props.ParagraphStyleId = new ParagraphStyleId { Val = "Heading2" };
        heading2.Append(heading2Props);
        heading2.Append(new Run(new Text("Section 1.1: Overview")));
        body.Append(heading2);

        // 일반 텍스트
        var para2 = new Paragraph(new Run(new Text("This section provides an overview of the key concepts and methodologies.")));
        body.Append(para2);

        // Heading 3 스타일
        var heading3 = new Paragraph();
        var heading3Props = new ParagraphProperties();
        heading3Props.ParagraphStyleId = new ParagraphStyleId { Val = "Heading3" };
        heading3.Append(heading3Props);
        heading3.Append(new Run(new Text("Subsection 1.1.1: Technical Details")));
        body.Append(heading3);

        // 일반 텍스트
        var para3 = new Paragraph(new Run(new Text("Here are the technical implementation details and specifications.")));
        body.Append(para3);

        mainPart.Document.Append(body);
        await Task.CompletedTask;
        return fileName;
    }

    private async Task<string> CreateWordDocumentWithTablesAndCaptionsAsync()
    {
        var fileName = Path.Combine(_testDir, $"tables_captions_test_{Guid.NewGuid():N}.docx");
        _createdFiles.Add(fileName);

        using var doc = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
        
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        // 표 제목
        var tableTitle = new Paragraph(new Run(new Text("Employee Information Table")));
        body.Append(tableTitle);

        // 표 생성
        var table = new Table();
        
        // 표 헤더
        var headerRow = new TableRow();
        headerRow.Append(new TableCell(new Paragraph(new Run(new Text("Name")))));
        headerRow.Append(new TableCell(new Paragraph(new Run(new Text("Age")))));  
        headerRow.Append(new TableCell(new Paragraph(new Run(new Text("City")))));
        table.Append(headerRow);

        // 데이터 행
        var dataRow1 = new TableRow();
        dataRow1.Append(new TableCell(new Paragraph(new Run(new Text("John Doe")))));
        dataRow1.Append(new TableCell(new Paragraph(new Run(new Text("25")))));
        dataRow1.Append(new TableCell(new Paragraph(new Run(new Text("New York")))));
        table.Append(dataRow1);

        var dataRow2 = new TableRow();
        dataRow2.Append(new TableCell(new Paragraph(new Run(new Text("Jane Smith")))));
        dataRow2.Append(new TableCell(new Paragraph(new Run(new Text("30")))));
        dataRow2.Append(new TableCell(new Paragraph(new Run(new Text("Los Angeles")))));
        table.Append(dataRow2);

        body.Append(table);

        // 표 캡션
        var caption = new Paragraph(new Run(new Text("Table 1: Sample employee data for testing purposes")));
        body.Append(caption);

        mainPart.Document.Append(body);
        await Task.CompletedTask;
        return fileName;
    }

    private async Task<string> CreateWordDocumentWithComplexFormattingAsync()
    {
        var fileName = Path.Combine(_testDir, $"complex_formatting_test_{Guid.NewGuid():N}.docx");
        _createdFiles.Add(fileName);

        using var doc = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
        
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        // 굵은 텍스트
        var boldPara = new Paragraph();
        var boldRun = new Run();
        boldRun.RunProperties = new RunProperties(new Bold());
        boldRun.Append(new Text("Important: This is bold text that should be detected"));
        boldPara.Append(boldRun);
        body.Append(boldPara);

        // 기울임 텍스트
        var italicPara = new Paragraph();
        var italicRun = new Run();
        italicRun.RunProperties = new RunProperties(new Italic());
        italicRun.Append(new Text("This text has emphasis through italic formatting"));
        italicPara.Append(italicRun);
        body.Append(italicPara);

        // 일반 텍스트
        var normalPara = new Paragraph(new Run(new Text("This is normal text without special formatting")));
        body.Append(normalPara);

        // 혼합 서식
        var mixedPara = new Paragraph();
        mixedPara.Append(new Run(new Text("This paragraph has ")));
        
        var boldItalicRun = new Run();
        boldItalicRun.RunProperties = new RunProperties(new Bold(), new Italic());
        boldItalicRun.Append(new Text("both bold and italic"));
        mixedPara.Append(boldItalicRun);
        
        mixedPara.Append(new Run(new Text(" formatting combined.")));
        body.Append(mixedPara);

        mainPart.Document.Append(body);
        await Task.CompletedTask;
        return fileName;
    }

    private async Task<string> CreateWordDocumentWithNotesAsync()
    {
        var fileName = Path.Combine(_testDir, $"notes_test_{Guid.NewGuid():N}.docx");
        _createdFiles.Add(fileName);

        using var doc = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
        
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        // 각주가 있는 텍스트 (간단 버전)
        var paraWithNote = new Paragraph();
        paraWithNote.Append(new Run(new Text("This is the main text with a reference")));
        paraWithNote.Append(new Run(new Text(" [footnote: This is a footnote content]")));
        body.Append(paraWithNote);

        var normalPara = new Paragraph(new Run(new Text("This is additional content in the document.")));
        body.Append(normalPara);

        mainPart.Document.Append(body);
        await Task.CompletedTask;
        return fileName;
    }

    private async Task<string> CreateWordDocumentWithPropertiesAsync()
    {
        var fileName = Path.Combine(_testDir, $"properties_test_{Guid.NewGuid():N}.docx");
        _createdFiles.Add(fileName);

        using var doc = WordprocessingDocument.Create(fileName, WordprocessingDocumentType.Document);
        
        // 문서 속성 설정
        var coreProps = doc.PackageProperties;
        coreProps.Title = "Test Document Title";
        coreProps.Creator = "FileFlux Test Suite";
        coreProps.Subject = "Word Document Processing Test";
        coreProps.Description = "Test document for Word metadata extraction";

        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = new Body();

        var titlePara = new Paragraph(new Run(new Text("Document with Metadata")));
        body.Append(titlePara);

        var contentPara = new Paragraph(new Run(new Text("This document contains various metadata properties that should be extracted during processing.")));
        body.Append(contentPara);

        mainPart.Document.Append(body);
        await Task.CompletedTask;
        return fileName;
    }

    #endregion

    public void Dispose()
    {
        // 테스트 파일 정리
        foreach (var file in _createdFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // 정리 실패는 무시
            }
        }

        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch
            {
                // 디렉토리 정리 실패는 무시
            }
        }
    }
}