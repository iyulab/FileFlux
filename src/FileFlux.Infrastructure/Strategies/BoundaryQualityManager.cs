using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Boundary Quality 일관성 개선을 위한 품질 관리자
/// Phase 9 평가 결과: Boundary Quality 14-77% (63% 변동) → 80%+ 일관성 목표
/// </summary>
public class BoundaryQualityManager
{
    private static readonly Regex SectionHeaderRegex = new(@"^#{1,6}\s+.+|^[A-Z\s]+:?\s*$", RegexOptions.Compiled);
    private static readonly Regex ListItemRegex = new(@"^\s*[-*+•]\s+|^\s*\d+\.\s+", RegexOptions.Compiled);
    private static readonly Regex TableRowRegex = new(@"\|.*\|", RegexOptions.Compiled);
    private static readonly Regex CodeBlockRegex = new(@"```|^    ", RegexOptions.Compiled);
    
    /// <summary>
    /// 청크 경계의 품질을 평가하고 개선된 분할점을 제안
    /// </summary>
    public BoundaryQualityResult EvaluateAndImproveBoundary(string text, int proposedSplitPosition, ChunkingOptions options)
    {
        var lines = text.Split('\n');
        var lineIndex = GetLineIndexFromPosition(text, proposedSplitPosition);
        
        // 1. 현재 위치의 품질 평가
        var currentQuality = EvaluateBoundaryQuality(lines, lineIndex);
        
        // 2. 품질이 낮으면 개선된 위치 탐색
        if (currentQuality.QualityScore < 0.7) // 70% 미만은 개선 필요
        {
            var improvedPosition = FindImprovedBoundary(lines, lineIndex, options);
            if (improvedPosition != lineIndex)
            {
                var improvedQuality = EvaluateBoundaryQuality(lines, improvedPosition);
                if (improvedQuality.QualityScore > currentQuality.QualityScore)
                {
                    return new BoundaryQualityResult
                    {
                        OriginalPosition = proposedSplitPosition,
                        ImprovedPosition = GetPositionFromLineIndex(text, improvedPosition),
                        QualityScore = improvedQuality.QualityScore,
                        BoundaryType = improvedQuality.BoundaryType,
                        Confidence = improvedQuality.Confidence,
                        ReasonForImprovement = improvedQuality.ReasonForImprovement
                    };
                }
            }
        }
        
        // 개선이 불가능하거나 불필요한 경우 원래 위치 유지
        return new BoundaryQualityResult
        {
            OriginalPosition = proposedSplitPosition,
            ImprovedPosition = proposedSplitPosition,
            QualityScore = currentQuality.QualityScore,
            BoundaryType = currentQuality.BoundaryType,
            Confidence = currentQuality.Confidence,
            ReasonForImprovement = "No improvement needed or possible"
        };
    }
    
    /// <summary>
    /// 특정 라인에서의 경계 품질 평가
    /// </summary>
    private BoundaryQualityEvaluation EvaluateBoundaryQuality(string[] lines, int lineIndex)
    {
        if (lineIndex <= 0 || lineIndex >= lines.Length - 1)
        {
            return new BoundaryQualityEvaluation
            {
                QualityScore = 0.3,
                BoundaryType = BoundaryType.Poor,
                Confidence = 0.9,
                ReasonForImprovement = "Boundary at document edge"
            };
        }
        
        var currentLine = lines[lineIndex].Trim();
        var previousLine = lines[lineIndex - 1].Trim();
        var nextLine = lines[lineIndex + 1].Trim();
        
        // 1. 구조적 경계 탐지 (최고 품질)
        if (IsStructuralBoundary(currentLine, previousLine, nextLine))
        {
            return new BoundaryQualityEvaluation
            {
                QualityScore = 0.95,
                BoundaryType = BoundaryType.Structural,
                Confidence = 0.9,
                ReasonForImprovement = "Natural structural boundary"
            };
        }
        
        // 2. 의미적 경계 탐지 (높은 품질)
        if (IsSemanticBoundary(currentLine, previousLine, nextLine))
        {
            return new BoundaryQualityEvaluation
            {
                QualityScore = 0.85,
                BoundaryType = BoundaryType.Semantic,
                Confidence = 0.8,
                ReasonForImprovement = "Semantic topic transition"
            };
        }
        
        // 3. 문단 경계 탐지 (중간 품질)
        if (IsParagraphBoundary(currentLine, previousLine, nextLine))
        {
            return new BoundaryQualityEvaluation
            {
                QualityScore = 0.75,
                BoundaryType = BoundaryType.Paragraph,
                Confidence = 0.7,
                ReasonForImprovement = "Natural paragraph break"
            };
        }
        
        // 4. 문장 경계 탐지 (기본 품질)
        if (IsSentenceBoundary(currentLine, previousLine))
        {
            return new BoundaryQualityEvaluation
            {
                QualityScore = 0.65,
                BoundaryType = BoundaryType.Sentence,
                Confidence = 0.6,
                ReasonForImprovement = "Sentence completion"
            };
        }
        
        // 5. 임의 분할 (낮은 품질)
        return new BoundaryQualityEvaluation
        {
            QualityScore = 0.3,
            BoundaryType = BoundaryType.Arbitrary,
            Confidence = 0.5,
            ReasonForImprovement = "Mid-sentence or mid-thought split"
        };
    }
    
    /// <summary>
    /// 개선된 경계 위치 탐색
    /// </summary>
    private int FindImprovedBoundary(string[] lines, int currentLineIndex, ChunkingOptions options)
    {
        var searchRadius = Math.Min(10, lines.Length / 10); // 최대 10줄 또는 전체의 10% 내에서 탐색
        
        var bestLineIndex = currentLineIndex;
        var bestQuality = EvaluateBoundaryQuality(lines, currentLineIndex);
        
        // 위아래로 탐색하여 최적 위치 찾기
        for (int offset = 1; offset <= searchRadius; offset++)
        {
            // 위쪽 탐색
            if (currentLineIndex - offset > 0)
            {
                var upwardQuality = EvaluateBoundaryQuality(lines, currentLineIndex - offset);
                if (upwardQuality.QualityScore > bestQuality.QualityScore)
                {
                    bestQuality = upwardQuality;
                    bestLineIndex = currentLineIndex - offset;
                }
            }
            
            // 아래쪽 탐색
            if (currentLineIndex + offset < lines.Length - 1)
            {
                var downwardQuality = EvaluateBoundaryQuality(lines, currentLineIndex + offset);
                if (downwardQuality.QualityScore > bestQuality.QualityScore)
                {
                    bestQuality = downwardQuality;
                    bestLineIndex = currentLineIndex + offset;
                }
            }
        }
        
        return bestLineIndex;
    }
    
    /// <summary>
    /// 구조적 경계 탐지 (섹션 헤더, 테이블, 코드 블록 등)
    /// </summary>
    private bool IsStructuralBoundary(string currentLine, string previousLine, string nextLine)
    {
        // 섹션 헤더 앞뒤
        if (SectionHeaderRegex.IsMatch(currentLine) || SectionHeaderRegex.IsMatch(nextLine))
            return true;
            
        // 테이블 시작/끝
        if (TableRowRegex.IsMatch(currentLine) != TableRowRegex.IsMatch(nextLine))
            return true;
            
        // 코드 블록 시작/끝
        if (CodeBlockRegex.IsMatch(currentLine) && !CodeBlockRegex.IsMatch(nextLine))
            return true;
            
        // 리스트 항목 그룹 사이
        if (ListItemRegex.IsMatch(previousLine) && !ListItemRegex.IsMatch(currentLine) && !string.IsNullOrWhiteSpace(nextLine))
            return true;
            
        return false;
    }
    
    /// <summary>
    /// 의미적 경계 탐지 (주제 전환, 키워드 변화 등)
    /// </summary>
    private bool IsSemanticBoundary(string currentLine, string previousLine, string nextLine)
    {
        // 빈 줄로 구분된 의미적 전환
        if (string.IsNullOrWhiteSpace(currentLine) && 
            !string.IsNullOrWhiteSpace(previousLine) && 
            !string.IsNullOrWhiteSpace(nextLine))
        {
            var prevKeywords = ExtractKeywords(previousLine);
            var nextKeywords = ExtractKeywords(nextLine);
            var commonKeywords = prevKeywords.Intersect(nextKeywords).Count();
            var totalKeywords = prevKeywords.Union(nextKeywords).Count();
            
            // 공통 키워드가 30% 미만이면 주제 전환으로 판단
            return totalKeywords > 0 && (double)commonKeywords / totalKeywords < 0.3;
        }
        
        return false;
    }
    
    /// <summary>
    /// 문단 경계 탐지
    /// </summary>
    private bool IsParagraphBoundary(string currentLine, string previousLine, string nextLine)
    {
        // 빈 줄이 문단 구분자 역할
        return string.IsNullOrWhiteSpace(currentLine) && 
               !string.IsNullOrWhiteSpace(previousLine) && 
               !string.IsNullOrWhiteSpace(nextLine);
    }
    
    /// <summary>
    /// 문장 경계 탐지
    /// </summary>
    private bool IsSentenceBoundary(string currentLine, string previousLine)
    {
        if (string.IsNullOrWhiteSpace(previousLine))
            return false;
            
        // 문장 끝 기호로 끝나는지 확인
        return previousLine.TrimEnd().EndsWith('.') || 
               previousLine.TrimEnd().EndsWith('!') || 
               previousLine.TrimEnd().EndsWith('?') ||
               previousLine.TrimEnd().EndsWith('。'); // 한국어 마침표
    }
    
    /// <summary>
    /// 텍스트에서 키워드 추출
    /// </summary>
    private HashSet<string> ExtractKeywords(string text)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = text.Split(new[] { ' ', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}' }, 
            StringSplitOptions.RemoveEmptyEntries);
            
        foreach (var word in words)
        {
            var cleanWord = word.Trim().ToLower();
            if (cleanWord.Length >= 3) // 3글자 이상만 키워드로 간주
            {
                keywords.Add(cleanWord);
            }
        }
        
        return keywords;
    }
    
    /// <summary>
    /// 텍스트 위치에서 라인 인덱스 계산
    /// </summary>
    private int GetLineIndexFromPosition(string text, int position)
    {
        if (position <= 0) return 0;
        if (position >= text.Length) return text.Split('\n').Length - 1;
        
        return text.Substring(0, position).Count(c => c == '\n');
    }
    
    /// <summary>
    /// 라인 인덱스에서 텍스트 위치 계산
    /// </summary>
    private int GetPositionFromLineIndex(string text, int lineIndex)
    {
        var lines = text.Split('\n');
        if (lineIndex <= 0) return 0;
        if (lineIndex >= lines.Length) return text.Length;
        
        var position = 0;
        for (int i = 0; i < lineIndex; i++)
        {
            position += lines[i].Length + 1; // +1 for newline
        }
        
        return position;
    }
}

/// <summary>
/// 경계 품질 평가 결과
/// </summary>
public class BoundaryQualityResult
{
    public int OriginalPosition { get; set; }
    public int ImprovedPosition { get; set; }
    public double QualityScore { get; set; }
    public BoundaryType BoundaryType { get; set; }
    public double Confidence { get; set; }
    public string ReasonForImprovement { get; set; } = string.Empty;
}

/// <summary>
/// 내부 품질 평가 결과
/// </summary>
internal class BoundaryQualityEvaluation
{
    public double QualityScore { get; set; }
    public BoundaryType BoundaryType { get; set; }
    public double Confidence { get; set; }
    public string ReasonForImprovement { get; set; } = string.Empty;
}

/// <summary>
/// 경계 유형 분류
/// </summary>
public enum BoundaryType
{
    Structural,  // 구조적 경계 (헤더, 테이블 등)
    Semantic,    // 의미적 경계 (주제 전환)
    Paragraph,   // 문단 경계
    Sentence,    // 문장 경계
    Arbitrary,   // 임의 분할
    Poor         // 품질 낮음
}