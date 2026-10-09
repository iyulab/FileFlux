namespace FileFlux.CLI;

/// <summary>
/// Process exit codes returned by every command.
/// </summary>
internal static class ExitCodes
{
    /// <summary>The command completed.</summary>
    public const int Success = 0;

    /// <summary>The command failed: missing input, missing configuration, or an error while processing.</summary>
    public const int Failure = 1;
}
