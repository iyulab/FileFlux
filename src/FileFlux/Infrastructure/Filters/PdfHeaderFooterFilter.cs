using System.Text.RegularExpressions;

namespace FileFlux.Infrastructure.Filters;

/// <summary>
/// Detects and removes repetitive header/footer patterns from PDF extracted text.
/// This filter identifies lines that appear on multiple pages and removes them
/// to reduce noise in RAG chunking and improve embedding quality.
/// </summary>
/// <remarks>
/// IMPORTANT: This filter uses heuristic-based pattern detection.
/// - Enable only when confident that repetitive text is truly header/footer noise
/// - Some documents may have intentionally repeated content (e.g., legal disclaimers)
/// - Always provide configuration options to users
/// </remarks>
public class PdfHeaderFooterFilter
{
    /// <summary>
    /// Configuration options for the filter.
    /// </summary>
    public class Options
    {
        /// <summary>
        /// Enable header/footer detection and removal. Default: false (opt-in).
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Minimum ratio of pages a line must appear on to be considered header/footer.
        /// Range: 0.0-1.0. Default: 0.5 (50% of pages).
        /// </summary>
        public double RepetitionThreshold { get; set; } = 0.5;

        /// <summary>
        /// Minimum number of pages required to apply filtering.
        /// Prevents false positives on short documents. Default: 3.
        /// </summary>
        public int MinPageCount { get; set; } = 3;

        /// <summary>
        /// Maximum line length to consider as header/footer candidate.
        /// Longer lines are less likely to be headers. Default: 200 characters.
        /// </summary>
        public int MaxLineLength { get; set; } = 200;

        /// <summary>
        /// Patterns to always preserve (never treat as header/footer).
        /// Regex patterns matched against normalized lines.
        /// </summary>
        public List<string> PreservePatterns { get; set; } = new();

        /// <summary>
        /// Additional patterns to always remove (in addition to detected patterns).
        /// Regex patterns matched against normalized lines.
        /// </summary>
        public List<string> RemovePatterns { get; set; } = new();
    }

    private readonly Options _options;

    /// <summary>
    /// Creates a new PdfHeaderFooterFilter with default options.
    /// </summary>
    public PdfHeaderFooterFilter() : this(new Options()) { }

    /// <summary>
    /// Creates a new PdfHeaderFooterFilter with custom options.
    /// </summary>
    public PdfHeaderFooterFilter(Options options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Filters repetitive header/footer content from the given text.
    /// </summary>
    /// <param name="content">The extracted PDF content.</param>
    /// <param name="pageCount">The number of pages in the original document.</param>
    /// <returns>Filtered content with header/footer lines removed.</returns>
    public string Filter(string content, int pageCount)
    {
        if (!_options.Enabled)
            return content;

        if (string.IsNullOrWhiteSpace(content))
            return content;

        if (pageCount < _options.MinPageCount)
            return content;

        var lines = content.Split('\n');
        var lineFrequency = new Dictionary<string, int>();
        var normalizedToOriginal = new Dictionary<string, string>();

        // Count frequency of each normalized line
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.Length > _options.MaxLineLength)
                continue;

            var normalized = NormalizeLine(line);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            lineFrequency[normalized] = lineFrequency.GetValueOrDefault(normalized) + 1;

            if (!normalizedToOriginal.ContainsKey(normalized))
                normalizedToOriginal[normalized] = line;
        }

        // Determine threshold count
        var thresholdCount = Math.Max(2, (int)(pageCount * _options.RepetitionThreshold));

        // Identify header/footer patterns
        var headerFooterPatterns = new HashSet<string>();
        foreach (var (normalized, count) in lineFrequency)
        {
            if (count >= thresholdCount)
            {
                // Check if this pattern should be preserved
                if (!ShouldPreserve(normalized))
                {
                    headerFooterPatterns.Add(normalized);
                }
            }
        }

        // Add explicitly configured remove patterns
        var additionalRemovePatterns = CompilePatterns(_options.RemovePatterns);

        // Filter lines
        var filteredLines = new List<string>();
        foreach (var line in lines)
        {
            var normalized = NormalizeLine(line);

            // Keep empty lines for formatting
            if (string.IsNullOrWhiteSpace(normalized))
            {
                filteredLines.Add(line);
                continue;
            }

            // Remove if matches detected header/footer pattern
            if (headerFooterPatterns.Contains(normalized))
                continue;

            // Remove if matches configured remove patterns
            if (MatchesAnyPattern(line, additionalRemovePatterns))
                continue;

            filteredLines.Add(line);
        }

        return string.Join('\n', filteredLines);
    }

    /// <summary>
    /// Analyzes content and returns detected header/footer patterns.
    /// Useful for debugging or allowing users to review detected patterns.
    /// </summary>
    public List<DetectedPattern> AnalyzePatterns(string content, int pageCount)
    {
        var patterns = new List<DetectedPattern>();

        if (string.IsNullOrWhiteSpace(content) || pageCount < 2)
            return patterns;

        var lines = content.Split('\n');
        var lineFrequency = new Dictionary<string, int>();
        var normalizedToOriginal = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length > _options.MaxLineLength)
                continue;

            var normalized = NormalizeLine(line);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            lineFrequency[normalized] = lineFrequency.GetValueOrDefault(normalized) + 1;
            if (!normalizedToOriginal.ContainsKey(normalized))
                normalizedToOriginal[normalized] = line.Trim();
        }

        foreach (var (normalized, count) in lineFrequency.OrderByDescending(kv => kv.Value))
        {
            if (count >= 2) // At least appears twice
            {
                var ratio = (double)count / pageCount;
                patterns.Add(new DetectedPattern
                {
                    OriginalText = normalizedToOriginal.GetValueOrDefault(normalized, normalized),
                    NormalizedText = normalized,
                    Occurrences = count,
                    Ratio = ratio,
                    WouldBeFiltered = ratio >= _options.RepetitionThreshold && !ShouldPreserve(normalized)
                });
            }
        }

        return patterns;
    }

    /// <summary>
    /// Normalizes a line for comparison by removing variable parts like page numbers.
    /// </summary>
    private static string NormalizeLine(string line)
    {
        var normalized = line.Trim();

        // Replace page number patterns with placeholder
        normalized = Regex.Replace(normalized, @"Page\s*\d+", "PAGE_NUM", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\d+\s*페이지", "PAGE_NUM");
        normalized = Regex.Replace(normalized, @"페이지\s*\d+", "PAGE_NUM");
        normalized = Regex.Replace(normalized, @"第\s*\d+\s*页", "PAGE_NUM"); // Chinese
        normalized = Regex.Replace(normalized, @"Seite\s*\d+", "PAGE_NUM", RegexOptions.IgnoreCase); // German
        normalized = Regex.Replace(normalized, @"\b\d+\s*/\s*\d+\b", "PAGE_NUM"); // e.g., "5/10"

        // Replace dates with placeholder
        normalized = Regex.Replace(normalized, @"\d{4}[-/]\d{1,2}[-/]\d{1,2}", "DATE");
        normalized = Regex.Replace(normalized, @"\d{1,2}[-/]\d{1,2}[-/]\d{4}", "DATE");

        // Normalize whitespace
        normalized = Regex.Replace(normalized, @"\s+", " ");

        return normalized;
    }

    private bool ShouldPreserve(string normalizedLine)
    {
        if (_options.PreservePatterns.Count == 0)
            return false;

        var preservePatterns = CompilePatterns(_options.PreservePatterns);
        return MatchesAnyPattern(normalizedLine, preservePatterns);
    }

    private static List<Regex> CompilePatterns(List<string> patterns)
    {
        var compiled = new List<Regex>();
        foreach (var pattern in patterns)
        {
            try
            {
                compiled.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern, skip
            }
        }
        return compiled;
    }

    private static bool MatchesAnyPattern(string text, List<Regex> patterns)
    {
        return patterns.Any(p => p.IsMatch(text));
    }

    /// <summary>
    /// Represents a detected repetitive pattern in the document.
    /// </summary>
    public class DetectedPattern
    {
        /// <summary>
        /// The original text of the pattern.
        /// </summary>
        public string OriginalText { get; set; } = string.Empty;

        /// <summary>
        /// The normalized text used for matching.
        /// </summary>
        public string NormalizedText { get; set; } = string.Empty;

        /// <summary>
        /// Number of times this pattern appears.
        /// </summary>
        public int Occurrences { get; set; }

        /// <summary>
        /// Ratio of occurrences to page count.
        /// </summary>
        public double Ratio { get; set; }

        /// <summary>
        /// Whether this pattern would be filtered with current settings.
        /// </summary>
        public bool WouldBeFiltered { get; set; }
    }
}
