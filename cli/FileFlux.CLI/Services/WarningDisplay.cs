using Spectre.Console;

namespace FileFlux.CLI.Services;

/// <summary>Shows the notes an extraction carries (<c>RawContent.Warnings</c> and the zero-text note) under the result.</summary>
internal static class WarningDisplay
{
    public static void Write(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        foreach (var warning in warnings)
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
        }
    }
}
