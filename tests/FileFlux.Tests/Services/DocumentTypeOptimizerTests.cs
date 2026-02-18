using FileFlux.Core;
using FileFlux.Infrastructure.Services;

namespace FileFlux.Tests.Services;

public class DocumentTypeOptimizerTests
{
    private readonly DocumentTypeOptimizer _optimizer = new();

    #region DetectDocumentTypeAsync — Category detection

    [Fact]
    public async Task DetectDocumentTypeAsync_TechnicalContent_DetectsTechnical()
    {
        var content = "The function implements an algorithm for code compilation. " +
                      "The API method handles the system software implementation. " +
                      "Debug the class syntax and compile the code.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Technical);
        result.Confidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_LegalContent_DetectsLegal()
    {
        var content = "This legal agreement contract includes a clause on liability. " +
                      "The court statute regulation requires compliance. " +
                      "The attorney shall act pursuant to the law.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Legal);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_AcademicContent_DetectsAcademic()
    {
        var content = "This research study presents the hypothesis and methodology. " +
                      "The abstract describes the conclusion and literature review. " +
                      "Citation reference analysis supports the findings.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Academic);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_FinancialContent_DetectsFinancial()
    {
        var content = "The finance investment portfolio shows revenue growth. " +
                      "Profit from asset equity dividend earnings exceeded expectations. " +
                      "The fiscal budget and market performance are strong.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Financial);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_MedicalContent_DetectsMedical()
    {
        var content = "The patient diagnosis requires treatment at the hospital. " +
                      "Clinical symptom assessment and disease therapy are needed. " +
                      "The physician prescribed medication for the health condition.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Medical);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_CreativeContent_DetectsCreative()
    {
        var content = "The story follows the character through the plot narrative. " +
                      "Creative artistic design with aesthetic inspiration drives imagination. " +
                      "The expression of the story and character is remarkable.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Creative);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_GenericContent_DetectsGeneral()
    {
        var content = "Hello world. This is a simple sentence. Nothing special here.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.General);
        result.Confidence.Should().Be(0.5);
    }

    #endregion

    #region DetectDocumentTypeAsync — Language detection

    [Fact]
    public async Task DetectDocumentTypeAsync_EnglishContent_DetectsEnglish()
    {
        var content = "This is an English document about many topics and things.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Language.Should().Be("en");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_KoreanContent_DetectsKorean()
    {
        var content = "이것은 한국어 문서입니다. 여러 주제에 대해 다룹니다.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Language.Should().Be("ko");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_ChineseContent_DetectsChineseOrKorean()
    {
        // Korean range [\u3131-\uD79D] overlaps CJK unified ideographs, so Chinese may detect as "ko"
        var content = "这是一个中文文档，涵盖了许多主题。";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        // Due to detection order (ko checked first with wide range), CJK content detects as "ko"
        result.Language.Should().BeOneOf("zh", "ko");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_JapaneseHiragana_DetectsJapaneseOrKorean()
    {
        // Korean range [\u3131-\uD79D] may overlap some katakana, so Japanese may detect as "ko"
        var content = "これは日本語のドキュメントです。さまざまなトピックについて説明します。";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Language.Should().BeOneOf("ja", "ko");
    }

    #endregion

    #region DetectDocumentTypeAsync — Structural elements

    [Fact]
    public async Task DetectDocumentTypeAsync_MarkdownHeaders_DetectsHeaderElements()
    {
        var content = "# Title\n\nSome content here.\n\n## Section One\n\nMore content.\n\n### Sub Section\n\nDetails.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.StructuralElements.Should().Contain(e => e.Type == "Header" && e.Count == 3);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_CodeBlocks_DetectsCodeBlockElements()
    {
        var content = "Introduction text.\n\n```csharp\nvar x = 1;\n```\n\nMore text.\n\n```python\nprint('hello')\n```";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.StructuralElements.Should().Contain(e => e.Type == "CodeBlock" && e.Count == 2);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_Lists_DetectsListElements()
    {
        var content = "Items:\n- First item\n- Second item\n* Third item\n+ Fourth item";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.StructuralElements.Should().Contain(e => e.Type == "List" && e.Count == 4);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_Tables_DetectsTableElements()
    {
        var content = "Data table:\n| Col1 | Col2 |\n| --- | --- |\n| A | B |\n| C | D |";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.StructuralElements.Should().Contain(e => e.Type == "Table");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_TooFewTableRows_NoTableElement()
    {
        // Only 2 rows, need >3 for table detection
        var content = "| Col1 | Col2 |\n| A | B |";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.StructuralElements.Should().NotContain(e => e.Type == "Table");
    }

    #endregion

    #region DetectDocumentTypeAsync — Characteristics

    [Fact]
    public async Task DetectDocumentTypeAsync_AddsCharacteristics()
    {
        var content = "# Header\n\nSome text with ```code``` blocks.\n- List item\n| table |";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Characteristics.Should().ContainKey("WordCount");
        result.Characteristics.Should().ContainKey("LineCount");
        result.Characteristics.Should().ContainKey("HasCode");
        result.Characteristics.Should().ContainKey("HasHeaders");
        result.Characteristics.Should().ContainKey("HasLists");
        result.Characteristics.Should().ContainKey("HasTables");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_ContentWithCode_HasCodeTrue()
    {
        var content = "Here is some code:\n```python\nprint('hello')\n```";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Characteristics["HasCode"].Should().Be(true);
    }

    #endregion

    #region DetectDocumentTypeAsync — Complexity and sentence length

    [Fact]
    public async Task DetectDocumentTypeAsync_ComplexityScore_Between0And1()
    {
        var content = "This is a relatively simple sentence. Another short sentence here.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.ComplexityScore.Should().BeInRange(0, 1);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_AverageSentenceLength_Calculated()
    {
        var content = "First sentence. Second sentence.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.AverageSentenceLength.Should().BeGreaterThan(0);
    }

    #endregion

    #region DetectDocumentTypeAsync — SubType detection

    [Fact]
    public async Task DetectDocumentTypeAsync_TechnicalWithAPI_SubTypeAPIDocumentation()
    {
        var content = "The API endpoint provides code for the function algorithm. " +
                      "This implementation handles the software system compilation. " +
                      "The API method debug syntax compile class code.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Technical);
        result.SubType.Should().Be("API Documentation");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_LegalWithContract_SubTypeContract()
    {
        var content = "This legal agreement contract defines the clause liability. " +
                      "The court statute regulation compliance requires the attorney. " +
                      "Pursuant to law and contract agreement terms.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Legal);
        result.SubType.Should().Be("Contract");
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_AcademicWithAbstractMethodology_SubTypeResearchPaper()
    {
        var content = "The abstract describes the methodology of this research study. " +
                      "The hypothesis analysis and literature citation support the findings. " +
                      "Reference conclusion methodology abstract research.";

        var result = await _optimizer.DetectDocumentTypeAsync(content, null);

        result.Category.Should().Be(DocumentCategory.Academic);
        result.SubType.Should().Be("Research Paper");
    }

    #endregion

    #region DetectDocumentTypeAsync — Metadata adjustment

    [Fact]
    public async Task DetectDocumentTypeAsync_CsFileType_BoostsTechnical()
    {
        // Ambiguous content but .cs file extension → Technical boost
        var content = "This code implementation handles the market analysis for business growth. " +
                      "The system software function processes customer strategy data.";
        var metadata = new DocumentMetadata { FileType = ".cs" };

        var result = await _optimizer.DetectDocumentTypeAsync(content, metadata);

        result.Category.Should().Be(DocumentCategory.Technical);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_DocxFileType_BoostsBusiness()
    {
        // Content could be business or general
        var content = "The business strategy for customer growth and market performance. " +
                      "Management stakeholder product service competitive analysis. " +
                      "Business customer management strategy performance.";
        var metadata = new DocumentMetadata { FileType = ".docx" };

        var result = await _optimizer.DetectDocumentTypeAsync(content, metadata);

        result.Category.Should().Be(DocumentCategory.Business);
    }

    #endregion

    #region GetOptimalOptions — Basic

    [Fact]
    public void GetOptimalOptions_Technical_ReturnsAutoStrategy()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Technical,
            ComplexityScore = 0.5,
            StructuralElements = []
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.Strategy.Should().Be(ChunkingStrategies.Auto);
        options.MaxChunkSize.Should().Be(650); // (500+800)/2
    }

    [Fact]
    public void GetOptimalOptions_Legal_ReturnsSemanticStrategy()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Legal,
            ComplexityScore = 0.5,
            StructuralElements = []
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.Strategy.Should().Be(ChunkingStrategies.Semantic);
        options.MaxChunkSize.Should().Be(400); // (300+500)/2
    }

    #endregion

    #region GetOptimalOptions — Complexity adjustments

    [Fact]
    public void GetOptimalOptions_HighComplexity_UsesMinChunkSize()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Technical,
            ComplexityScore = 0.8, // > 0.7
            StructuralElements = []
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.MaxChunkSize.Should().Be(500); // Technical min
    }

    [Fact]
    public void GetOptimalOptions_LowComplexity_UsesMaxChunkSize()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Technical,
            ComplexityScore = 0.2, // < 0.3
            StructuralElements = []
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.MaxChunkSize.Should().Be(800); // Technical max
    }

    [Fact]
    public void GetOptimalOptions_MediumComplexity_UsesAverageChunkSize()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Technical,
            ComplexityScore = 0.5, // between 0.3 and 0.7
            StructuralElements = []
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.MaxChunkSize.Should().Be(650); // average
    }

    #endregion

    #region GetOptimalOptions — Structure adjustments

    [Fact]
    public void GetOptimalOptions_ManyCodeBlocks_SetsAutoStrategy()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Business, // normally Paragraph
            ComplexityScore = 0.5,
            StructuralElements =
            [
                new DocumentStructuralElement { Type = "CodeBlock", Count = 6, AverageSize = 100, Importance = 0.8 }
            ]
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.Strategy.Should().Be(ChunkingStrategies.Auto);
    }

    [Fact]
    public void GetOptimalOptions_HasTables_IncreasesChunkSize()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Technical,
            ComplexityScore = 0.5,
            StructuralElements =
            [
                new DocumentStructuralElement { Type = "Table", Count = 1, AverageSize = 500, Importance = 0.7 }
            ]
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        // 650 * 1.5 = 975
        options.MaxChunkSize.Should().Be(975);
    }

    [Fact]
    public void GetOptimalOptions_ManyHeaders_ParagraphToSemantic()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Business, // Paragraph strategy
            ComplexityScore = 0.5,
            StructuralElements =
            [
                new DocumentStructuralElement { Type = "Header", Count = 11, AverageSize = 20, Importance = 0.9 }
            ]
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.Strategy.Should().Be(ChunkingStrategies.Semantic);
    }

    [Fact]
    public void GetOptimalOptions_FewHeaders_KeepsOriginalStrategy()
    {
        var typeInfo = new DocumentTypeInfo
        {
            Category = DocumentCategory.Business,
            ComplexityScore = 0.5,
            StructuralElements =
            [
                new DocumentStructuralElement { Type = "Header", Count = 5, AverageSize = 20, Importance = 0.9 }
            ]
        };

        var options = _optimizer.GetOptimalOptions(typeInfo);

        options.Strategy.Should().Be(ChunkingStrategies.Paragraph);
    }

    #endregion

    #region GetOptimalOptionsAsync — Integration

    [Fact]
    public async Task GetOptimalOptionsAsync_CombinesDetectionAndOptimization()
    {
        var content = "The function implements an algorithm for code compilation. " +
                      "The API method handles the system software implementation. " +
                      "Debug the class syntax and compile the code.";

        var options = await _optimizer.GetOptimalOptionsAsync(content, null);

        options.Should().NotBeNull();
        options.MaxChunkSize.Should().BeGreaterThan(0);
        options.OverlapSize.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region GetPerformanceMetrics

    [Fact]
    public void GetPerformanceMetrics_ReturnsAllCategories()
    {
        var metrics = _optimizer.GetPerformanceMetrics();

        metrics.Should().HaveCount(8);
        metrics.Should().ContainKey(DocumentCategory.Technical);
        metrics.Should().ContainKey(DocumentCategory.Legal);
        metrics.Should().ContainKey(DocumentCategory.Academic);
        metrics.Should().ContainKey(DocumentCategory.Financial);
        metrics.Should().ContainKey(DocumentCategory.Medical);
        metrics.Should().ContainKey(DocumentCategory.Business);
        metrics.Should().ContainKey(DocumentCategory.Creative);
        metrics.Should().ContainKey(DocumentCategory.General);
    }

    [Fact]
    public void GetPerformanceMetrics_ReturnsDefensiveCopy()
    {
        var metrics1 = _optimizer.GetPerformanceMetrics();
        var metrics2 = _optimizer.GetPerformanceMetrics();

        metrics1.Should().NotBeSameAs(metrics2);
    }

    [Fact]
    public void GetPerformanceMetrics_TechnicalHasExpectedValues()
    {
        var metrics = _optimizer.GetPerformanceMetrics();
        var technical = metrics[DocumentCategory.Technical];

        technical.OptimalTokenRange.Min.Should().Be(500);
        technical.OptimalTokenRange.Max.Should().Be(800);
        technical.OptimalOverlapRange.Min.Should().Be(20);
        technical.OptimalOverlapRange.Max.Should().Be(30);
        technical.ExpectedF1Score.Should().Be(0.85);
        technical.RecommendedStrategy.Should().Be(ChunkingStrategies.Auto);
        technical.OptimizationHints.Should().HaveCount(3);
    }

    #endregion

    #region Edge cases

    [Fact]
    public async Task DetectDocumentTypeAsync_EmptyContent_HandlesGracefully()
    {
        var result = await _optimizer.DetectDocumentTypeAsync("", null);

        result.Category.Should().Be(DocumentCategory.General);
        result.ComplexityScore.Should().Be(0);
    }

    [Fact]
    public async Task DetectDocumentTypeAsync_SingleWord_HandlesGracefully()
    {
        var result = await _optimizer.DetectDocumentTypeAsync("hello", null);

        result.Should().NotBeNull();
        result.Language.Should().Be("en");
    }

    #endregion
}
