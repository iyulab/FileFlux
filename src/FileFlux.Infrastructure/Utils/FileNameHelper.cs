using System.Text;

namespace FileFlux.Infrastructure.Utils;

/// <summary>
/// UTF-8 파일명 처리를 위한 유틸리티 클래스
/// </summary>
public static class FileNameHelper
{
    /// <summary>
    /// 파일명이 올바른 UTF-8 인코딩인지 검증하고 정규화합니다.
    /// </summary>
    /// <param name="fileName">검증할 파일명</param>
    /// <returns>UTF-8로 정규화된 파일명</returns>
    public static string NormalizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        // 이미 올바른 UTF-8 문자열인지 검증
        try
        {
            var bytes = Encoding.UTF8.GetBytes(fileName);
            var decoded = Encoding.UTF8.GetString(bytes);

            // 원본과 동일하면 이미 올바른 UTF-8
            if (string.Equals(fileName, decoded, StringComparison.Ordinal))
                return fileName;

            return decoded;
        }
        catch (EncoderFallbackException)
        {
            // UTF-8 인코딩 실패 시 안전한 ASCII 변환
            return ConvertToSafeFileName(fileName);
        }
    }

    /// <summary>
    /// 안전한 파일명으로 변환 (UTF-8 호환)
    /// </summary>
    /// <param name="fileName">원본 파일명</param>
    /// <returns>안전한 UTF-8 파일명</returns>
    private static string ConvertToSafeFileName(string fileName)
    {
        var safeChars = fileName.Select(c =>
        {
            // ASCII 범위 내 안전한 문자는 유지
            if (c < 128 && char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                return c;

            // 한글, 한자 등 유니코드 문자는 유지
            if (char.IsLetter(c) || char.IsDigit(c))
                return c;

            // 기타 문자는 언더스코어로 변환
            return '_';
        });

        return new string(safeChars.ToArray());
    }

    /// <summary>
    /// 파일 경로에서 UTF-8 안전한 파일명 추출
    /// </summary>
    /// <param name="filePath">파일 경로</param>
    /// <returns>UTF-8 정규화된 파일명</returns>
    public static string GetSafeFileName(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return string.Empty;

        var fileName = Path.GetFileName(filePath);
        return NormalizeFileName(fileName);
    }

    /// <summary>
    /// 파일명에 유효하지 않은 문자가 있는지 확인
    /// </summary>
    /// <param name="fileName">확인할 파일명</param>
    /// <returns>유효하면 true, 무효하면 false</returns>
    public static bool IsValidFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return false;

        var invalidChars = Path.GetInvalidFileNameChars();
        return !fileName.Any(c => invalidChars.Contains(c));
    }

    /// <summary>
    /// UTF-8 메타데이터를 안전하게 추출
    /// </summary>
    /// <param name="fileInfo">파일 정보</param>
    /// <returns>UTF-8 안전한 파일명</returns>
    public static string ExtractSafeFileName(FileInfo fileInfo)
    {
        if (fileInfo == null)
            return string.Empty;

        return NormalizeFileName(fileInfo.Name);
    }
}