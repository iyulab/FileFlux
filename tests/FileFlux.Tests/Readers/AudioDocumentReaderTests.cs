using FileFlux.Core;
using FileFlux.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;

namespace FileFlux.Tests.Readers;

/// <summary>
/// Audio reads through a registered <see cref="IAudioToTextService"/>: segments become text, their times become
/// source spans, and chunks carry the time range they cover. Without a service, audio stays unsupported.
/// </summary>
public sealed class AudioDocumentReaderTests : IDisposable
{
    private readonly string _wav = Path.Combine(Path.GetTempPath(), $"audio-reader-{Guid.NewGuid():N}.wav");

    public AudioDocumentReaderTests() => File.WriteAllBytes(_wav, [0x52, 0x49, 0x46, 0x46]);

    public void Dispose() => File.Delete(_wav);

    private sealed class FakeTranscriber : IAudioToTextService
    {
        public IEnumerable<string> SupportedAudioFormats => [".wav", ".mp3"];
        public string ProviderName => "fake";

        public Task<AudioTranscript> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AudioTranscript(
            [
                new AudioSegment(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(4), "Welcome to the quarterly review.") { Speaker = "S1" },
                new AudioSegment(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9), "Revenue grew in every region.") { Speaker = "S2" },
                new AudioSegment(TimeSpan.FromSeconds(31), TimeSpan.FromSeconds(36), "Next quarter we hire two engineers.") { Speaker = "S1" },
            ])
            { Language = "en", Duration = TimeSpan.FromSeconds(40) });

        public Task<AudioTranscript> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken = default) =>
            TranscribeAsync(fileName, cancellationToken);
    }

    [Fact]
    public async Task Extract_TurnsSegmentsIntoTextAndTimedSpans()
    {
        var reader = new AudioDocumentReader(new FakeTranscriber());

        var raw = await reader.ExtractAsync(_wav, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("S1: Welcome to the quarterly review.\n\nS2: Revenue grew in every region.\n\nS1: Next quarter we hire two engineers.", raw.Text);
        Assert.Equal(3, raw.Spans.Count);
        Assert.Equal(TimeSpan.FromSeconds(31), raw.Spans[2].StartTime);
        Assert.StartsWith("S1: Next quarter", raw.Text[raw.Spans[2].Start..raw.Spans[2].End], StringComparison.Ordinal);
        Assert.Equal(2, raw.Hints["speaker_count"]);
    }

    [Fact]
    public void WithoutAService_AudioIsUnsupported()
    {
        var services = new ServiceCollection();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IDocumentReaderFactory>().CanRead("meeting.wav"));
    }

    [Fact]
    public async Task ProcessedChunks_CarryTheTimeRangeTheyCover()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAudioToTextService, FakeTranscriber>();
        services.AddFileFlux();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDocumentProcessorFactory>();

        using var processor = factory.Create(_wav);
        await processor.ChunkAsync(cancellationToken: TestContext.Current.CancellationToken);

        var chunks = processor.Result.Chunks!;
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotNull(c.Location.StartTime));
        Assert.Equal(TimeSpan.Zero, chunks.Min(c => c.Location.StartTime));
        Assert.Equal(TimeSpan.FromSeconds(36), chunks.Max(c => c.Location.EndTime));
        Assert.Contains("hire two engineers", string.Concat(chunks.Select(c => c.Content)), StringComparison.Ordinal);
    }
}

/// <summary>
/// With the LMSupply transcriber registered, a real 54-second recording becomes chunks whose time ranges are absolute
/// across the file — segments of the second 30-second window start after 30 s, and the chunks span the whole recording.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AudioDocumentReaderIntegrationTests
{
    private const string RecordingUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/0-four-speakers-zh.wav";

    [Fact]
    public async Task RealTranscription_ChunksCarryAbsoluteTimes()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "fileflux-fixtures", "0-four-speakers-zh.wav");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var http = new HttpClient();
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(RecordingUrl, ct), ct);
        }

        var services = new ServiceCollection();
        FileFlux.Providers.LMSupply.Extensions.ServiceCollectionExtensions.AddLMSupplyTranscriber(services, language: "zh");
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();

        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);
        await processor.ChunkAsync(new ChunkingOptions { MaxChunkSize = 200 }, ct);

        var chunks = processor.Result.Chunks!;
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotNull(c.Location.StartTime));
        // The short transcript fits one chunk; the spans it covers show the times are absolute across windows.
        Assert.Contains(processor.Result.Refined!.Spans, s => s.StartTime > TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.Zero, chunks.Min(c => c.Location.StartTime));
        Assert.True(chunks.Max(c => c.Location.EndTime) > TimeSpan.FromSeconds(50));
        Assert.True(chunks.Max(c => c.Location.EndTime) <= TimeSpan.FromSeconds(58));
    }
    // Diarize on the same four-speaker recording: the document text reads as who said what — every passage opens with
    // an S<n> label from the transcriber, and NumSpeakers = 4 yields exactly four distinct labels.
    [Fact]
    public async Task RealTranscription_WithDiarize_LabelsEachPassageWithItsSpeaker()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "fileflux-fixtures", "0-four-speakers-zh.wav");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var http = new HttpClient();
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(RecordingUrl, ct), ct);
        }

        var services = new ServiceCollection();
        FileFlux.Providers.LMSupply.Extensions.ServiceCollectionExtensions.AddLMSupplyTranscriber(
            services, language: "zh", configure: o => { o.Diarize = true; o.NumSpeakers = 4; });
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();

        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);
        await processor.ChunkAsync(new ChunkingOptions { MaxChunkSize = 200 }, ct);

        var passages = processor.Result.Refined!.Text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(passages);
        Assert.All(passages, p => Assert.Matches(@"^S\d+: ", p));
        var labels = passages.Select(p => p[..p.IndexOf(':')]).Distinct().ToList();
        Assert.Equal(4, labels.Count);
    }
    // The same recording handed over as a stream (processor Create(stream, ".wav")). The provider passes the stream to
    // LMSupply directly (0.79.2 decodes streams like files), and the chunks carry the same absolute times.
    [Fact]
    public async Task RealTranscription_FromAStream_ChunksCarryAbsoluteTimes()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), "fileflux-fixtures", "0-four-speakers-zh.wav");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var http = new HttpClient();
            await File.WriteAllBytesAsync(path, await http.GetByteArrayAsync(RecordingUrl, ct), ct);
        }

        var services = new ServiceCollection();
        FileFlux.Providers.LMSupply.Extensions.ServiceCollectionExtensions.AddLMSupplyTranscriber(services, language: "zh");
        services.AddFileFlux();
        await using var provider = services.BuildServiceProvider();

        await using var stream = File.OpenRead(path);
        using var processor = provider.GetRequiredService<IDocumentProcessorFactory>().Create(stream, ".wav");
        await processor.ChunkAsync(new ChunkingOptions { MaxChunkSize = 200 }, ct);

        var chunks = processor.Result.Chunks!;
        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.NotNull(c.Location.StartTime));
        Assert.True(chunks.Max(c => c.Location.EndTime) > TimeSpan.FromSeconds(50));
        Assert.True(chunks.Max(c => c.Location.EndTime) <= TimeSpan.FromSeconds(58));
    }
}
