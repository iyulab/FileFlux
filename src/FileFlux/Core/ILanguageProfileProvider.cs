namespace FileFlux.Core;

/// <summary>
/// Provider for language-specific text segmentation profiles.
/// Supports auto-detection and manual language selection.
/// </summary>
public interface ILanguageProfileProvider
{
    /// <summary>
    /// Get language profile by ISO 639-1 code
    /// </summary>
    /// <param name="languageCode">ISO 639-1 language code (e.g., "en", "ko")</param>
    /// <returns>Language profile, or default English profile if not found</returns>
    ILanguageProfile GetProfile(string languageCode);

    /// <summary>
    /// Detect language and return appropriate profile
    /// </summary>
    /// <param name="text">Text sample for language detection</param>
    /// <returns>Detected language profile</returns>
    ILanguageProfile DetectAndGetProfile(string text);

    /// <summary>
    /// Get the default/fallback language profile
    /// </summary>
    ILanguageProfile DefaultProfile { get; }

    /// <summary>
    /// List of supported language codes
    /// </summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Check if a language is supported
    /// </summary>
    /// <param name="languageCode">ISO 639-1 language code</param>
    /// <returns>True if language is supported</returns>
    bool IsSupported(string languageCode);

    /// <summary>
    /// Register a custom language profile
    /// </summary>
    /// <param name="profile">Language profile to register</param>
    void RegisterProfile(ILanguageProfile profile);
}
