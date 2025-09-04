using FileFlux.Core;
using FileFlux.Domain;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// PDF 파일 처리를 위한 문서 Reader
/// PdfPig 라이브러리를 사용하여 텍스트 추출 및 구조 인식
/// </summary>
public partial class PdfDocumentReader : IDocumentReader
{
    public string ReaderType => "PdfReader";

    public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };
    private static readonly char[] separator = new[] { ' ', '\t', '\n', '\r' };

    public bool CanRead(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension == ".pdf";
    }

    public async Task<RawDocumentContent> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}");

        if (!CanRead(filePath))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(filePath)}", nameof(filePath));

        return await Task.Run(() => ExtractPdfContent(filePath, cancellationToken), cancellationToken);
    }

    public async Task<RawDocumentContent> ExtractAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!CanRead(fileName))
            throw new ArgumentException($"File format not supported: {Path.GetExtension(fileName)}", nameof(fileName));

        return await Task.Run(() => ExtractPdfContentFromStream(stream, fileName, cancellationToken), cancellationToken);
    }

    private RawDocumentContent ExtractPdfContent(string filePath, CancellationToken cancellationToken)
    {
        var extractionWarnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var textBuilder = new StringBuilder();

        var startTime = DateTime.UtcNow;
        var fileInfo = new FileInfo(filePath);

        try
        {
            using var document = PdfDocument.Open(filePath);

            // PDF 메타데이터 수집
            var info = document.Information;
            if (info != null)
            {
                structuralHints["Title"] = info.Title ?? "";
                structuralHints["Author"] = info.Author ?? "";
                structuralHints["Subject"] = info.Subject ?? "";
                structuralHints["Creator"] = info.Creator ?? "";
                structuralHints["Producer"] = info.Producer ?? "";
                if (info.CreationDate != null)
                    structuralHints["CreationDate"] = info.CreationDate;
                if (info.ModifiedDate != null)
                    structuralHints["ModifiedDate"] = info.ModifiedDate;
            }

            structuralHints["PageCount"] = document.NumberOfPages;
            structuralHints["Version"] = document.Version.ToString();

            var totalPages = document.NumberOfPages;
            var processedPages = 0;

            // 페이지별 텍스트 추출
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = document.GetPage(pageNum);
                    var pageText = ExtractPageText(page, pageNum, extractionWarnings);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        // 페이지 구분자 추가 (청킹에 더 적합한 형식)
                        if (textBuilder.Length > 0)
                        {
                            textBuilder.AppendLine();
                            textBuilder.AppendLine(); // 추가 공백으로 명확한 페이지 경계
                        }

                        // 페이지 텍스트 전처리 및 정리
                        var cleanedText = NormalizeText(pageText);
                        textBuilder.AppendLine(cleanedText);
                    }

                    processedPages++;
                }
                catch (Exception ex)
                {
                    extractionWarnings.Add($"페이지 {pageNum} 처리 중 오류: {ex.Message}");
                }
            }

            // 추출 통계
            var extractedText = textBuilder.ToString();
            structuralHints["ProcessedPages"] = processedPages;
            structuralHints["TotalCharacters"] = extractedText.Length;
            structuralHints["WordCount"] = CountWords(extractedText);
            structuralHints["LineCount"] = extractedText.Split('\n').Length;

            if (processedPages < totalPages)
            {
                extractionWarnings.Add($"일부 페이지 처리 실패: {processedPages}/{totalPages} 페이지만 처리됨");
            }

            return new RawDocumentContent
            {
                Text = extractedText,
                FileInfo = new FileMetadata
                {
                    FileName = Path.GetFileName(filePath),
                    FileExtension = ".pdf",
                    FileSize = fileInfo.Length,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = ReaderType
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = extractionWarnings
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF 파일 처리 중 오류 발생: {ex.Message}", ex);
        }
    }

    private RawDocumentContent ExtractPdfContentFromStream(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var extractionWarnings = new List<string>();
        var structuralHints = new Dictionary<string, object>();
        var textBuilder = new StringBuilder();

        var startTime = DateTime.UtcNow;
        var streamLength = stream.CanSeek ? stream.Length : -1;

        try
        {
            using var document = PdfDocument.Open(stream);

            // PDF 메타데이터 수집
            var info = document.Information;
            if (info != null)
            {
                structuralHints["Title"] = info.Title ?? "";
                structuralHints["Author"] = info.Author ?? "";
                structuralHints["Subject"] = info.Subject ?? "";
                structuralHints["Creator"] = info.Creator ?? "";
                structuralHints["Producer"] = info.Producer ?? "";
                if (info.CreationDate != null)
                    structuralHints["CreationDate"] = info.CreationDate;
                if (info.ModifiedDate != null)
                    structuralHints["ModifiedDate"] = info.ModifiedDate;
            }

            structuralHints["PageCount"] = document.NumberOfPages;
            structuralHints["Version"] = document.Version.ToString();

            var totalPages = document.NumberOfPages;
            var processedPages = 0;

            // 페이지별 텍스트 추출
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var page = document.GetPage(pageNum);
                    var pageText = ExtractPageText(page, pageNum, extractionWarnings);

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        // 페이지 구분자 추가 (청킹에 더 적합한 형식)
                        if (textBuilder.Length > 0)
                        {
                            textBuilder.AppendLine();
                            textBuilder.AppendLine(); // 추가 공백으로 명확한 페이지 경계
                        }

                        // 페이지 텍스트 전처리 및 정리
                        var cleanedText = NormalizeText(pageText);
                        textBuilder.AppendLine(cleanedText);
                    }

                    processedPages++;
                }
                catch (Exception ex)
                {
                    extractionWarnings.Add($"페이지 {pageNum} 처리 중 오류: {ex.Message}");
                }
            }

            // 추출 통계
            var extractedText = textBuilder.ToString();
            structuralHints["ProcessedPages"] = processedPages;
            structuralHints["TotalCharacters"] = extractedText.Length;
            structuralHints["WordCount"] = CountWords(extractedText);
            structuralHints["LineCount"] = extractedText.Split('\n').Length;

            if (processedPages < totalPages)
            {
                extractionWarnings.Add($"일부 페이지 처리 실패: {processedPages}/{totalPages} 페이지만 처리됨");
            }

            return new RawDocumentContent
            {
                Text = extractedText,
                FileInfo = new FileMetadata
                {
                    FileName = fileName,
                    FileExtension = ".pdf",
                    FileSize = streamLength,
                    ExtractedAt = DateTime.UtcNow,
                    ReaderType = ReaderType
                },
                StructuralHints = structuralHints,
                ExtractionWarnings = extractionWarnings
            };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PDF 스트림 처리 중 오류 발생: {ex.Message}", ex);
        }
    }

    private string ExtractPageText(Page page, int pageNum, List<string> warnings)
    {
        try
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                warnings.Add($"페이지 {pageNum}: 텍스트 없음");
                return "";
            }

            // 단어를 위치 기준으로 정렬 (위에서 아래로, 왼쪽에서 오른쪽으로)
            var sortedWords = words
                .OrderBy(w => Math.Round(page.Height - w.BoundingBox.Bottom, 1)) // Y 좌표 (위에서 아래)
                .ThenBy(w => Math.Round(w.BoundingBox.Left, 1))                   // X 좌표 (왼쪽에서 오른쪽)
                .ToList();

            var textBuilder = new StringBuilder();
            var currentLineY = double.MinValue;
            var lineHeight = EstimateLineHeight(sortedWords);

            foreach (var word in sortedWords)
            {
                var wordY = Math.Round(page.Height - word.BoundingBox.Bottom, 1);

                // 새로운 줄 감지
                if (Math.Abs(wordY - currentLineY) > lineHeight * 0.5)
                {
                    if (textBuilder.Length > 0)
                    {
                        textBuilder.AppendLine();
                    }
                    currentLineY = wordY;
                }
                else
                {
                    // 같은 줄에서 단어 사이 공백 추가
                    if (textBuilder.Length > 0 && !textBuilder.ToString().EndsWith('\n'))
                    {
                        textBuilder.Append(' ');
                    }
                }

                textBuilder.Append(word.Text);
            }

            return textBuilder.ToString();
        }
        catch (Exception ex)
        {
            warnings.Add($"페이지 {pageNum} 텍스트 추출 오류: {ex.Message}");
            return "";
        }
    }

    private static double EstimateLineHeight(IList<Word> words)
    {
        if (!words.Any()) return 12.0; // 기본값

        // 단어들의 높이 분석
        var heights = words
            .Where(w => w.BoundingBox.Height > 0)
            .Select(w => w.BoundingBox.Height)
            .ToList();

        if (heights.Count == 0) return 12.0;

        // 중간값 사용 (이상치 제거)
        heights.Sort();
        var medianHeight = heights[heights.Count / 2];

        // 줄간격은 보통 글자 높이의 1.2~1.5배
        return medianHeight * 1.3;
    }

    /// <summary>
    /// 추출된 텍스트를 RAG에 최적화된 형태로 정규화
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        // 1. 연속된 공백 및 탭 정리
        text = MyRegex().Replace(text, " ");

        // 2. 연속된 줄바꿈 정리 (3개 이상 → 2개로 제한)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

        // 3. 줄 끝의 불필요한 공백 제거
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }

        // 4. 빈 줄들 사이의 단일 공백 제거
        text = string.Join('\n', lines);

        // 5. 문서 시작/끝 공백 정리
        text = text.Trim();

        // 6. 하이픈으로 끝나는 단어 연결 (예: "docu-\nment" → "document")
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\w+)-\s*\n\s*(\w+)", "$1$2");

        // 7. 단락 내 줄바꿈 정리 (문장 중간의 줄바꿈을 공백으로)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<![.!?])\n(?![A-Z•\d])", " ");

        return text;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        return text
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"[ \t]+")]
    private static partial System.Text.RegularExpressions.Regex MyRegex();
}