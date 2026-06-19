namespace FileFlux.Core.Infrastructure.Interop;

/// <summary>
/// Shared resolution for native binary self-update opt-in.
/// Self-update (downloading newer native binaries from GitHub releases at runtime) is OFF by
/// default; reliability and reproducibility come from the NuGet-pinned bundled binary. Consumers
/// who want runtime self-update enable it per loader via <c>AutoUpdateEnabled</c>, or globally via
/// the <see cref="EnvVar"/> environment variable.
/// </summary>
internal static class NativeAutoUpdate
{
    /// <summary>
    /// Environment variable that enables native self-update when set to a truthy value
    /// (1 / true / yes / on, case-insensitive). Used as the default when a loader has no explicit override.
    /// </summary>
    public const string EnvVar = "FILEFLUX_NATIVE_AUTOUPDATE";

    private static readonly string[] Truthy = { "1", "true", "yes", "on" };

    /// <summary>
    /// Returns true when <paramref name="value"/> is a recognized truthy flag (1/true/yes/on,
    /// case-insensitive, surrounding whitespace tolerated). Null/empty/unrecognized -> false.
    /// </summary>
    public static bool ParseEnv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToLowerInvariant();
        return Array.IndexOf(Truthy, normalized) >= 0;
    }

    /// <summary>
    /// Resolves the default self-update state from the <see cref="EnvVar"/> environment variable.
    /// </summary>
    public static bool ResolveDefault()
        => ParseEnv(Environment.GetEnvironmentVariable(EnvVar));
}
