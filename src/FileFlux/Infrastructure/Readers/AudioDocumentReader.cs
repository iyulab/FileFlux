using System.Text;
using FileFlux.Core;

namespace FileFlux.Infrastructure.Readers;

/// <summary>
/// Reads speech from audio files through a registered <see cref="IAudioToTextService"/>. The transcript's segments
/// become the text (one paragraph each, prefixed with the speaker label when the service separates speakers), and each
/// segment's time range becomes a <see cref="SourceSpan"/> — so a chunk says which stretch of the recording it came
/// from (<see cref="SourceLocation.StartTime"/>/<see cref="SourceLocation.EndTime"/>), however the chunker merges
/// segments. Without a registered service the reader reads nothing: audio stays unsupported rather than an empty
/// document.
/// </summary>
public sealed class AudioDocumentReader : IDocumentReader
{
    private static readonly string[] s_audioExtensions = [".wav", ".mp3", ".m4a", ".flac", ".ogg", ".aac", ".wma"];

    private readonly IAudioToTextService? _audioToText;

    /// <summary>Creates the reader over <paramref name="audioToText"/> (null: the reader claims no file).</summary>
    public AudioDocumentReader(IAudioToTextService? audioToText) => _audioToText = audioToText;

    /// <inheritdoc/>
    public IEnumerable<string> SupportedExtensions => _audioToText is null
        ? []
        : s_audioExtensions.Intersect(_audioToText.SupportedAudioFormats.Select(f => f.ToLowerInvariant()));

    /// <inheritdoc/>
    public string ReaderType => "AudioReader";

    /// <inheritdoc/>
    public bool CanRead(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    /// <inheritdoc/>
    public Task<ReadResult> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            throw new FileNotFoundException($"Audio file not found: {filePath}");
        return Task.FromResult(new ReadResult { File = FileInfoOf(info), ReaderType = ReaderType });
    }

    /// <inheritdoc/>
    public Task<ReadResult> ReadAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Task.FromResult(new ReadResult { File = StreamInfoOf(stream, fileName), ReaderType = ReaderType });
    }

    /// <inheritdoc/>
    public async Task<RawContent> ExtractAsync(string filePath, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            throw new FileNotFoundException($"Audio file not found: {filePath}");
        var transcript = await Service().TranscribeAsync(filePath, cancellationToken).ConfigureAwait(false);
        return ToRawContent(transcript, FileInfoOf(info));
    }

    /// <inheritdoc/>
    public async Task<RawContent> ExtractAsync(Stream stream, string fileName, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var file = StreamInfoOf(stream, fileName);
        var transcript = await Service().TranscribeAsync(stream, fileName, cancellationToken).ConfigureAwait(false);
        return ToRawContent(transcript, file);
    }

    private IAudioToTextService Service() => _audioToText
        ?? throw new InvalidOperationException("No IAudioToTextService is registered; audio files are not supported.");

    internal RawContent ToRawContent(AudioTranscript transcript, SourceFileInfo file)
    {
        var text = new StringBuilder();
        var spans = new List<SourceSpan>(transcript.Segments.Count);
        foreach (var segment in transcript.Segments)
        {
            var line = segment.Text.Trim();
            if (line.Length == 0)
                continue;
            if (text.Length > 0)
                text.Append("\n\n");
            var start = text.Length;
            if (segment.Speaker is { Length: > 0 } speaker)
                text.Append(speaker).Append(": ");
            text.Append(line);
            spans.Add(new SourceSpan(start, text.Length) { StartTime = segment.Start, EndTime = segment.End });
        }

        var hints = new Dictionary<string, object>
        {
            ["segment_count"] = spans.Count,
            ["audio_provider"] = _audioToText?.ProviderName ?? "",
        };
        if (transcript.Duration is { } duration)
            hints["duration_seconds"] = duration.TotalSeconds;
        if (transcript.Language is { } language)
            hints["language"] = language;
        if (transcript.Segments.Any(s => s.Speaker is not null))
            hints["speaker_count"] = transcript.Segments.Select(s => s.Speaker).Where(s => s is not null).Distinct().Count();

        return new RawContent
        {
            Text = text.ToString(),
            Spans = spans,
            File = file,
            Hints = hints,
            ReaderType = ReaderType,
            Status = ProcessingStatus.Completed,
        };
    }

    private static SourceFileInfo FileInfoOf(FileInfo info) => new()
    {
        Name = info.Name,
        Extension = info.Extension,
        Size = info.Length,
        CreatedAt = info.CreationTimeUtc,
        ModifiedAt = info.LastWriteTimeUtc,
    };

    private static SourceFileInfo StreamInfoOf(Stream stream, string fileName) => new()
    {
        Name = fileName,
        Extension = Path.GetExtension(fileName),
        Size = stream.CanSeek ? stream.Length : 0,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
    };
}
