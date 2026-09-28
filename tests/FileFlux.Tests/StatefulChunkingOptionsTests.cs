using FileFlux.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileFlux.Tests;

/// <summary>
/// <see cref="ChunkingOptions.LanguageCode"/> picks the segmentation profile. The legacy processor passed it to FluxCurator;
/// the stateful pipeline — the one the README documents — did not, so every document was segmented with the auto-detected
/// profile whatever the caller set.
/// </summary>
public sealed class StatefulChunkingOptionsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"fileflux-lang-{Guid.NewGuid():N}.md");
    private readonly ServiceProvider _provider;

    public StatefulChunkingOptionsTests()
    {
        File.WriteAllText(_path,
            "# Heading\n\nThis is an English paragraph about renewable energy and the grid.\n\nAnother English paragraph follows here.\n");
        var services = new ServiceCollection();
        services.AddFileFlux();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        File.Delete(_path);
    }

    private async Task<IReadOnlyList<DocumentChunk>> ChunkWith(string? languageCode)
    {
        await using var processor = _provider.GetRequiredService<IDocumentProcessorFactory>().Create(_path);
        await processor.ProcessAsync(
            new ProcessingOptions { Chunking = new ChunkingOptions { LanguageCode = languageCode } },
            TestContext.Current.CancellationToken);
        return processor.Result.ToList();
    }

    [Fact]
    public async Task LanguageCode_SetByTheCaller_SelectsTheSegmentationProfile()
    {
        var chunks = await ChunkWith("ko");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal("ko", c.SourceInfo.Language));
    }

    [Fact]
    public async Task LanguageCode_ReachesTheStreamingPathToo()
    {
        await using var processor = _provider.GetRequiredService<IDocumentProcessorFactory>().Create(_path);
        var chunks = new List<DocumentChunk>();
        await foreach (var chunk in processor.ChunkStreamAsync(
                           new ChunkingOptions { LanguageCode = "ko" }, TestContext.Current.CancellationToken))
            chunks.Add(chunk);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal("ko", c.SourceInfo.Language));
    }

    [Fact]
    public async Task KoreanDocument_ReportsKorean()
    {
        // Every stateful chunk used to report the SourceMetadataInfo default "en", whatever the document's language.
        var path = Path.Combine(Path.GetTempPath(), $"fileflux-ko-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(path,
            "# 제목\n\n재생 에너지와 전력망에 관한 한국어 문단입니다. 태양광 발전은 빛을 전기로 바꿉니다.\n\n두 번째 문단은 저장 장치에 관한 내용입니다.\n",
            TestContext.Current.CancellationToken);
        try
        {
            await using var processor = _provider.GetRequiredService<IDocumentProcessorFactory>().Create(path);
            await processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEmpty(processor.Result);
            Assert.All(processor.Result, c => Assert.Equal("ko", c.SourceInfo.Language));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task StreamedChunks_CarryTheirSource()
    {
        // The streaming conversion set no SourceInfo at all — no source id, file path or title.
        await using var processor = _provider.GetRequiredService<IDocumentProcessorFactory>().Create(_path);
        await foreach (var chunk in processor.ChunkStreamAsync(cancellationToken: TestContext.Current.CancellationToken))
        {
            Assert.Equal(_path, chunk.SourceInfo.FilePath);
            Assert.False(string.IsNullOrEmpty(chunk.SourceInfo.SourceId));
        }
    }

    [Fact]
    public async Task LanguageCode_Auto_DetectsTheLanguage()
    {
        // Control: without an explicit code the profile is detected — English here — so the fact above is not passing
        // on a pipeline that reports "ko" for everything.
        var chunks = await ChunkWith("auto");

        Assert.NotEmpty(chunks);
        Assert.All(chunks, c => Assert.Equal("en", c.SourceInfo.Language));
    }
}
