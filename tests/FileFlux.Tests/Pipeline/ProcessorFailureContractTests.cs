using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.Infrastructure.Factories;
using FluxCurator.Infrastructure.Chunking;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileFlux.Tests.Pipeline;

/// <summary>
/// What a caller sees when a stage does not finish: its own cancellation as <see cref="OperationCanceledException"/>,
/// FileFlux's exceptions as themselves, and a failed processor that refuses to continue instead of returning an empty
/// result. The processor wrapped all three in <see cref="DocumentProcessingException"/>, and a failed processor skipped
/// every later stage because <see cref="ProcessorState.Failed"/> sorts after them.
/// </summary>
public class ProcessorFailureContractTests
{
    private static IDocumentProcessor Create(string path) =>
        new DocumentProcessorFactory(new DocumentReaderFactory(), new ChunkerFactory(), loggerFactory: NullLoggerFactory.Instance)
            .Create(path);

    private static string TempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task CallerCancels_ThrowsOperationCanceled_AndTheStageRunsAgainLater()
    {
        var path = TempFile("Some text to read.", ".txt");
        try
        {
            using var processor = Create(path);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(cancellationToken: cts.Token));
            Assert.NotEqual(ProcessorState.Failed, processor.State);

            await processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotEmpty(processor.Result.Chunks!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UnsupportedFormat_ArrivesAsItself()
    {
        var path = TempFile("not a known format", ".unknownext");
        try
        {
            using var processor = Create(path);

            await Assert.ThrowsAsync<UnsupportedFileFormatException>(
                () => processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(ProcessorState.Failed, processor.State);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedProcessor_RefusesToContinue_NamingTheStage()
    {
        var path = TempFile("not a known format", ".unknownext");
        try
        {
            using var processor = Create(path);
            await Assert.ThrowsAnyAsync<FileFluxException>(
                () => processor.ProcessAsync(cancellationToken: TestContext.Current.CancellationToken));

            var again = await Assert.ThrowsAsync<InvalidOperationException>(
                () => processor.ChunkAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Contains("Extract", again.Message);
            Assert.Null(processor.Result.Chunks);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
