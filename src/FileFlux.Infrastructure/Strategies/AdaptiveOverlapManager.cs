using FileFlux.Domain;
using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Strategies;

/// <summary>
/// Context Preservation 강화를 위한 적응형 오버랩 관리자
/// Phase 9 평가 결과: Context Preservation 37-52% → 75% 목표
/// </summary>
public class AdaptiveOverlapManager
{
    private static readonly Regex SentenceEndRegex = new(@"[.!?]+(?:\s|$)", RegexOptions.Compiled);
    private static readonly Regex ParagraphBoundaryRegex = new(@"\n\s*\n+", RegexOptions.Compiled);
    private static readonly Regex ImportantKeywordRegex = new(@"\b(중요|핵심|요약|결론|참고|주의|경고|important|key|summary|conclusion|note|warning|attention)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    /// <summary>
    /// 두 청크 간의 최적 오버랩 크기를 계산
    /// </summary>
    public int CalculateOptimalOverlap(string previousChunk, string currentChunk, ChunkingOptions options)
    {
        var baseOverlapSize = options.OverlapSize;
        var maxOverlapSize = Math.Min(baseOverlapSize * 3, options.MaxChunkSize / 4); // 최대 청크 크기의 1/4
        
        // 1. 문장 경계 기반 오버랩 조정
        var sentenceBoundaryOverlap = CalculateSentenceBoundaryOverlap(previousChunk, baseOverlapSize);
        
        // 2. 의미적 연속성 평가
        var semanticContinuityBonus = CalculateSemanticContinuityBonus(previousChunk, currentChunk, baseOverlapSize);
        
        // 3. 중요 키워드 존재 시 오버랩 증가
        var importantContentBonus = CalculateImportantContentBonus(previousChunk, baseOverlapSize);
        
        var adaptiveOverlapSize = Math.Min(
            sentenceBoundaryOverlap + semanticContinuityBonus + importantContentBonus,
            maxOverlapSize);
            
        return Math.Max(adaptiveOverlapSize, baseOverlapSize); // 최소 기본 크기는 보장
    }
    
    /// <summary>
    /// 문장 경계를 보존하는 오버랩 생성
    /// </summary>
    public string CreateContextPreservingOverlap(string previousChunk, int optimalOverlapSize)
    {
        if (string.IsNullOrEmpty(previousChunk) || optimalOverlapSize <= 0)
            return string.Empty;
            
        // 문장 단위로 오버랩 생성하여 문장 중단 방지
        var sentences = SplitIntoSentences(previousChunk);
        var overlap = BuildOverlapFromSentences(sentences, optimalOverlapSize);
        
        return overlap;
    }
    
    /// <summary>
    /// 두 청크 간의 의미적 연결성 검증
    /// </summary>
    public double ValidateContextPreservation(string overlapText, string previousChunk, string currentChunk)
    {
        if (string.IsNullOrEmpty(overlapText))
            return 0.0;
            
        // 1. 오버랩이 이전 청크 끝부분과 일치하는지 검증
        var endMatch = ValidateEndMatch(overlapText, previousChunk);
        
        // 2. 오버랩이 현재 청크 시작부분과 일치하는지 검증
        var startMatch = ValidateStartMatch(overlapText, currentChunk);
        
        // 3. 오버랩 내 문장 완결성 검증
        var sentenceCompleteness = ValidateSentenceCompleteness(overlapText);
        
        // 4. 전체 Context Preservation 점수 계산
        return (endMatch * 0.4 + startMatch * 0.4 + sentenceCompleteness * 0.2);
    }
    
    private int CalculateSentenceBoundaryOverlap(string previousChunk, int baseOverlapSize)
    {
        if (string.IsNullOrEmpty(previousChunk))
            return baseOverlapSize;
            
        // 마지막 완전한 문장들을 포함하도록 오버랩 크기 조정
        var lastSentenceEnd = FindLastCompleteSentence(previousChunk, baseOverlapSize);
        
        return Math.Max(lastSentenceEnd, baseOverlapSize);
    }
    
    private int CalculateSemanticContinuityBonus(string previousChunk, string currentChunk, int baseOverlapSize)
    {
        // 청크 간 주제 연결성이 높으면 오버랩 증가
        var previousKeywords = ExtractKeywords(previousChunk);
        var currentKeywords = ExtractKeywords(currentChunk);
        
        var sharedKeywords = previousKeywords.Intersect(currentKeywords).Count();
        var totalKeywords = previousKeywords.Union(currentKeywords).Count();
        
        var continuityRatio = totalKeywords > 0 ? (double)sharedKeywords / totalKeywords : 0;
        
        // 연결성이 높으면 오버랩 20-50% 증가
        return (int)(baseOverlapSize * continuityRatio * 0.5);
    }
    
    private int CalculateImportantContentBonus(string previousChunk, int baseOverlapSize)
    {
        // 중요 키워드가 포함된 경우 오버랩 증가
        var importantMatches = ImportantKeywordRegex.Matches(previousChunk);
        
        if (importantMatches.Count > 0)
        {
            return (int)(baseOverlapSize * 0.3); // 30% 증가
        }
        
        return 0;
    }
    
    private int FindLastCompleteSentence(string text, int targetSize)
    {
        var endPosition = Math.Min(text.Length, targetSize);
        
        // 목표 크기 근처에서 마지막 완전한 문장 찾기
        for (int i = endPosition; i >= targetSize / 2; i--)
        {
            if (i < text.Length && SentenceEndRegex.IsMatch(text.Substring(i - 1, 1)))
            {
                return i;
            }
        }
        
        return targetSize; // 문장을 찾지 못하면 기본 크기 반환
    }
    
    private List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var matches = SentenceEndRegex.Matches(text);
        int start = 0;
        
        foreach (Match match in matches)
        {
            var sentence = text.Substring(start, match.Index + match.Length - start).Trim();
            if (!string.IsNullOrWhiteSpace(sentence))
            {
                sentences.Add(sentence);
            }
            start = match.Index + match.Length;
        }
        
        // 마지막 문장 처리
        if (start < text.Length)
        {
            var lastSentence = text.Substring(start).Trim();
            if (!string.IsNullOrWhiteSpace(lastSentence))
            {
                sentences.Add(lastSentence);
            }
        }
        
        return sentences;
    }
    
    private string BuildOverlapFromSentences(List<string> sentences, int targetSize)
    {
        var overlap = new List<string>();
        int currentSize = 0;
        
        // 뒤에서부터 문장을 추가하여 목표 크기에 맞춤
        for (int i = sentences.Count - 1; i >= 0; i--)
        {
            var sentence = sentences[i];
            if (currentSize + sentence.Length <= targetSize)
            {
                overlap.Insert(0, sentence);
                currentSize += sentence.Length;
            }
            else
            {
                break;
            }
        }
        
        return string.Join(" ", overlap);
    }
    
    private double ValidateEndMatch(string overlap, string previousChunk)
    {
        if (string.IsNullOrEmpty(overlap) || string.IsNullOrEmpty(previousChunk))
            return 0.0;
            
        // 오버랩이 이전 청크의 끝부분과 얼마나 일치하는지 계산
        var endPortion = previousChunk.Length >= overlap.Length 
            ? previousChunk.Substring(previousChunk.Length - overlap.Length)
            : previousChunk;
            
        return CalculateTextSimilarity(overlap, endPortion);
    }
    
    private double ValidateStartMatch(string overlap, string currentChunk)
    {
        if (string.IsNullOrEmpty(overlap) || string.IsNullOrEmpty(currentChunk))
            return 0.0;
            
        // 오버랩이 현재 청크의 시작부분과 얼마나 일치하는지 계산
        var startPortion = currentChunk.Length >= overlap.Length 
            ? currentChunk.Substring(0, overlap.Length)
            : currentChunk;
            
        return CalculateTextSimilarity(overlap, startPortion);
    }
    
    private double ValidateSentenceCompleteness(string overlapText)
    {
        if (string.IsNullOrEmpty(overlapText))
            return 0.0;
            
        var sentences = SplitIntoSentences(overlapText);
        var completeSentences = sentences.Count(s => SentenceEndRegex.IsMatch(s));
        
        return sentences.Count > 0 ? (double)completeSentences / sentences.Count : 0.0;
    }
    
    private double CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0.0;
            
        // 간단한 문자열 유사도 계산 (Levenshtein distance 기반)
        var longer = text1.Length > text2.Length ? text1 : text2;
        var shorter = text1.Length > text2.Length ? text2 : text1;
        
        if (longer.Length == 0)
            return 1.0;
            
        var editDistance = CalculateLevenshteinDistance(shorter, longer);
        return (longer.Length - editDistance) / (double)longer.Length;
    }
    
    private int CalculateLevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];
        
        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;
            
        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;
            
        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }
        
        return matrix[s1.Length, s2.Length];
    }
    
    private HashSet<string> ExtractKeywords(string text)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // 단순한 키워드 추출 (공백 기준 분할 후 길이 4 이상)
        var words = text.Split(new[] { ' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries);
            
        foreach (var word in words)
        {
            if (word.Length >= 4)
            {
                keywords.Add(word.Trim().ToLower());
            }
        }
        
        return keywords;
    }
}