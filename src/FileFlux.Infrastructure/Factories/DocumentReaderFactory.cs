using FileFlux;
using FileFlux.Domain;
using System.Collections.Concurrent;

namespace FileFlux.Infrastructure.Factories;

/// <summary>
/// Document Reader 팩토리 구현체
/// 파일 형식에 따라 적절한 Reader를 제공
/// </summary>
public class DocumentReaderFactory : IDocumentReaderFactory
{
    private readonly ConcurrentDictionary<string, IDocumentReader> _readers = new();

    public DocumentReaderFactory()
    {
        // 기본 Reader들 등록
        RegisterDefaultReaders();
    }

    public IEnumerable<IDocumentReader> GetAvailableReaders()
    {
        return _readers.Values.ToList();
    }

    public IDocumentReader? GetReader(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // 확장자 기반 매칭
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return _readers.Values
            .FirstOrDefault(reader => reader.SupportedExtensions.Contains(extension) && reader.CanRead(fileName));
    }

    public IDocumentReader? GetReader(RawDocumentContent rawContent)
    {
        if (rawContent?.FileInfo == null)
            return null;

        return GetReader(rawContent.FileInfo.FileName);
    }

    public void RegisterReader(IDocumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _readers.AddOrUpdate(reader.ReaderType, reader, (key, existingReader) => reader);
    }

    public bool UnregisterReader(string readerType)
    {
        if (string.IsNullOrWhiteSpace(readerType))
            return false;

        return _readers.TryRemove(readerType, out _);
    }

    private void RegisterDefaultReaders()
    {
        // 텍스트 기반 Reader들
        RegisterReader(new Readers.TextDocumentReader());
        
        // Office 문서 Reader들 (DocumentFormat.OpenXml 기반)
        RegisterReader(new Readers.WordDocumentReader());
        RegisterReader(new Readers.ExcelDocumentReader());
        RegisterReader(new Readers.PowerPointDocumentReader());
        
        // PDF Reader (PdfPig 기반)
        RegisterReader(new Readers.PdfDocumentReader());
    }
}