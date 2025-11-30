namespace FileFlux.Core;

/// <summary>
/// FileFlux base exception class
/// </summary>
public abstract class FileFluxException : Exception
{
    protected FileFluxException() : base() { }
    protected FileFluxException(string message) : base(message) { }
    protected FileFluxException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Unsupported file format exception
/// </summary>
public class UnsupportedFileFormatException : FileFluxException
{
    public string FileName { get; } = string.Empty;

    public UnsupportedFileFormatException() : base("Unsupported file format") { }

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

    public UnsupportedFileFormatException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Document processing exception
/// </summary>
public class DocumentProcessingException : FileFluxException
{
    public string? FileName { get; }

    public DocumentProcessingException() : base("Document processing error") { }

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
