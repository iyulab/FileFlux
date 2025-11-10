namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// 이미지 처리 관련 공통 상수
/// </summary>
public static class ImageProcessingConstants
{
    /// <summary>
    /// 최소 이미지 너비 (픽셀)
    /// 이보다 작은 이미지는 장식용(아이콘, 로고)으로 간주하여 제외
    /// </summary>
    public const int MinImageWidth = 100;

    /// <summary>
    /// 최소 이미지 높이 (픽셀)
    /// 이보다 작은 이미지는 장식용(아이콘, 로고)으로 간주하여 제외
    /// </summary>
    public const int MinImageHeight = 100;

    /// <summary>
    /// 최소 이미지 면적 (평방 픽셀)
    /// 추가적인 필터링 기준으로 사용 가능
    /// </summary>
    public const int MinImageArea = MinImageWidth * MinImageHeight; // 10,000 px²

    /// <summary>
    /// 이미지 크기가 처리 임계값을 만족하는지 확인
    /// </summary>
    /// <param name="width">이미지 너비 (픽셀)</param>
    /// <param name="height">이미지 높이 (픽셀)</param>
    /// <returns>처리해야 할 이미지인 경우 true</returns>
    public static bool ShouldProcessImage(int width, int height)
    {
        return width >= MinImageWidth && height >= MinImageHeight;
    }

    /// <summary>
    /// 이미지가 장식용인지 판단
    /// </summary>
    /// <param name="width">이미지 너비 (픽셀)</param>
    /// <param name="height">이미지 높이 (픽셀)</param>
    /// <returns>장식용 이미지인 경우 true</returns>
    public static bool IsDecorativeImage(int width, int height)
    {
        return !ShouldProcessImage(width, height);
    }
}
