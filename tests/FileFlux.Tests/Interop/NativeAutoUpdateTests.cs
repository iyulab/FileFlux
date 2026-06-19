using FileFlux.Core.Infrastructure.Interop;
using FluentAssertions;

namespace FileFlux.Tests.Interop;

/// <summary>
/// Verifies that native binary self-update from GitHub releases is opt-in (default OFF),
/// per ISSUE-FileFlux-20260619-143000-native-autoupdate-optin. Reliability comes from the
/// NuGet-pinned bundled binary; GitHub self-update only runs when explicitly enabled via the
/// static AutoUpdateEnabled flag or the FILEFLUX_NATIVE_AUTOUPDATE environment variable.
/// </summary>
public class NativeAutoUpdateTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("True", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData(" 1 ", true)]   // surrounding whitespace tolerated
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("", false)]
    [InlineData("garbage", false)]
    [InlineData(null, false)]
    public void ParseEnv_ResolvesTruthyValuesOnly(string? value, bool expected)
    {
        NativeAutoUpdate.ParseEnv(value).Should().Be(expected);
    }

    [Fact]
    public void ResolveDefault_WhenEnvUnset_IsFalse()
    {
        var original = Environment.GetEnvironmentVariable(NativeAutoUpdate.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, null);
            NativeAutoUpdate.ResolveDefault().Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, original);
        }
    }

    [Fact]
    public void ResolveDefault_WhenEnvTruthy_IsTrue()
    {
        var original = Environment.GetEnvironmentVariable(NativeAutoUpdate.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, "1");
            NativeAutoUpdate.ResolveDefault().Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, original);
        }
    }

    [Fact]
    public void UndocLoader_AutoUpdateEnabled_DefaultsToFalse_AndExplicitSetWins()
    {
        var original = Environment.GetEnvironmentVariable(NativeAutoUpdate.EnvVar);
        try
        {
            // Env unset -> default OFF
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, null);
            UndocNativeLoader.ResetAutoUpdateOverride();
            UndocNativeLoader.AutoUpdateEnabled.Should().BeFalse();

            // Explicit enable wins even when env would say off
            UndocNativeLoader.AutoUpdateEnabled = true;
            UndocNativeLoader.AutoUpdateEnabled.Should().BeTrue();

            // Explicit disable wins even when env says on
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, "1");
            UndocNativeLoader.AutoUpdateEnabled = false;
            UndocNativeLoader.AutoUpdateEnabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, original);
            UndocNativeLoader.ResetAutoUpdateOverride();
        }
    }

    [Fact]
    public void UnhwpLoader_AutoUpdateEnabled_DefaultsToFalse()
    {
        var original = Environment.GetEnvironmentVariable(NativeAutoUpdate.EnvVar);
        try
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, null);
            UnhwpNativeLoader.ResetAutoUpdateOverride();
            UnhwpNativeLoader.AutoUpdateEnabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(NativeAutoUpdate.EnvVar, original);
            UnhwpNativeLoader.ResetAutoUpdateOverride();
        }
    }
}
