using System.CommandLine;

namespace FileFlux.CLI.Commands;

/// <summary>
/// The <c>--format</c>/<c>-f</c> option of a command that writes only some formats. A value outside them is a parse
/// error that names the allowed values, so the command fails before it reads the input or writes anything; a known value
/// is accepted in any case and handed to the command in lower case.
/// </summary>
internal static class FormatOption
{
    public static Option<string> Create(string defaultFormat, params string[] allowed)
    {
        var list = string.Join(", ", allowed);
        return new Option<string>("--format", "-f")
        {
            Description = $"Output format: {list}",
            DefaultValueFactory = _ => defaultFormat,
            CustomParser = result =>
            {
                if (result.Tokens.Count == 0)
                    return defaultFormat;

                var value = result.Tokens[0].Value;
                var format = allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));
                if (format is null)
                    result.AddError($"Unknown output format '{value}'. This command writes: {list}.");
                return format;
            }
        };
    }
}
