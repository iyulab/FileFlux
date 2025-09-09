using System;
using FileFlux.Tests.Helpers;
using Xunit;

namespace FileFlux.Tests.Attributes;

/// <summary>
/// Fact attribute that only runs when OpenAI API is configured
/// </summary>
public class RequiresApiAttribute : FactAttribute
{
    public RequiresApiAttribute()
    {
        if (!EnvLoader.IsOpenAiConfigured())
        {
            Skip = "Requires OpenAI API key in .env.local";
        }
    }
}