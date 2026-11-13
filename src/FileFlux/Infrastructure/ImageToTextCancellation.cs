using FileFlux.Core;

namespace FileFlux.Infrastructure;

/// <summary>
/// An image-to-text adapter that turns every exception into <see cref="ImageToTextResult.ErrorMessage"/> (so a bad picture
/// does not throw) turns the caller's cancellation into one too. Every caller that reads the result checks it here, so a
/// call nobody waits for any more is a cancellation and not a failed read or an image that quietly got no text.
/// </summary>
internal static class ImageToTextCancellation
{
    /// <summary>Throws <see cref="OperationCanceledException"/> when <paramref name="result"/> is an error answer and the token is cancelled.</summary>
    internal static void ThrowIfCancelledBehindError(ImageToTextResult result, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(result.ErrorMessage))
            cancellationToken.ThrowIfCancellationRequested();
    }
}
