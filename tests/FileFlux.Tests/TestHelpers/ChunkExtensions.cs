using FileFlux.Domain;

namespace FileFlux.Tests.TestHelpers;

/// <summary>
/// Extension methods for backward compatibility with Phase 7 property changes
/// </summary>
public static class ChunkExtensions
{
    public static string? GetContextualHeader(this DocumentChunk chunk)
    {
        return chunk.Props.TryGetValue("ContextualHeader", out var value)
            ? value?.ToString()
            : null;
    }

    public static string? GetDocumentDomain(this DocumentChunk chunk)
    {
        return chunk.Props.TryGetValue("Domain", out var value)
            ? value?.ToString()
            : null;
    }

    public static List<string> GetTechnicalKeywords(this DocumentChunk chunk)
    {
        if (chunk.Props.TryGetValue("TechnicalKeywords", out var value) && value is List<string> keywords)
        {
            return keywords;
        }
        return new List<string>();
    }

    public static string? GetStructuralRole(this DocumentChunk chunk)
    {
        return chunk.Props.TryGetValue("StructuralRole", out var value)
            ? value?.ToString()
            : null;
    }

    public static string? GetContentType(this DocumentChunk chunk)
    {
        return chunk.Props.TryGetValue("ContentType", out var value)
            ? value?.ToString()
            : null;
    }

    public static Dictionary<string, double> GetContextualScores(this DocumentChunk chunk)
    {
        if (chunk.Props.TryGetValue("ContextualScores", out var value) && value is Dictionary<string, double> scores)
        {
            return scores;
        }
        return new Dictionary<string, double>();
    }

    // Legacy property names for backward compatibility in tests
    public static string? ContextualHeader(this DocumentChunk chunk) => chunk.GetContextualHeader();
    public static string? DocumentDomain(this DocumentChunk chunk) => chunk.GetDocumentDomain();
    public static List<string> TechnicalKeywords(this DocumentChunk chunk) => chunk.GetTechnicalKeywords();
    public static string? StructuralRole(this DocumentChunk chunk) => chunk.GetStructuralRole();
    public static string? ContentType(this DocumentChunk chunk) => chunk.GetContentType();
    public static Dictionary<string, double> ContextualScores(this DocumentChunk chunk) => chunk.GetContextualScores();

    // Additional legacy properties
    public static int ChunkIndex(this DocumentChunk chunk) => chunk.Index;
    public static int EstimatedTokens(this DocumentChunk chunk) => chunk.Tokens;

    // Quality score properties (chunk.Quality is now double directly)
    public static double QualityScore(this DocumentChunk chunk) => chunk.Quality;
    public static double RelevanceScore(this DocumentChunk chunk)
    {
        if (chunk.Props.TryGetValue("RelevanceScore", out var value) && value is double score)
        {
            return score;
        }
        return 0.0;
    }
}
