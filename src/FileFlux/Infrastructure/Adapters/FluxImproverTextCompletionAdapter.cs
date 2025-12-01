namespace FileFlux.Infrastructure.Adapters;

using FluxImprover.Services;

/// <summary>
/// Adapter that wraps FileFlux's ITextCompletionService for use with FluxImprover.
/// </summary>
internal sealed class FluxImproverTextCompletionAdapter : FluxImprover.Services.ITextCompletionService
{
    private readonly FileFlux.ITextCompletionService _inner;

    public FluxImproverTextCompletionAdapter(FileFlux.ITextCompletionService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string prompt,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return await _inner.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        CompletionOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // FileFlux's ITextCompletionService doesn't support streaming,
        // so we return the full response as a single chunk
        var result = await _inner.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        yield return result;
    }
}
