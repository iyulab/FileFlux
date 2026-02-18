using FileFlux.Core;
using FileFlux.Domain;
using FileFlux.Infrastructure.Output;
using FileFlux.Infrastructure.Services;
using System.Text;
using System.Globalization;

namespace FileFlux.Infrastructure;

/// <summary>
/// FluxDocumentProcessor - Output API implementations for CLI commands.
/// </summary>
public sealed partial class FluxDocumentProcessor
{
    /// <summary>
    /// Extract document and write to output directory.
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <param name="outputOptions">Output options</param>
    /// <param name="imageToTextService">Optional AI image analysis service</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extraction result with output directory</returns>
    public async Task<ExtractionResult> ExtractToDirectoryAsync(
        string filePath,
        OutputOptions? outputOptions = null,
        IImageToTextService? imageToTextService = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);
        outputOptions ??= new OutputOptions();

        // Determine output directory
        var outputDirectory = outputOptions.OutputDirectory
            ?? OutputOptions.GetDefaultOutputDirectory(filePath, "extract");

        Directory.CreateDirectory(outputDirectory);

        // Extract raw content
        var rawContent = await ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);

        // Parse document structure
        var parsedContent = await ParseAsync(rawContent, null, cancellationToken).ConfigureAwait(false);

        // Process images
        var processedText = parsedContent.Text;

        // Convert tables to markdown and append if present
        if (rawContent.HasTables)
        {
            var tableMarkdown = ConvertTablesToMarkdown(rawContent.Tables);
            if (!string.IsNullOrEmpty(tableMarkdown))
            {
                processedText = processedText + "\n\n## Tables\n\n" + tableMarkdown;
            }
        }

        var images = new List<ProcessedImage>();
        var skippedCount = 0;

        // Use explicit ImagesDirectory if provided, otherwise default to outputDirectory/images
        var imagesDir = outputOptions.ImagesDirectory ?? Path.Combine(outputDirectory, "images");

        if (outputOptions.ExtractImages)
        {
            var imageProcessor = new ImageProcessor(outputOptions);

            if (outputOptions.Verbose)
            {
                Console.WriteLine($"[Verbose] RawContent.Images.Count = {rawContent.Images.Count}");
                Console.WriteLine($"[Verbose] Images with Data: {rawContent.Images.Count(i => i.Data != null)}");
                Console.WriteLine($"[Verbose] Images without Data (external URLs): {rawContent.Images.Count(i => i.Data == null)}");
            }

            // Check if images were pre-extracted by Reader (e.g., HTML with embedded base64)
            if (rawContent.Images.Count > 0 && rawContent.Images.Any(i => i.Data != null))
            {
                if (outputOptions.Verbose)
                {
                    Console.WriteLine($"[Verbose] Using ProcessPreExtractedImagesAsync for {rawContent.Images.Count(i => i.Data != null)} pre-extracted images");
                }

                var imageResult = await imageProcessor.ProcessPreExtractedImagesAsync(
                    parsedContent.Text, rawContent.Images, imagesDir, imageToTextService, cancellationToken).ConfigureAwait(false);

                processedText = imageResult.ProcessedContent;
                images = imageResult.Images;
                skippedCount = imageResult.SkippedCount;
            }
            else
            {
                if (outputOptions.Verbose)
                {
                    Console.WriteLine($"[Verbose] Using ProcessImagesAsync (inline base64 fallback)");
                }

                // Fallback to inline base64 processing (for other document types)
                var imageResult = await imageProcessor.ProcessImagesAsync(
                    parsedContent.Text, imagesDir, imageToTextService, cancellationToken).ConfigureAwait(false);

                processedText = imageResult.ProcessedContent;
                images = imageResult.Images;
                skippedCount = imageResult.SkippedCount;
            }
        }
        else
        {
            processedText = ImageProcessor.RemoveBase64Images(parsedContent.Text);
        }

        var result = new ExtractionResult
        {
            ParsedContent = parsedContent,
            ProcessedText = processedText,
            Images = images,
            SkippedImageCount = skippedCount,
            AIProvider = outputOptions.EnableAI ? "configured" : null,
            ImagesDirectory = outputOptions.ExtractImages ? imagesDir : null,
            OutputDirectory = outputDirectory
        };

        // Write output
        var writer = new FileSystemOutputWriter();
        await writer.WriteExtractionAsync(result, outputDirectory, outputOptions, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Chunk document and write to output directory.
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <param name="chunkingOptions">Chunking options</param>
    /// <param name="outputOptions">Output options</param>
    /// <param name="imageToTextService">Optional AI image analysis service</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chunking result with output directory</returns>
    public async Task<ChunkingResult> ChunkToDirectoryAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        OutputOptions? outputOptions = null,
        IImageToTextService? imageToTextService = null,
        CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);
        chunkingOptions ??= new ChunkingOptions();
        outputOptions ??= new OutputOptions();

        // Determine output directory
        var outputDirectory = outputOptions.OutputDirectory
            ?? OutputOptions.GetDefaultOutputDirectory(filePath, "chunks");

        Directory.CreateDirectory(outputDirectory);

        // Extract raw content
        var rawContent = await ExtractAsync(filePath, cancellationToken).ConfigureAwait(false);

        // Parse document structure
        var parsedContent = await ParseAsync(rawContent, null, cancellationToken).ConfigureAwait(false);

        // Process images
        var processedText = parsedContent.Text;
        var images = new List<ProcessedImage>();
        var skippedCount = 0;

        // Use explicit ImagesDirectory if provided, otherwise default to outputDirectory/images
        var chunkImagesDir = outputOptions.ImagesDirectory ?? Path.Combine(outputDirectory, "images");

        if (outputOptions.ExtractImages)
        {
            var imageProcessor = new ImageProcessor(outputOptions);

            // Check if images were pre-extracted by Reader (e.g., HTML with embedded base64)
            if (rawContent.Images.Count > 0 && rawContent.Images.Any(i => i.Data != null))
            {
                var imageResult = await imageProcessor.ProcessPreExtractedImagesAsync(
                    parsedContent.Text, rawContent.Images, chunkImagesDir, imageToTextService, cancellationToken).ConfigureAwait(false);

                processedText = imageResult.ProcessedContent;
                images = imageResult.Images;
                skippedCount = imageResult.SkippedCount;
            }
            else
            {
                // Fallback to inline base64 processing (for other document types)
                var imageResult = await imageProcessor.ProcessImagesAsync(
                    parsedContent.Text, chunkImagesDir, imageToTextService, cancellationToken).ConfigureAwait(false);

                processedText = imageResult.ProcessedContent;
                images = imageResult.Images;
                skippedCount = imageResult.SkippedCount;
            }
        }
        else
        {
            processedText = ImageProcessor.RemoveBase64Images(parsedContent.Text);
        }

        // Update parsed content with processed text
        parsedContent.Text = processedText;

        // Chunk the processed content
        var chunks = await ChunkAsync(parsedContent, chunkingOptions, cancellationToken).ConfigureAwait(false);

        var extraction = new ExtractionResult
        {
            ParsedContent = parsedContent,
            ProcessedText = processedText,
            Images = images,
            SkippedImageCount = skippedCount,
            AIProvider = outputOptions.EnableAI ? "configured" : null,
            ImagesDirectory = outputOptions.ExtractImages ? chunkImagesDir : null,
            OutputDirectory = outputDirectory
        };

        var result = new ChunkingResult
        {
            Chunks = chunks,
            Extraction = extraction,
            OutputDirectory = outputDirectory,
            Options = chunkingOptions
        };

        // Write output
        var writer = new FileSystemOutputWriter();
        await writer.WriteChunkingAsync(result, outputDirectory, outputOptions, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Process document (extract + chunk + enrich) and write to output directory.
    /// </summary>
    /// <param name="filePath">Input file path</param>
    /// <param name="chunkingOptions">Chunking options</param>
    /// <param name="outputOptions">Output options</param>
    /// <param name="imageToTextService">Optional AI image analysis service</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chunking result with output directory</returns>
    public async Task<ChunkingResult> ProcessToDirectoryAsync(
        string filePath,
        ChunkingOptions? chunkingOptions = null,
        OutputOptions? outputOptions = null,
        IImageToTextService? imageToTextService = null,
        CancellationToken cancellationToken = default)
    {
        outputOptions ??= new OutputOptions();

        // Change default output directory for process command
        if (outputOptions.OutputDirectory == null)
        {
            outputOptions.OutputDirectory = OutputOptions.GetDefaultOutputDirectory(filePath, "processed");
        }

        return await ChunkToDirectoryAsync(filePath, chunkingOptions, outputOptions, imageToTextService, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a list of TableData to markdown format.
    /// </summary>
    private static string ConvertTablesToMarkdown(IEnumerable<TableData> tables)
    {
        var sb = new StringBuilder();
        int tableNum = 1;

        foreach (var table in tables)
        {
            var tableMarkdown = ConvertTableToMarkdown(table);
            if (!string.IsNullOrEmpty(tableMarkdown))
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"### Table {tableNum} (Page {table.PageNumber})");
                sb.AppendLine();
                sb.AppendLine(tableMarkdown);
                tableNum++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a single TableData to markdown table format.
    /// </summary>
    private static string ConvertTableToMarkdown(TableData table)
    {
        if (table.Cells == null || table.Cells.Length == 0)
        {
            // Use plain text fallback if available
            if (!string.IsNullOrEmpty(table.PlainTextFallback))
                return table.PlainTextFallback;
            return string.Empty;
        }

        var sb = new StringBuilder();
        var columnCount = table.Cells.Max(row => row?.Length ?? 0);

        if (columnCount == 0)
            return string.Empty;

        // Determine headers
        string[] headers;
        int dataStartRow;

        if (table.HasHeader && table.Headers != null && table.Headers.Length > 0)
        {
            headers = table.Headers;
            dataStartRow = 0;
        }
        else if (table.HasHeader && table.Cells.Length > 0)
        {
            headers = table.Cells[0] ?? [];
            dataStartRow = 1;
        }
        else
        {
            // Use first row as header
            headers = table.Cells[0] ?? [];
            dataStartRow = 1;
        }

        // Ensure headers match column count
        if (headers.Length < columnCount)
        {
            var newHeaders = new string[columnCount];
            Array.Copy(headers, newHeaders, headers.Length);
            for (int i = headers.Length; i < columnCount; i++)
            {
                newHeaders[i] = "";
            }
            headers = newHeaders;
        }

        // Header row
        sb.Append('|');
        foreach (var header in headers.Take(columnCount))
        {
            sb.Append(CultureInfo.InvariantCulture, $" {EscapeMarkdownCell(header ?? "")} |");
        }
        sb.AppendLine();

        // Separator row
        sb.Append('|');
        for (int i = 0; i < columnCount; i++)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        // Data rows
        for (int rowIdx = dataStartRow; rowIdx < table.Cells.Length; rowIdx++)
        {
            var row = table.Cells[rowIdx];
            if (row == null) continue;

            sb.Append('|');
            for (int colIdx = 0; colIdx < columnCount; colIdx++)
            {
                var cell = colIdx < row.Length ? row[colIdx] : "";
                sb.Append(CultureInfo.InvariantCulture, $" {EscapeMarkdownCell(cell ?? "")} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes characters that have special meaning in markdown table cells.
    /// </summary>
    private static string EscapeMarkdownCell(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        // Replace pipe characters and newlines
        return text
            .Replace("|", "\\|")
            .Replace("\n", " ")
            .Replace("\r", "")
            .Trim();
    }
}
