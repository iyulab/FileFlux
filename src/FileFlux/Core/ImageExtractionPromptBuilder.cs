using FileFlux.Domain;

namespace FileFlux;

/// <summary>
/// 이미지 텍스트 추출을 위한 프롬프트 빌더
/// 모든 이미지 추출 시나리오(PPTX, DOCX, XLSX, PDF)에서 일관된 프롬프트 생성
/// </summary>
public static class ImageExtractionPromptBuilder
{
    /// <summary>
    /// 이미지 텍스트 추출용 프롬프트를 생성합니다.
    /// CustomPrompt가 설정되어 있으면 해당 프롬프트를 사용하고, 없으면 기본 프롬프트를 생성합니다.
    /// </summary>
    /// <param name="options">텍스트 추출 옵션</param>
    /// <returns>생성된 프롬프트 문자열</returns>
    public static string BuildPrompt(ImageToTextOptions? options = null)
    {
        options ??= new ImageToTextOptions();

        // CustomPrompt가 설정되어 있으면 그것을 사용
        if (!string.IsNullOrWhiteSpace(options.CustomPrompt))
        {
            return options.CustomPrompt;
        }

        // 기본 프롬프트 생성
        return BuildDefaultPrompt(options);
    }

    /// <summary>
    /// 기본 프롬프트를 생성합니다.
    /// </summary>
    private static string BuildDefaultPrompt(ImageToTextOptions options)
    {
        var prompt = "Analyze this image for text extraction and visual description.\n\n";

        prompt += "SUCCESS CRITERIA:\n";
        prompt += "1. If readable text exists (OCR): Extract all visible text content\n";
        prompt += "2. If no readable text: Describe the visual content (objects, scenes, activities)\n";
        prompt += "Return the extracted text or description directly.\n\n";

        prompt += "FAILURE CRITERIA:\n";
        prompt += "If extraction is IMPOSSIBLE (blurry, too low resolution, corrupted), respond with:\n";
        prompt += "EXTRACTION_FAILED: [brief reason why extraction failed]\n\n";

        if (options.ExtractStructure)
        {
            prompt += "For text extraction: Preserve structure and layout (tables, lists, headings).\n";
        }

        if (!string.IsNullOrWhiteSpace(options.ImageTypeHint))
        {
            prompt += options.ImageTypeHint switch
            {
                "chart" => "Image type: Chart/Graph - extract titles, labels, data values, or describe trends.\n",
                "table" => "Image type: Table - extract headers and cell values in structured format.\n",
                "document" => "Image type: Document - extract all text while preserving formatting.\n",
                "diagram" => "Image type: Diagram - extract text labels and describe visual relationships.\n",
                _ => ""
            };
        }

        if (options.Quality == "high")
        {
            prompt += "Quality requirement: Provide detailed and accurate extraction with high precision.\n";
        }

        prompt += "\nIMPORTANT: Return content directly without introductory phrases like 'This image shows' or 'The text says'.";

        return prompt;
    }
}
