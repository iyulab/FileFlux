namespace FileFlux.Core.Exceptions;

/// <summary>
/// FileFlux 기본 예외 클래스
/// </summary>
public abstract class FileFluxException : Exception
{
    protected FileFluxException(string message) : base(message) { }
    protected FileFluxException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 지원하지 않는 파일 형식 예외
/// </summary>
public class UnsupportedFileFormatException : FileFluxException
{
    public string FileName { get; }

    public UnsupportedFileFormatException(string fileName)
        : base($"Unsupported file format: {fileName}")
    {
        FileName = fileName;
    }

    public UnsupportedFileFormatException(string fileName, string message)
        : base(message)
    {
        FileName = fileName;
    }
}

/// <summary>
/// 문서 처리 중 발생하는 예외
/// </summary>
public class DocumentProcessingException : FileFluxException
{
    public string? FileName { get; }

    public DocumentProcessingException(string message) : base(message) { }

    public DocumentProcessingException(string message, Exception innerException)
        : base(message, innerException) { }

    public DocumentProcessingException(string fileName, string message)
        : base(message)
    {
        FileName = fileName;
    }

    public DocumentProcessingException(string fileName, string message, Exception innerException)
        : base(message, innerException)
    {
        FileName = fileName;
    }
}

/// <summary>
/// 청킹 전략 관련 예외
/// </summary>
public class ChunkingStrategyException : FileFluxException
{
    public string? StrategyName { get; }

    public ChunkingStrategyException(string message) : base(message) { }

    public ChunkingStrategyException(string message, Exception innerException)
        : base(message, innerException) { }

    public ChunkingStrategyException(string strategyName, string message)
        : base(message)
    {
        StrategyName = strategyName;
    }
}