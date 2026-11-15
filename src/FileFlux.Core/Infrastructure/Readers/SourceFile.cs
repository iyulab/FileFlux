namespace FileFlux.Core.Infrastructure.Readers;

/// <summary>
/// Opens a source document for reading without denying anyone else the file. A document is most often asked about
/// while the program that edits it still has it open, and on Windows an editor holds its file open for writing:
/// <see cref="File.ReadAllBytes(string)"/>, <see cref="File.OpenRead(string)"/> and a <see cref="FileStream"/> that
/// says <see cref="FileShare.Read"/> all deny writers and are refused with «being used by another process». A
/// reader never needs to deny anyone, so every read of a source path goes through here.
/// </summary>
internal static class SourceFile
{
    /// <summary>Everything another process may do to the file while it is read (it may write it, and replace or delete it).</summary>
    public const FileShare Sharing = FileShare.ReadWrite | FileShare.Delete;

    /// <summary>Opens <paramref name="path"/> for reading, shared for read, write and delete.</summary>
    public static FileStream Open(string path, int bufferSize = 4096, FileOptions options = FileOptions.None) =>
        new(path, FileMode.Open, FileAccess.Read, Sharing, bufferSize, options);

    /// <summary>As <see cref="File.OpenRead(string)"/>, shared for read, write and delete.</summary>
    public static FileStream OpenRead(string path) => Open(path);

    /// <summary>As <see cref="File.ReadAllBytes(string)"/>, shared for read, write and delete.</summary>
    public static byte[] ReadAllBytes(string path)
    {
        using var stream = Open(path);
        using var buffer = new MemoryStream(CapacityOf(stream));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>As <see cref="File.ReadAllBytesAsync(string, CancellationToken)"/>, shared for read, write and delete.</summary>
    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = Open(path, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var buffer = new MemoryStream(CapacityOf(stream));
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    // The length is a hint: a file an editor is writing may grow or shrink while it is read, and the copy goes on to its end.
    private static int CapacityOf(FileStream stream) => (int)Math.Min(stream.Length, Array.MaxLength);
}
