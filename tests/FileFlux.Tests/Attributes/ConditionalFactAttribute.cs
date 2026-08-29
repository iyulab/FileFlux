using System;
using System.Runtime.CompilerServices;
using FileFlux.Tests.Helpers;
using Xunit;

namespace FileFlux.Tests.Attributes;

/// <summary>
/// Fact attribute that only runs when OpenAI API is configured
/// </summary>
public class RequiresApiAttribute : FactAttribute
{
    public RequiresApiAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!EnvLoader.IsOpenAiConfigured())
        {
            Skip = "Requires OpenAI API key in .env.local";
        }
    }
}