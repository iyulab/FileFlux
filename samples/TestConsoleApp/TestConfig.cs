using FileFlux.Domain;

namespace TestConsoleApp;

/// <summary>
/// 테스트용 청킹 설정 - Phase 15 개선사항 적용
/// </summary>
public static class TestConfig
{
    /// <summary>
    /// 개선된 청킹 옵션 - 더 작은 청크, 더 나은 품질
    /// </summary>
    public static ChunkingOptions GetImprovedOptions()
    {
        var options = new ChunkingOptions
        {
            Strategy = ChunkingStrategies.Auto,
            MaxChunkSize = 600,          // 기존 1024 → 600으로 감소
            OverlapSize = 50            // 기존 128 → 50으로 감소 (더 작은 청크에 맞게)
        };

        // 구조화 문서 처리 강화를 위한 커스텀 속성
        options.CustomProperties["MinCompleteness"] = 0.8;           // 완성도 기준 강화
        options.CustomProperties["PreserveSentences"] = true;        // 문장 경계 보존
        options.CustomProperties["SmartOverlap"] = true;             // 스마트 오버랩
        options.CustomProperties["EnableStructureDetection"] = true; // 구조 감지 활성화
        options.CustomProperties["SectionAware"] = true;             // 섹션 인식
        options.CustomProperties["NumberedSectionHandling"] = true;  // 번호 체계 처리
        options.CustomProperties["TargetQualityScore"] = 0.75;       // 목표 품질 점수

        return options;
    }

    /// <summary>
    /// 기존 설정 (비교용)
    /// </summary>
    public static ChunkingOptions GetOriginalOptions()
    {
        return new ChunkingOptions
        {
            Strategy = ChunkingStrategies.Auto,
            MaxChunkSize = 1024,
            OverlapSize = 128
        };
    }
}