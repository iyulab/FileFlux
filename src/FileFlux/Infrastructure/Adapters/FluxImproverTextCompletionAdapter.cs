namespace FileFlux.Infrastructure.Adapters;

using FluxImprover.Services;

/// <summary>
/// Adapter that wraps FileFlux's IDocumentAnalysisService for use with FluxImprover.
/// </summary>
/// <remarks>
/// FluxImprover states each call's sampling and instructions in <see cref="CompletionOptions"/>. What the FileFlux
/// port can carry reaches it: <see cref="CompletionOptions.Temperature"/> and <see cref="CompletionOptions.MaxTokens"/>
/// as <see cref="GenerationSettings"/>, and <see cref="CompletionOptions.SystemPrompt"/> ahead of the prompt (the port
/// takes a single prompt). A call that would otherwise run under the service's default token limit — a summary sized
/// for 512 tokens under a 256-token default — was cut off and reported as truncated.
/// </remarks>
internal sealed class FluxImproverTextCompletionAdapter : FluxImprover.Services.ITextGenerationService
{
    private readonly FileFlux.IDocumentAnalysisService _inner;

    public FluxImproverTextCompletionAdapter(FileFlux.IDocumentAnalysisService inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <inheritdoc />
    public Task<string> CompleteAsync(
        string prompt,
        CompletionOptions? options = null,
        CancellationToken cancellationToken = default)
        => _inner.GenerateAsync(ComposePrompt(prompt, options), ToSettings(options), cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        string prompt,
        CompletionOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // FileFlux's IDocumentAnalysisService doesn't support streaming,
        // so we return the full response as a single chunk
        yield return await CompleteAsync(prompt, options, cancellationToken).ConfigureAwait(false);
    }

    internal static GenerationSettings ToSettings(CompletionOptions? options)
        => options is null ? GenerationSettings.Default : new GenerationSettings(options.Temperature, options.MaxTokens);

    internal static string ComposePrompt(string prompt, CompletionOptions? options)
        => string.IsNullOrWhiteSpace(options?.SystemPrompt) ? prompt : $"{options.SystemPrompt}\n\n{prompt}";
}
