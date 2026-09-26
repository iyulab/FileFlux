namespace FileFlux.Providers.LMSupply.Services;

/// <summary>Options for <see cref="LMSupplyTranscriberService"/>.</summary>
public sealed class LMSupplyTranscriberOptions
{
    /// <summary>Transcriber model id or alias (<c>"default"</c>, <c>"parakeet-tdt"</c>, …).</summary>
    public string ModelId { get; set; } = "default";

    /// <summary>Language hint (ISO 639-1); null identifies the language from the audio.</summary>
    public string? Language { get; set; }

    /// <summary>Model cache directory; null uses LMSupply's default.</summary>
    public string? CacheDirectory { get; set; }

    /// <summary>
    /// Labels each segment with its speaker (<see cref="AudioSegment.Speaker"/>), so a recording of several people reads
    /// as who said what. Downloads a speaker segmentation and a speaker embedding model on first use and adds a pass over
    /// the audio. Off by default.
    /// </summary>
    public bool Diarize { get; set; }

    /// <summary>
    /// The number of speakers, when known — the most reliable way to separate similar voices. Only read with
    /// <see cref="Diarize"/>; null lets the voices decide (see <see cref="SpeakerThreshold"/>).
    /// </summary>
    public int? NumSpeakers { get; set; }

    /// <summary>
    /// How different two voices must be to count as two speakers when <see cref="NumSpeakers"/> is not set; null uses
    /// LMSupply's default. Only read with <see cref="Diarize"/>.
    /// </summary>
    public float? SpeakerThreshold { get; set; }
}
