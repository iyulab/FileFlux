namespace FileFlux;

/// <summary>
/// Turns speech in an audio file into timed text — the port FileFlux's audio reader reads through, as image readers
/// read through <see cref="IImageToTextService"/>. The consumer registers an implementation (for a local model:
/// <c>FileFlux.Providers.LMSupply</c>'s <c>AddLMSupplyTranscriber</c>). With none registered, audio files stay
/// unsupported.
/// </summary>
public interface IAudioToTextService
{
    /// <summary>Transcribes an audio file.</summary>
    Task<AudioTranscript> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default);

    /// <summary>Transcribes audio from a stream; <paramref name="fileName"/> names its format by extension.</summary>
    Task<AudioTranscript> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken = default);

    /// <summary>File extensions this service can decode, with the leading dot (e.g. <c>.wav</c>, <c>.mp3</c>).</summary>
    IEnumerable<string> SupportedAudioFormats { get; }

    /// <summary>Name of the implementation (for diagnostics).</summary>
    string ProviderName { get; }
}

/// <summary>The speech in an audio file as timed segments.</summary>
/// <param name="Segments">Segments in time order, with times from the start of the file.</param>
public sealed record AudioTranscript(IReadOnlyList<AudioSegment> Segments)
{
    /// <summary>Language of the speech (ISO 639-1), when the service identified or was told it.</summary>
    public string? Language { get; init; }

    /// <summary>Length of the audio, when known.</summary>
    public TimeSpan? Duration { get; init; }
}

/// <summary>A stretch of speech.</summary>
/// <param name="Start">Start, from the beginning of the file.</param>
/// <param name="End">End, from the beginning of the file.</param>
/// <param name="Text">What was said.</param>
public sealed record AudioSegment(TimeSpan Start, TimeSpan End, string Text)
{
    /// <summary>A label for the voice (e.g. <c>S1</c>) when the service separates speakers; otherwise null.</summary>
    public string? Speaker { get; init; }
}
