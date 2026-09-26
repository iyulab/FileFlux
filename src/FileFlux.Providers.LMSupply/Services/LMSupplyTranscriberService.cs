using LMSupply.Transcriber;

namespace FileFlux.Providers.LMSupply.Services;

/// <summary>
/// <see cref="IAudioToTextService"/> over a local LMSupply transcriber (Whisper or Parakeet). The model loads on first
/// use, not at registration, so registering the service costs nothing until an audio file is read.
/// </summary>
public sealed class LMSupplyTranscriberService : IAudioToTextService, IAsyncDisposable, IDisposable
{
    private static readonly string[] s_formats = [".wav", ".mp3"];

    private readonly LMSupplyTranscriberOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITranscriberModel? _model;
    private bool _disposed;

    /// <summary>Creates the service.</summary>
    /// <param name="modelId">Transcriber model id or alias (<c>"default"</c>, <c>"parakeet-tdt"</c>, …).</param>
    /// <param name="language">Language hint (ISO 639-1); null identifies the language from the audio.</param>
    /// <param name="cacheDirectory">Model cache directory; null uses LMSupply's default.</param>
    public LMSupplyTranscriberService(string modelId = "default", string? language = null, string? cacheDirectory = null)
        : this(new LMSupplyTranscriberOptions { ModelId = modelId, Language = language, CacheDirectory = cacheDirectory })
    {
    }

    /// <summary>Creates the service from options (model, language, cache and speaker separation).</summary>
    public LMSupplyTranscriberService(LMSupplyTranscriberOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc/>
    public IEnumerable<string> SupportedAudioFormats => s_formats;

    /// <inheritdoc/>
    public string ProviderName => "LMSupply.Transcriber";

    /// <inheritdoc/>
    public async Task<AudioTranscript> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        var model = await ModelAsync(cancellationToken).ConfigureAwait(false);
        return ToTranscript(await model.TranscribeAsync(audioPath, TranscribeOptions(), cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<AudioTranscript> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken = default)
    {
        // LMSupply 0.79.2 decodes a stream as it decodes a file (MP3 recognised from its first bytes, mixed down and
        // resampled to 16 kHz), so the stream goes straight through — no temporary file.
        var model = await ModelAsync(cancellationToken).ConfigureAwait(false);
        return ToTranscript(await model.TranscribeAsync(audio, TranscribeOptions(), cancellationToken).ConfigureAwait(false));
    }

    private TranscribeOptions TranscribeOptions() => new()
    {
        Language = _options.Language,
        WordTimestamps = true,
        Diarize = _options.Diarize,
        NumSpeakers = _options.NumSpeakers,
        SpeakerThreshold = _options.SpeakerThreshold,
    };

    private static AudioTranscript ToTranscript(TranscriptionResult result) =>
        new(result.Segments.Select(s => new AudioSegment(TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), s.Text) { Speaker = s.Speaker }).ToList())
        {
            Language = result.Language,
            Duration = result.DurationSeconds is { } d ? TimeSpan.FromSeconds(d) : null,
        };

    private async Task<ITranscriberModel> ModelAsync(CancellationToken cancellationToken)
    {
        if (_model is { } ready)
            return ready;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _model ??= await LocalTranscriber.LoadAsync(
                _options.ModelId, new TranscriberOptions { CacheDirectory = _options.CacheDirectory }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    /// <summary>
    /// Disposes synchronously, for a container disposed with <c>Dispose()</c> (which throws on a service that is only
    /// <see cref="IAsyncDisposable"/>). Blocks on <see cref="DisposeAsync"/>.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_model is not null)
            await _model.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
