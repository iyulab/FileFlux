namespace FileFlux.Core;

/// <summary>
/// Document processing stage enumeration
/// </summary>
public enum ProcessingStage
{
    /// <summary>Document reading started</summary>
    Reading,

    /// <summary>Text extraction in progress</summary>
    Extracting,

    /// <summary>Parsing in progress</summary>
    Parsing,

    /// <summary>Chunking in progress</summary>
    Chunking,

    /// <summary>Validation in progress</summary>
    Validating,

    /// <summary>Completed</summary>
    Completed,

    /// <summary>Error occurred</summary>
    Error
}

/// <summary>
/// Document processing progress information
/// </summary>
public class ProcessingProgress
{
    /// <summary>
    /// File path
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Current processing stage
    /// </summary>
    public ProcessingStage Stage { get; set; }

    /// <summary>
    /// Overall progress (0.0 - 1.0)
    /// </summary>
    public double OverallProgress { get; set; }

    /// <summary>
    /// Current stage progress (0.0 - 1.0)
    /// </summary>
    public double StageProgress { get; set; }

    /// <summary>
    /// Progress message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Processed bytes
    /// </summary>
    public long ProcessedBytes { get; set; }

    /// <summary>
    /// Total bytes
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Processed chunks count (used in chunking stage)
    /// </summary>
    public int ProcessedChunks { get; set; }

    /// <summary>
    /// Estimated chunks count (used in chunking stage)
    /// </summary>
    public int EstimatedChunks { get; set; }

    /// <summary>
    /// Processing start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Current time
    /// </summary>
    public DateTime CurrentTime { get; set; }

    /// <summary>
    /// Estimated completion time (nullable)
    /// </summary>
    public DateTime? EstimatedCompletion { get; set; }

    /// <summary>
    /// Error information (when error occurs)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Elapsed time
    /// </summary>
    public TimeSpan ElapsedTime => CurrentTime - StartTime;

    /// <summary>
    /// Returns progress as percentage string
    /// </summary>
    public string ProgressPercentage => $"{OverallProgress:P1}";

    /// <summary>
    /// Static methods for creating progress information
    /// </summary>
    public static class Factory
    {
        /// <summary>
        /// Creates new progress information
        /// </summary>
        public static ProcessingProgress Create(string filePath, ProcessingStage stage, double progress, string message = "")
        {
            return new ProcessingProgress
            {
                FilePath = filePath,
                Stage = stage,
                OverallProgress = progress,
                StageProgress = progress,
                Message = message,
                StartTime = DateTime.UtcNow,
                CurrentTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates error progress information
        /// </summary>
        public static ProcessingProgress CreateError(string filePath, string errorMessage)
        {
            return new ProcessingProgress
            {
                FilePath = filePath,
                Stage = ProcessingStage.Error,
                OverallProgress = 0.0,
                Message = "Error during processing",
                ErrorMessage = errorMessage,
                StartTime = DateTime.UtcNow,
                CurrentTime = DateTime.UtcNow
            };
        }
    }
}
