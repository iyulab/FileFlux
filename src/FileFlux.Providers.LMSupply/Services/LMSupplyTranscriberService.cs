using LMSupply.Transcriber;

namespace FileFlux.Providers.LMSupply.Services;

/// <summary>
/// <see cref="IAudioToTextService"/> over a local LMSupply transcriber (Whisper or Parakeet). The model loads on first
/// use, not at registration, so registering the service costs nothing until an audio file is read.
/// </summary>
public sealed class LMSupplyTranscriberService : IAudioToTextService, IAsyncDisposable
{
    private static readonly string[] s_formats = [".wav", ".mp3"];

    private readonly string _modelId;
    private readonly string? _language;
    private readonly string? _cacheDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ITranscriberModel? _model;

    /// <summary>Creates the service.</summary>
    /// <param name="modelId">Transcriber model id or alias (<c>"default"</c>, <c>"parakeet-tdt"</c>, …).</param>
    /// <param name="language">Language hint (ISO 639-1); null identifies the language from the audio.</param>
    /// <param name="cacheDirectory">Model cache directory; null uses LMSupply's default.</param>
    public LMSupplyTranscriberService(string modelId = "default", string? language = null, string? cacheDirectory = null)
    {
        _modelId = modelId;
        _language = language;
        _cacheDirectory = cacheDirectory;
    }

    /// <inheritdoc/>
    public IEnumerable<string> SupportedAudioFormats => s_formats;

    /// <inheritdoc/>
    public string ProviderName => "LMSupply.Transcriber";

    /// <inheritdoc/>
    public async Task<AudioTranscript> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        var model = await ModelAsync(cancellationToken).ConfigureAwait(false);
        var result = await model.TranscribeAsync(audioPath, new TranscribeOptions { Language = _language, WordTimestamps = true }, cancellationToken)
            .ConfigureAwait(false);
        return new AudioTranscript(
            result.Segments.Select(s => new AudioSegment(TimeSpan.FromSeconds(s.Start), TimeSpan.FromSeconds(s.End), s.Text)).ToList())
        {
            Language = result.Language,
            Duration = result.DurationSeconds is { } d ? TimeSpan.FromSeconds(d) : null,
        };
    }

    /// <inheritdoc/>
    public async Task<AudioTranscript> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken = default)
    {
        // LMSupply decodes MP3 from a file path only; a temporary file keeps every supported format working.
        var temp = Path.Combine(Path.GetTempPath(), $"fileflux-audio-{Guid.NewGuid():N}{Path.GetExtension(fileName)}");
        try
        {
            await using (var file = File.Create(temp))
                await audio.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            return await TranscribeAsync(temp, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private async Task<ITranscriberModel> ModelAsync(CancellationToken cancellationToken)
    {
        if (_model is { } ready)
            return ready;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _model ??= await LocalTranscriber.LoadAsync(
                _modelId, new TranscriberOptions { CacheDirectory = _cacheDirectory }, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_model is not null)
            await _model.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
