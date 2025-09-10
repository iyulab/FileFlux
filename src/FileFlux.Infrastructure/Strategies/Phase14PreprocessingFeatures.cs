using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FileFlux.Domain;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Phase 14: 고급 전처리 기능 통합 시스템
/// 문서 요약, 의미적 압축, 문서 증강, 멀티모달 고도화를 포괄하는 전처리 시스템
/// </summary>
public class Phase14PreprocessingFeatures
{
    public async Task<PreprocessingResult> ProcessDocumentAsync(
        ParsedDocumentContent document,
        PreprocessingOptions options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PreprocessingOptions();
        await Task.Yield();

        var result = new PreprocessingResult
        {
            OriginalDocument = document,
            ProcessingStartTime = DateTime.UtcNow
        };

        // T14-001: 문서 요약 시스템
        if (options.EnableSummarization)
        {
            result.Summary = await GenerateSummaryAsync(document, options.SummaryOptions, cancellationToken);
        }

        // T14-002: 의미적 압축
        if (options.EnableCompression)
        {
            result.CompressedContent = await CompressContentAsync(document, options.CompressionOptions, cancellationToken);
        }

        // T14-003: 문서 증강
        if (options.EnableAugmentation)
        {
            result.AugmentedContent = await AugmentContentAsync(document, options.AugmentationOptions, cancellationToken);
        }

        // T14-004: 멀티모달 고도화 (간단한 구현)
        if (options.EnableMultimodalEnhancement)
        {
            result.MultimodalAnalysis = await AnalyzeMultimodalContentAsync(document, cancellationToken);
        }

        result.ProcessingEndTime = DateTime.UtcNow;
        result.ProcessingScore = CalculateProcessingScore(result);

        return result;
    }

    /// <summary>
    /// T14-001: 문서 요약 시스템 (간단한 구현)
    /// </summary>
    private async Task<DocumentSummary> GenerateSummaryAsync(
        ParsedDocumentContent document, 
        SummaryOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        
        var summary = new DocumentSummary();
        var text = document.StructuredText;
        
        // 추출적 요약 - 첫 번째와 마지막 문장들
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s) && s.Trim().Length > 10)
            .Select(s => s.Trim())
            .ToList();

        if (sentences.Count > 0)
        {
            var summaryLength = Math.Min(3, sentences.Count);
            var selectedSentences = new List<string>();
            
            // 첫 번째 문장
            selectedSentences.Add(sentences[0]);
            
            // 중간 문장
            if (sentences.Count > 2 && summaryLength > 1)
            {
                selectedSentences.Add(sentences[sentences.Count / 2]);
            }
            
            // 마지막 문장
            if (sentences.Count > 1 && summaryLength > 2)
            {
                selectedSentences.Add(sentences[^1]);
            }

            summary.ExtractedSentences = selectedSentences;
            summary.SummaryText = string.Join(" ", selectedSentences);
        }

        // 키워드 추출 - 자주 나오는 단어들
        var words = text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 4 && !IsStopWord(w))
            .GroupBy(w => w.ToLower())
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.Key)
            .ToList();
            
        summary.KeyPhrases = words;
        summary.ConfidenceScore = sentences.Count > 0 ? 0.7 : 0.3;

        return summary;
    }

    /// <summary>
    /// T14-002: 의미적 압축 시스템 (간단한 구현)
    /// </summary>
    private async Task<CompressedContent> CompressContentAsync(
        ParsedDocumentContent document,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        
        var compressed = new CompressedContent();
        var text = document.StructuredText;
        
        // 중복 제거 - 반복되는 문장 제거
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct()
            .ToList();
        
        compressed.DeduplicatedText = string.Join(". ", sentences);
        
        // 핵심 정보 추출 - 긴 문장들 우선
        var coreSentences = sentences
            .Where(s => s.Length > 50) // 50자 이상의 문장들
            .OrderByDescending(s => s.Length)
            .Take(Math.Min(5, sentences.Count))
            .ToList();
            
        compressed.CoreInformation = string.Join(". ", coreSentences);
        
        // 압축률 계산
        compressed.CompressionRatio = (double)compressed.CoreInformation.Length / text.Length;
        compressed.QualityScore = compressed.CompressionRatio > 0.3 ? 0.8 : 0.5;

        return compressed;
    }

    /// <summary>
    /// T14-003: 문서 증강 시스템 (간단한 구현)
    /// </summary>
    private async Task<AugmentedContent> AugmentContentAsync(
        ParsedDocumentContent document,
        AugmentationOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        
        var augmented = new AugmentedContent();
        var text = document.StructuredText;
        
        // 컨텍스트 확장 - 메타데이터 정보 추가
        var contextInfo = new StringBuilder();
        contextInfo.AppendLine("[문서 컨텍스트]");
        contextInfo.AppendLine($"파일명: {document.Metadata.FileName}");
        contextInfo.AppendLine($"생성일: {document.Metadata.CreatedAt}");
        contextInfo.AppendLine($"언어: {document.Metadata.Language}");
        
        if (document.Structure.Keywords?.Any() == true)
        {
            contextInfo.AppendLine($"키워드: {string.Join(", ", document.Structure.Keywords)}");
        }
        
        augmented.ContextInformation = contextInfo.ToString();
        
        // 설명 주석 생성 - 복잡한 용어나 약어에 대한 간단한 설명
        var annotations = new List<string>();
        var complexTerms = ExtractComplexTerms(text);
        foreach (var term in complexTerms.Take(5))
        {
            annotations.Add($"{term}: [복합 기술 용어]");
        }
        
        augmented.Annotations = annotations;
        
        // 참조 링크 강화 - URL이나 파일명 감지
        var references = text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Contains("http") || w.Contains("www") || w.EndsWith(".pdf") || w.EndsWith(".doc"))
            .Distinct()
            .Take(10)
            .ToList();
            
        augmented.References = references;
        augmented.EnhancementScore = (annotations.Count + references.Count) / 10.0;

        return augmented;
    }

    private bool IsStopWord(string word)
    {
        var stopWords = new HashSet<string> { "the", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", "이", "가", "을", "를", "에", "에서", "로", "으로", "와", "과", "그리고", "또는" };
        return stopWords.Contains(word.ToLower());
    }

    private List<string> ExtractComplexTerms(string text)
    {
        return text.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 8 || (w.Length > 3 && w.All(char.IsUpper)))
            .Distinct()
            .Take(10)
            .ToList();
    }

    /// <summary>
    /// T14-004: 멀티모달 분석 (간단한 구현)
    /// </summary>
    private async Task<MultimodalAnalysis> AnalyzeMultimodalContentAsync(
        ParsedDocumentContent document,
        CancellationToken cancellationToken)
    {
        await Task.Yield();
        
        var analysis = new MultimodalAnalysis();
        
        // 구조화된 콘텐츠 분석
        var structuredContent = document.StructuredText;
        
        // 테이블 패턴 감지 (텍스트에서)
        var tablePatterns = new[] { "|", "┌", "└", "├", "─", "│", "표", "Table" };
        analysis.HasTables = tablePatterns.Any(pattern => structuredContent.Contains(pattern));
        
        // 이미지 참조 감지
        var imagePatterns = new[] { "그림", "Figure", "Fig.", "이미지", "Image", ".png", ".jpg", ".jpeg" };
        analysis.HasImages = imagePatterns.Any(pattern => structuredContent.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        
        // 다이어그램 감지
        var diagramPatterns = new[] { "다이어그램", "diagram", "차트", "chart", "그래프", "graph", "플로우", "flow" };
        analysis.HasDiagrams = diagramPatterns.Any(pattern => structuredContent.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        
        // 레이아웃 복잡성 분석
        var sectionCount = document.Structure.Sections?.Count ?? 0;
        analysis.LayoutComplexity = sectionCount switch
        {
            0 => "Simple",
            <= 3 => "Moderate", 
            <= 7 => "Complex",
            _ => "Very Complex"
        };
        
        // 분석 점수 계산
        var score = 0.5; // 기본 점수
        if (analysis.HasTables) score += 0.2;
        if (analysis.HasImages) score += 0.2;
        if (analysis.HasDiagrams) score += 0.1;
        
        analysis.AnalysisScore = Math.Min(score, 1.0);
        
        return analysis;
    }

    private double CalculateProcessingScore(PreprocessingResult result)
    {
        var score = 0.0;
        var factors = 0;

        if (result.Summary != null)
        {
            score += result.Summary.ConfidenceScore;
            factors++;
        }

        if (result.CompressedContent != null)
        {
            score += result.CompressedContent.QualityScore;
            factors++;
        }

        if (result.AugmentedContent != null)
        {
            score += result.AugmentedContent.EnhancementScore;
            factors++;
        }

        if (result.MultimodalAnalysis != null)
        {
            score += result.MultimodalAnalysis.AnalysisScore;
            factors++;
        }

        return factors > 0 ? score / factors : 0.0;
    }
}

// Phase 14 데이터 구조들
public class PreprocessingOptions
{
    public bool EnableSummarization { get; set; } = true;
    public bool EnableCompression { get; set; } = true;
    public bool EnableAugmentation { get; set; } = true;
    public bool EnableMultimodalEnhancement { get; set; } = true;
    
    public SummaryOptions SummaryOptions { get; set; } = new();
    public CompressionOptions CompressionOptions { get; set; } = new();
    public AugmentationOptions AugmentationOptions { get; set; } = new();
}

public class SummaryOptions
{
    public int MaxSentences { get; set; } = 3;
    public double MinConfidence { get; set; } = 0.5;
}

public class CompressionOptions
{
    public double MinCompressionRatio { get; set; } = 0.3;
    public int MaxCoreSentences { get; set; } = 5;
}

public class AugmentationOptions
{
    public int MaxAnnotations { get; set; } = 5;
    public int MaxReferences { get; set; } = 10;
}

public class PreprocessingResult
{
    public ParsedDocumentContent OriginalDocument { get; set; }
    public DocumentSummary Summary { get; set; }
    public CompressedContent CompressedContent { get; set; }
    public AugmentedContent AugmentedContent { get; set; }
    public MultimodalAnalysis MultimodalAnalysis { get; set; }
    public double ProcessingScore { get; set; }
    public DateTime ProcessingStartTime { get; set; }
    public DateTime ProcessingEndTime { get; set; }
}

public class DocumentSummary
{
    public string SummaryText { get; set; } = string.Empty;
    public List<string> ExtractedSentences { get; set; } = new();
    public List<string> KeyPhrases { get; set; } = new();
    public double ConfidenceScore { get; set; }
}

public class CompressedContent
{
    public string DeduplicatedText { get; set; } = string.Empty;
    public string CoreInformation { get; set; } = string.Empty;
    public double CompressionRatio { get; set; }
    public double QualityScore { get; set; }
}

public class AugmentedContent
{
    public string ContextInformation { get; set; } = string.Empty;
    public List<string> Annotations { get; set; } = new();
    public List<string> References { get; set; } = new();
    public double EnhancementScore { get; set; }
}

public class MultimodalAnalysis
{
    public bool HasTables { get; set; }
    public bool HasImages { get; set; }
    public bool HasDiagrams { get; set; }
    public string LayoutComplexity { get; set; } = string.Empty;
    public double AnalysisScore { get; set; }
}