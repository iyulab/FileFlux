using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FileFlux.Infrastructure.Readers;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace FileFlux.Tests.Readers;

/// <summary>
/// HTML 문서 고급 처리 기능 테스트
/// - 시맨틱 구조 인식 (header, nav, main, article, section, aside, footer)
/// - 메타데이터 추출 (title, description, keywords)
/// - 구조화된 콘텐츠 추출 (헤딩 계층, 리스트, 테이블)
/// - 링크 및 이미지 컨텍스트 보존
/// </summary>
public class HtmlDocumentAdvancedTests
{
    private readonly ITestOutputHelper _output;

    public HtmlDocumentAdvancedTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ExtractAsync_WithSemanticStructure_ShouldRecognizeHtml5Elements()
    {
        // Arrange
        var htmlContent = """
            <!DOCTYPE html>
            <html lang="ko">
            <head>
                <meta charset="UTF-8">
                <title>시맨틱 HTML 테스트 문서</title>
                <meta name="description" content="HTML5 시맨틱 요소들을 테스트하는 문서입니다">
                <meta name="keywords" content="html5, semantic, test, fileflux">
            </head>
            <body>
                <header>
                    <h1>메인 헤더</h1>
                    <nav>
                        <ul>
                            <li><a href="#section1">섹션 1</a></li>
                            <li><a href="#section2">섹션 2</a></li>
                        </ul>
                    </nav>
                </header>
                
                <main>
                    <article>
                        <h2>주요 기사</h2>
                        <p>이것은 주요 기사의 내용입니다.</p>
                        
                        <section id="section1">
                            <h3>섹션 1 제목</h3>
                            <p>섹션 1의 내용입니다.</p>
                        </section>
                        
                        <section id="section2">
                            <h3>섹션 2 제목</h3>
                            <p>섹션 2의 내용입니다.</p>
                        </section>
                    </article>
                    
                    <aside>
                        <h4>사이드바</h4>
                        <p>부가적인 정보입니다.</p>
                    </aside>
                </main>
                
                <footer>
                    <p>© 2024 FileFlux Test</p>
                </footer>
            </body>
            </html>
            """;

        var tempFile = await CreateTempHtmlFileAsync(htmlContent);

        try
        {
            // Act
            var reader = new HtmlDocumentReader();
            var result = await reader.ExtractAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result.Text);
            
            // 시맨틱 구조 인식 검증
            Assert.True(result.StructuralHints.ContainsKey("has_semantic_structure"));
            Assert.True((bool)result.StructuralHints["has_semantic_structure"]);
            
            // HTML5 요소들 인식 검증
            Assert.True(result.StructuralHints.ContainsKey("semantic_elements"));
            var semanticElements = (string[])result.StructuralHints["semantic_elements"];
            Assert.Contains("header", semanticElements);
            Assert.Contains("nav", semanticElements);
            Assert.Contains("main", semanticElements);
            Assert.Contains("article", semanticElements);
            Assert.Contains("section", semanticElements);
            Assert.Contains("aside", semanticElements);
            Assert.Contains("footer", semanticElements);
            
            // 헤딩 계층 구조 검증
            Assert.Contains("# 메인 헤더", result.Text);
            Assert.Contains("## 주요 기사", result.Text);
            Assert.Contains("### 섹션 1 제목", result.Text);
            Assert.Contains("### 섹션 2 제목", result.Text);
            Assert.Contains("#### 사이드바", result.Text);
            
            _output.WriteLine($"HTML 구조화 결과:");
            _output.WriteLine(result.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithMetadata_ShouldExtractHtmlMetaTags()
    {
        // Arrange
        var htmlContent = """
            <!DOCTYPE html>
            <html lang="ko">
            <head>
                <title>메타데이터 테스트</title>
                <meta name="description" content="이것은 테스트용 HTML 문서입니다">
                <meta name="keywords" content="test, html, metadata, fileflux">
                <meta name="author" content="FileFlux Team">
                <meta property="og:title" content="Open Graph 제목">
                <meta property="og:description" content="Open Graph 설명">
            </head>
            <body>
                <h1>메타데이터가 있는 HTML 문서</h1>
                <p>이 문서는 메타데이터 추출을 테스트합니다.</p>
            </body>
            </html>
            """;

        var tempFile = await CreateTempHtmlFileAsync(htmlContent);

        try
        {
            // Act
            var reader = new HtmlDocumentReader();
            var result = await reader.ExtractAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            
            // 메타데이터 추출 검증
            Assert.True(result.StructuralHints.ContainsKey("title"));
            Assert.Equal("메타데이터 테스트", result.StructuralHints["title"]);
            
            Assert.True(result.StructuralHints.ContainsKey("description"));
            Assert.Equal("이것은 테스트용 HTML 문서입니다", result.StructuralHints["description"]);
            
            Assert.True(result.StructuralHints.ContainsKey("keywords"));
            var keywords = (string[])result.StructuralHints["keywords"];
            Assert.Contains("test", keywords);
            Assert.Contains("html", keywords);
            Assert.Contains("metadata", keywords);
            Assert.Contains("fileflux", keywords);
            
            Assert.True(result.StructuralHints.ContainsKey("author"));
            Assert.Equal("FileFlux Team", result.StructuralHints["author"]);
            
            // Open Graph 메타데이터 검증
            Assert.True(result.StructuralHints.ContainsKey("og_title"));
            Assert.Equal("Open Graph 제목", result.StructuralHints["og_title"]);
            
            _output.WriteLine($"추출된 메타데이터:");
            _output.WriteLine($"Title: {result.StructuralHints["title"]}");
            _output.WriteLine($"Description: {result.StructuralHints["description"]}");
            _output.WriteLine($"Keywords: {string.Join(", ", keywords)}");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithTablesAndLists_ShouldExtractStructuredContent()
    {
        // Arrange
        var htmlContent = """
            <!DOCTYPE html>
            <html>
            <head>
                <title>표와 리스트 테스트</title>
            </head>
            <body>
                <h1>구조화된 콘텐츠</h1>
                
                <h2>순서가 있는 리스트</h2>
                <ol>
                    <li>첫 번째 항목</li>
                    <li>두 번째 항목
                        <ul>
                            <li>하위 항목 1</li>
                            <li>하위 항목 2</li>
                        </ul>
                    </li>
                    <li>세 번째 항목</li>
                </ol>
                
                <h2>데이터 표</h2>
                <table>
                    <caption>사용자 정보 표</caption>
                    <thead>
                        <tr>
                            <th>이름</th>
                            <th>나이</th>
                            <th>직업</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr>
                            <td>김철수</td>
                            <td>30</td>
                            <td>개발자</td>
                        </tr>
                        <tr>
                            <td>이영희</td>
                            <td>25</td>
                            <td>디자이너</td>
                        </tr>
                    </tbody>
                </table>
            </body>
            </html>
            """;

        var tempFile = await CreateTempHtmlFileAsync(htmlContent);

        try
        {
            // Act
            var reader = new HtmlDocumentReader();
            var result = await reader.ExtractAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            
            // 리스트 구조 검증
            Assert.True(result.StructuralHints.ContainsKey("has_lists"));
            Assert.True((bool)result.StructuralHints["has_lists"]);
            
            // 테이블 구조 검증
            Assert.True(result.StructuralHints.ContainsKey("has_tables"));
            Assert.True((bool)result.StructuralHints["has_tables"]);
            
            Assert.True(result.StructuralHints.ContainsKey("table_count"));
            Assert.Equal(1, result.StructuralHints["table_count"]);
            
            // 리스트 마크다운 형식 검증
            Assert.Contains("1. 첫 번째 항목", result.Text);
            Assert.Contains("2. 두 번째 항목", result.Text);
            Assert.Contains("   - 하위 항목 1", result.Text);
            Assert.Contains("   - 하위 항목 2", result.Text);
            
            // 테이블 마크다운 형식 검증
            Assert.Contains("--- TABLE: 사용자 정보 표 ---", result.Text);
            Assert.Contains("이름 | 나이 | 직업", result.Text);
            Assert.Contains("김철수 | 30 | 개발자", result.Text);
            Assert.Contains("이영희 | 25 | 디자이너", result.Text);
            
            _output.WriteLine($"구조화된 콘텐츠:");
            _output.WriteLine(result.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithLinksAndImages_ShouldPreserveContext()
    {
        // Arrange
        var htmlContent = """
            <!DOCTYPE html>
            <html>
            <head>
                <title>링크와 이미지 테스트</title>
            </head>
            <body>
                <h1>링크와 이미지가 있는 문서</h1>
                
                <p>이것은 <a href="https://example.com" title="예제 사이트">외부 링크</a>가 포함된 문단입니다.</p>
                
                <p>내부 링크도 있습니다: <a href="#section1">섹션 1로 이동</a></p>
                
                <div>
                    <img src="image1.jpg" alt="첫 번째 이미지" title="이미지 제목">
                    <p>이미지 설명: 이것은 첫 번째 테스트 이미지입니다.</p>
                </div>
                
                <figure>
                    <img src="chart.png" alt="데이터 차트">
                    <figcaption>2024년 판매 데이터 차트</figcaption>
                </figure>
                
                <section id="section1">
                    <h2>섹션 1</h2>
                    <p>이곳이 링크의 대상입니다.</p>
                </section>
            </body>
            </html>
            """;

        var tempFile = await CreateTempHtmlFileAsync(htmlContent);

        try
        {
            // Act
            var reader = new HtmlDocumentReader();
            var result = await reader.ExtractAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            
            // 링크 컨텍스트 보존 검증
            Assert.True(result.StructuralHints.ContainsKey("has_links"));
            Assert.True((bool)result.StructuralHints["has_links"]);
            
            Assert.True(result.StructuralHints.ContainsKey("external_links"));
            var externalLinks = (string[])result.StructuralHints["external_links"];
            Assert.Contains("https://example.com", externalLinks);
            
            // 이미지 컨텍스트 보존 검증
            Assert.True(result.StructuralHints.ContainsKey("has_images"));
            Assert.True((bool)result.StructuralHints["has_images"]);
            
            Assert.True(result.StructuralHints.ContainsKey("image_count"));
            Assert.Equal(2, result.StructuralHints["image_count"]);
            
            // 링크 텍스트 형식 검증
            Assert.Contains("[외부 링크](https://example.com)", result.Text);
            Assert.Contains("[섹션 1로 이동](#section1)", result.Text);
            
            // 이미지 텍스트 형식 검증
            Assert.Contains("![첫 번째 이미지](image1.jpg)", result.Text);
            Assert.Contains("![데이터 차트](chart.png)", result.Text);
            Assert.Contains("*Figure: 2024년 판매 데이터 차트*", result.Text);
            
            _output.WriteLine($"링크와 이미지 처리 결과:");
            _output.WriteLine(result.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithCodeBlocks_ShouldPreserveFormatting()
    {
        // Arrange
        var htmlContent = """
            <!DOCTYPE html>
            <html>
            <head>
                <title>코드 블록 테스트</title>
            </head>
            <body>
                <h1>코드가 포함된 문서</h1>
                
                <p>인라인 코드: <code>console.log("Hello");</code></p>
                
                <h2>JavaScript 예제</h2>
                <pre><code class="language-javascript">
                function greet(name) {
                    return "Hello, " + name + "!";
                }

                const message = greet("World");
                console.log(message);
                </code></pre>
                
                <h2>HTML 예제</h2>
                <pre><code class="language-html">
                &lt;div class="container"&gt;
                    &lt;h1&gt;제목&lt;/h1&gt;
                    &lt;p&gt;내용&lt;/p&gt;
                &lt;/div&gt;
                </code></pre>
            </body>
            </html>
            """;

        var tempFile = await CreateTempHtmlFileAsync(htmlContent);

        try
        {
            // Act
            var reader = new HtmlDocumentReader();
            var result = await reader.ExtractAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            
            // 코드 블록 인식 검증
            Assert.True(result.StructuralHints.ContainsKey("has_code"));
            Assert.True((bool)result.StructuralHints["has_code"]);
            
            Assert.True(result.StructuralHints.ContainsKey("code_languages"));
            var codeLanguages = (string[])result.StructuralHints["code_languages"];
            Assert.Contains("javascript", codeLanguages);
            Assert.Contains("html", codeLanguages);
            
            // 인라인 코드 형식 검증
            Assert.Contains("`console.log(\"Hello\");`", result.Text);
            
            // 코드 블록 형식 검증
            Assert.Contains("```javascript", result.Text);
            Assert.Contains("function greet(name)", result.Text);
            Assert.Contains("```html", result.Text);
            Assert.Contains("<div class=\"container\">", result.Text);
            
            _output.WriteLine($"코드 블록 처리 결과:");
            _output.WriteLine(result.Text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static async Task<string> CreateTempHtmlFileAsync(string htmlContent)
    {
        var tempFile = Path.GetTempFileName();
        var htmlFile = Path.ChangeExtension(tempFile, ".html");
        
        await File.WriteAllTextAsync(htmlFile, htmlContent, Encoding.UTF8);
        
        if (File.Exists(tempFile))
            File.Delete(tempFile);
            
        return htmlFile;
    }
}