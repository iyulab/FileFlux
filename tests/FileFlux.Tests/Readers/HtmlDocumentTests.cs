using FileFlux.Core.Infrastructure.Readers;
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
public class HtmlDocumentTests
{
    private readonly ITestOutputHelper _output;

    public HtmlDocumentTests(ITestOutputHelper output)
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
            Assert.True(result.Hints.ContainsKey("has_semantic_structure"));
            Assert.True((bool)result.Hints["has_semantic_structure"]);
            
            // HTML5 요소들 인식 검증
            Assert.True(result.Hints.ContainsKey("semantic_elements"));
            var semanticElements = (string[])result.Hints["semantic_elements"];
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
            Assert.True(result.Hints.ContainsKey("title"));
            Assert.Equal("메타데이터 테스트", result.Hints["title"]);
            
            Assert.True(result.Hints.ContainsKey("description"));
            Assert.Equal("이것은 테스트용 HTML 문서입니다", result.Hints["description"]);
            
            Assert.True(result.Hints.ContainsKey("keywords"));
            var keywords = (string[])result.Hints["keywords"];
            Assert.Contains("test", keywords);
            Assert.Contains("html", keywords);
            Assert.Contains("metadata", keywords);
            Assert.Contains("fileflux", keywords);
            
            Assert.True(result.Hints.ContainsKey("author"));
            Assert.Equal("FileFlux Team", result.Hints["author"]);
            
            // Open Graph 메타데이터 검증
            Assert.True(result.Hints.ContainsKey("og_title"));
            Assert.Equal("Open Graph 제목", result.Hints["og_title"]);
            
            _output.WriteLine($"추출된 메타데이터:");
            _output.WriteLine($"Title: {result.Hints["title"]}");
            _output.WriteLine($"Description: {result.Hints["description"]}");
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
            Assert.True(result.Hints.ContainsKey("has_lists"));
            Assert.True((bool)result.Hints["has_lists"]);
            
            // 테이블 구조 검증
            Assert.True(result.Hints.ContainsKey("has_tables"));
            Assert.True((bool)result.Hints["has_tables"]);
            
            Assert.True(result.Hints.ContainsKey("table_count"));
            Assert.Equal(1, result.Hints["table_count"]);
            
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
            Assert.True(result.Hints.ContainsKey("has_links"));
            Assert.True((bool)result.Hints["has_links"]);
            
            Assert.True(result.Hints.ContainsKey("external_links"));
            var externalLinks = (string[])result.Hints["external_links"];
            Assert.Contains("https://example.com", externalLinks);
            
            // 이미지 컨텍스트 보존 검증
            Assert.True(result.Hints.ContainsKey("has_images"));
            Assert.True((bool)result.Hints["has_images"]);
            
            Assert.True(result.Hints.ContainsKey("image_count"));
            Assert.Equal(2, result.Hints["image_count"]);
            
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
            Assert.True(result.Hints.ContainsKey("has_code"));
            Assert.True((bool)result.Hints["has_code"]);
            
            Assert.True(result.Hints.ContainsKey("code_languages"));
            var codeLanguages = (string[])result.Hints["code_languages"];
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

    [Fact]
    public async Task ExtractAsync_WithBase64Images_ShouldExtractAndReplacePlaceholder()
    {
        // Arrange - Create a small valid PNG base64 (1x1 red pixel)
        var base64Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";
        var base64Jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==";

        var htmlContent = $"""
            <!DOCTYPE html>
            <html>
            <head><title>Base64 Image Test</title></head>
            <body>
                <h1>Document with Embedded Images</h1>
                <p>Here is an embedded PNG image:</p>
                <img src="data:image/png;base64,{base64Png}" alt="Red Pixel PNG" title="A 1x1 red pixel">
                <p>And an embedded JPEG image:</p>
                <img src="data:image/jpeg;base64,{base64Jpeg}" alt="Tiny JPEG">
                <p>And a regular external image:</p>
                <img src="https://example.com/image.png" alt="External Image">
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

            // Base64 should NOT appear in text (replaced with placeholder)
            Assert.DoesNotContain("iVBORw0KGgo", result.Text);
            Assert.DoesNotContain("/9j/4AAQSkZJRg", result.Text);

            // Placeholders should appear
            Assert.Contains("![Red Pixel PNG](embedded:img_000)", result.Text);
            Assert.Contains("![Tiny JPEG](embedded:img_001)", result.Text);

            // External image should remain as-is
            Assert.Contains("![External Image](https://example.com/image.png)", result.Text);

            // Images should be extracted to RawContent.Images
            Assert.Equal(3, result.Images.Count);

            // First image (PNG)
            var pngImage = result.Images.First(i => i.Id == "img_000");
            Assert.Equal("Red Pixel PNG", pngImage.Caption);
            Assert.Equal("image/png", pngImage.MimeType);
            Assert.NotNull(pngImage.Data);
            Assert.True(pngImage.Data.Length > 0);
            Assert.Equal("embedded:img_000", pngImage.SourceUrl);

            // Second image (JPEG)
            var jpegImage = result.Images.First(i => i.Id == "img_001");
            Assert.Equal("Tiny JPEG", jpegImage.Caption);
            Assert.Equal("image/jpeg", jpegImage.MimeType);
            Assert.NotNull(jpegImage.Data);

            // Third image (External URL)
            var externalImage = result.Images.First(i => i.Id == "img_002");
            Assert.Equal("External Image", externalImage.Caption);
            Assert.Null(externalImage.Data); // No binary data for external URLs
            Assert.Equal("https://example.com/image.png", externalImage.SourceUrl);

            // Hints should indicate embedded images were extracted
            Assert.True(result.Hints.ContainsKey("embedded_image_count"));
            Assert.Equal(2, result.Hints["embedded_image_count"]);
            Assert.True((bool)result.Hints["embedded_images_extracted"]);

            _output.WriteLine($"Base64 image extraction result:");
            _output.WriteLine($"Text length: {result.Text.Length} chars");
            _output.WriteLine($"Images extracted: {result.Images.Count}");
            _output.WriteLine($"Text preview: {result.Text[..Math.Min(500, result.Text.Length)]}...");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ExtractAsync_WithLargeBase64Image_ShouldExtractAndReduceTextSize()
    {
        // Arrange - Simulate a large base64 image (repeat base64 data to simulate ~100KB)
        var smallBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";
        // Create a fake "large" base64 by repeating valid base64 chars (won't decode but tests text size reduction)
        var largeBase64 = string.Concat(Enumerable.Repeat("AAAA", 25000)); // ~100KB of base64-like data

        var htmlContentWithBase64 = $"""
            <!DOCTYPE html>
            <html>
            <body>
                <p>Document content</p>
                <img src="data:image/png;base64,{largeBase64}" alt="Large Image">
            </body>
            </html>
            """;

        var htmlContentWithoutBase64 = """
            <!DOCTYPE html>
            <html>
            <body>
                <p>Document content</p>
                <img src="large-image.png" alt="Large Image">
            </body>
            </html>
            """;

        var tempFileWithBase64 = await CreateTempHtmlFileAsync(htmlContentWithBase64);
        var tempFileWithoutBase64 = await CreateTempHtmlFileAsync(htmlContentWithoutBase64);

        try
        {
            // Act
            var reader = new HtmlDocumentReader();
            var resultWithBase64 = await reader.ExtractAsync(tempFileWithBase64);
            var resultWithoutBase64 = await reader.ExtractAsync(tempFileWithoutBase64);

            // Assert - Text size should be similar (base64 replaced with small placeholder)
            var textWithExtraction = resultWithBase64.Text;
            var textWithoutBase64 = resultWithoutBase64.Text;

            // The extracted text should NOT contain the huge base64 data
            Assert.DoesNotContain("AAAA", textWithExtraction);

            // Text sizes should be comparable (within ~100 chars difference)
            Assert.True(Math.Abs(textWithExtraction.Length - textWithoutBase64.Length) < 100,
                $"Text sizes differ significantly: with extraction={textWithExtraction.Length}, without={textWithoutBase64.Length}");

            // Image should still be tracked
            Assert.Single(resultWithBase64.Images);
            Assert.Equal("embedded:img_000", resultWithBase64.Images[0].SourceUrl);

            // Original size should be recorded
            Assert.True(resultWithBase64.Images[0].OriginalSize > 50000); // Should be ~100KB

            _output.WriteLine($"Text reduction test:");
            _output.WriteLine($"Original base64 size: ~{largeBase64.Length} chars");
            _output.WriteLine($"Extracted text size: {textWithExtraction.Length} chars");
            _output.WriteLine($"Reference text size: {textWithoutBase64.Length} chars");
        }
        finally
        {
            if (File.Exists(tempFileWithBase64))
                File.Delete(tempFileWithBase64);
            if (File.Exists(tempFileWithoutBase64))
                File.Delete(tempFileWithoutBase64);
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