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
    // List를 사용하여 등록 순서 보장 (나중에 등록된 것이 우선순위 높음)
    private readonly List<IDocumentReader> _readers = new();
    private readonly object _lock = new();

    public DocumentReaderFactory()
    {
        // 기본 Reader들 등록 (DI 없이 사용 시)
        RegisterDefaultReaders();
    }

    /// <summary>
    /// DI 컨테이너로부터 Reader들을 주입받는 생성자
    /// </summary>
    public DocumentReaderFactory(IEnumerable<IDocumentReader> readers)
    {
        ArgumentNullException.ThrowIfNull(readers);

        // DI로 주입된 Reader들 등록
        foreach (var reader in readers)
        {
            RegisterReader(reader);
        }
    }

    public IEnumerable<IDocumentReader> GetAvailableReaders()
    {
        lock (_lock)
        {
            return _readers.ToList();
        }
    }

    public IDocumentReader? GetReader(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // 확장자 기반 매칭
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        lock (_lock)
        {
            // 역순으로 검색 (나중에 등록된 Reader가 우선순위 높음 - MultiModal Reader 우선)
            for (int i = _readers.Count - 1; i >= 0; i--)
            {
                var reader = _readers[i];
                if (reader.SupportedExtensions.Contains(extension) && reader.CanRead(fileName))
                {
                    return reader;
                }
            }
        }

        return null;
    }

    public IDocumentReader? GetReader(RawContent rawContent)
    {
        if (rawContent?.File == null)
            return null;

        return GetReader(rawContent.File.Name);
    }

    public void RegisterReader(IDocumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        lock (_lock)
        {
            // 같은 ReaderType이 이미 있으면 제거하고 새로 추가
            _readers.RemoveAll(r => r.ReaderType == reader.ReaderType);
            _readers.Add(reader);
        }
    }

    public bool UnregisterReader(string readerType)
    {
        if (string.IsNullOrWhiteSpace(readerType))
            return false;

        lock (_lock)
        {
            var removed = _readers.RemoveAll(r => r.ReaderType == readerType);
            return removed > 0;
        }
    }

    public IEnumerable<string> GetSupportedExtensions()
    {
        lock (_lock)
        {
            return _readers
                .SelectMany(reader => reader.SupportedExtensions)
                .Distinct()
                .OrderBy(ext => ext)
                .ToList();
        }
    }

    public bool IsExtensionSupported(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        var normalizedExtension = extension.StartsWith(".", StringComparison.Ordinal) ? extension.ToLowerInvariant() : $".{extension.ToLowerInvariant()}";

        lock (_lock)
        {
            return _readers
                .Any(reader => reader.SupportedExtensions.Contains(normalizedExtension));
        }
    }

    public IReadOnlyDictionary<string, string> GetExtensionReaderMapping()
    {
        var mapping = new Dictionary<string, string>();

        lock (_lock)
        {
            // 역순으로 처리하여 나중에 등록된 Reader가 매핑에 남도록
            for (int i = _readers.Count - 1; i >= 0; i--)
            {
                var reader = _readers[i];
                foreach (var extension in reader.SupportedExtensions)
                {
                    if (!mapping.ContainsKey(extension))
                    {
                        mapping[extension] = reader.ReaderType;
                    }
                }
            }
        }

        return mapping;
    }

    private void RegisterDefaultReaders()
    {
        // 텍스트 기반 Reader들
        RegisterReader(new Readers.TextDocumentReader());
        RegisterReader(new Readers.MarkdownDocumentReader());
        RegisterReader(new Readers.HtmlDocumentReader());

        // Office 문서 Reader들 (DocumentFormat.OpenXml 기반)
        RegisterReader(new Readers.WordDocumentReader());
        RegisterReader(new Readers.ExcelDocumentReader());
        RegisterReader(new Readers.PowerPointDocumentReader());

        // PDF Reader (PdfPig 기반)
        RegisterReader(new Readers.PdfDocumentReader());

        // ZIP Archive Reader (다른 Reader들을 재귀적으로 사용)
        RegisterReader(new Readers.ZipArchiveReader(this));
    }
}
