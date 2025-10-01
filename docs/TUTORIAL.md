# FileFlux Tutorial

Complete guide to using FileFlux for document processing and RAG system integration.

## Table of Contents

- [Installation](#installation)
- [Basic Usage](#basic-usage)
- [Document Formats](#document-formats)
- [Chunking Strategies](#chunking-strategies)
- [Advanced Features](#advanced-features)
- [RAG Integration](#rag-integration)
- [Error Handling](#error-handling)
- [Customization](#customization)

## Installation

Install FileFlux via NuGet:

```bash
dotnet add package FileFlux
```

## Basic Usage

### Service Registration

```csharp
using FileFlux;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Optional: Register AI services (implemented by your application)
services.AddScoped<ITextCompletionService, YourLLMService>();
services.AddScoped<IImageToTextService, YourVisionService>();

// Register FileFlux services
services.AddFileFlux();

var provider = services.BuildServiceProvider();
var processor = provider.GetRequiredService<IDocumentProcessor>();
```

### Simple Document Processing

```csharp
// Basic processing
var chunks = await processor.ProcessAsync("document.pdf");

foreach (var chunk in chunks)
{
    Console.WriteLine($"Chunk {chunk.Index}: {chunk.Content}");
}
```

### Streaming Processing

```csharp
// Recommended for large documents - memory efficient
await foreach (var result in processor.ProcessStreamAsync("document.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"Chunk {chunk.Index}: {chunk.Content.Length} chars");
            Console.WriteLine($"Quality Score: {chunk.Quality}");
        }
    }
}
```

### Chunking Options

```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",      // Automatic strategy selection (recommended)
    MaxChunkSize = 512,     // Maximum chunk size in tokens
    OverlapSize = 64,       // Overlap between chunks
    PreserveStructure = true // Maintain document structure
};

var chunks = await processor.ProcessAsync("document.pdf", options);
```

## Document Formats

FileFlux supports the following document formats:

| Format | Extension | Text Extraction | Image Processing |
|--------|-----------|----------------|------------------|
| PDF | `.pdf` | ✅ | ✅ |
| Word | `.docx` | ✅ | Planned |
| Excel | `.xlsx` | ✅ | ❌ |
| PowerPoint | `.pptx` | ✅ | Planned |
| Markdown | `.md` | ✅ | ❌ |
| HTML | `.html`, `.htm` | ✅ | ✅ |
| Text | `.txt` | ✅ | ❌ |
| JSON | `.json` | ✅ | ❌ |
| CSV | `.csv` | ✅ | ❌ |

### Format-Specific Features

**PDF**: Text and image extraction, structure recognition, metadata preservation

**Word**: Style recognition, headers, tables, and image captions

**Excel**: Multi-sheet support, formula extraction, table structure analysis

**PowerPoint**: Slide content, notes, and title structure extraction

**Markdown**: Header, code block, and table structure preservation

**HTML**: Web content extraction with structure preservation

**Text**: Plain text with automatic encoding detection

**JSON**: Structured data flattening and schema extraction

**CSV**: Table data with header preservation

### Extension Discovery

```csharp
var factory = provider.GetRequiredService<IDocumentReaderFactory>();

// Get all supported extensions
var extensions = factory.GetSupportedExtensions();
Console.WriteLine($"Supported: {string.Join(", ", extensions)}");

// Check specific extension
bool isSupported = factory.IsExtensionSupported(".pdf");

// Get extension-to-reader mapping
var mapping = factory.GetExtensionReaderMapping();
foreach (var kvp in mapping)
{
    Console.WriteLine($"{kvp.Key} → {kvp.Value}");
}
```

## Chunking Strategies

### Auto Strategy (Recommended)

Automatically selects the best strategy based on document type:

```csharp
var options = new ChunkingOptions
{
    Strategy = "Auto",
    MaxChunkSize = 512,
    OverlapSize = 64
};
```

### Smart Strategy

Sentence boundary-based chunking with high completeness:

```csharp
var options = new ChunkingOptions
{
    Strategy = "Smart",
    MaxChunkSize = 512,
    OverlapSize = 128
};
```

Use for: Legal documents, medical records, academic papers

### Intelligent Strategy

LLM-based semantic boundary detection (requires ITextCompletionService):

```csharp
var options = new ChunkingOptions
{
    Strategy = "Intelligent",
    MaxChunkSize = 512,
    OverlapSize = 64
};
```

Use for: Technical documentation, API docs, complex content

### MemoryOptimizedIntelligent Strategy

Memory-efficient intelligent chunking with object pooling:

```csharp
var options = new ChunkingOptions
{
    Strategy = "MemoryOptimizedIntelligent",
    MaxChunkSize = 512,
    OverlapSize = 64
};
```

Use for: Large documents, server environments with memory constraints

### Semantic Strategy

Sentence-based semantic chunking:

```csharp
var options = new ChunkingOptions
{
    Strategy = "Semantic",
    MaxChunkSize = 800
};
```

Use for: General documents, research papers

### Paragraph Strategy

Paragraph-level segmentation:

```csharp
var options = new ChunkingOptions
{
    Strategy = "Paragraph",
    PreserveStructure = true
};
```

Use for: Markdown files, blog posts, structured text

### FixedSize Strategy

Fixed-size token-based chunking:

```csharp
var options = new ChunkingOptions
{
    Strategy = "FixedSize",
    MaxChunkSize = 512
};
```

Use for: Uniform processing requirements, simple splitting needs

## Advanced Features

### Multimodal Processing

Process documents with images using vision AI:

```csharp
// Implement image-to-text service
public class OpenAiVisionService : IImageToTextService
{
    private readonly OpenAIClient _client;

    public OpenAiVisionService(string apiKey)
    {
        _client = new OpenAIClient(apiKey);
    }

    public async Task<ImageToTextResult> ExtractTextAsync(
        byte[] imageData,
        ImageToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var chatClient = _client.GetChatClient("gpt-4-vision-preview");

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("Extract all text from the image accurately."),
            new UserChatMessage(ChatMessageContentPart.CreateImagePart(
                BinaryData.FromBytes(imageData), "image/jpeg"))
        };

        var response = await chatClient.CompleteChatAsync(messages, cancellationToken);

        return new ImageToTextResult
        {
            ExtractedText = response.Value.Content[0].Text,
            Confidence = 0.95,
            IsSuccess = true
        };
    }
}

// Register and use
services.AddScoped<IImageToTextService, OpenAiVisionService>();

// Process document with images
await foreach (var result in processor.ProcessStreamAsync("document-with-images.pdf"))
{
    if (result.IsSuccess && result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            if (chunk.Props.ContainsKey("HasImages"))
            {
                Console.WriteLine($"Image text extracted: {chunk.Content}");
            }
        }
    }
}
```

### Step-by-Step Processing

Break down processing into individual steps:

```csharp
// Step 1: Extract raw content
var rawContent = await processor.ExtractAsync("document.pdf");
Console.WriteLine($"Extracted: {rawContent.Content.Length} chars");

// Step 2: Parse structure
var parsedContent = await processor.ParseAsync(rawContent);
Console.WriteLine($"Sections: {parsedContent.Sections?.Count ?? 0}");

// Step 3: Chunk content
var chunks = await processor.ChunkAsync(parsedContent, new ChunkingOptions
{
    Strategy = "Auto",
    MaxChunkSize = 512,
    OverlapSize = 64
});
Console.WriteLine($"Chunks: {chunks.Count()}");
```

### Quality Analysis

Analyze chunk quality metrics:

```csharp
var qualityEngine = provider.GetRequiredService<ChunkQualityEngine>();
var chunks = await processor.ProcessAsync("document.pdf");

var metrics = await qualityEngine.CalculateQualityMetricsAsync(chunks);
Console.WriteLine($"Average Completeness: {metrics.AverageCompleteness:P}");
Console.WriteLine($"Content Consistency: {metrics.ContentConsistency:P}");
Console.WriteLine($"Boundary Quality: {metrics.BoundaryQuality:P}");
Console.WriteLine($"Size Distribution: {metrics.SizeDistribution:P}");
```

### Question Generation

Generate questions for RAG quality testing:

```csharp
var parsedContent = await processor.ParseAsync(rawContent);
var questions = await qualityEngine.GenerateQuestionsAsync(parsedContent, 10);

foreach (var question in questions)
{
    Console.WriteLine($"Q: {question.Question}");
    Console.WriteLine($"   Type: {question.Type}");
    Console.WriteLine($"   Difficulty: {question.DifficultyScore:P}");
}

// Validate answerability
var validation = await qualityEngine.ValidateAnswerabilityAsync(questions, chunks);
Console.WriteLine($"Answerable: {validation.AnswerableQuestions}/{validation.TotalQuestions}");
```

## RAG Integration

### Complete RAG Pipeline

```csharp
public class RagService
{
    private readonly IDocumentProcessor _processor;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;

    public async Task IndexDocumentAsync(string filePath)
    {
        var options = new ChunkingOptions
        {
            Strategy = "Auto",
            MaxChunkSize = 512,
            OverlapSize = 64,
            PreserveStructure = true
        };

        await foreach (var result in _processor.ProcessStreamAsync(filePath, options))
        {
            if (result.IsSuccess && result.Result != null)
            {
                foreach (var chunk in result.Result)
                {
                    // Generate embedding
                    var embedding = await _embeddingService.GenerateAsync(chunk.Content);

                    // Store in vector database
                    await _vectorStore.StoreAsync(new
                    {
                        Id = chunk.Id,
                        Content = chunk.Content,
                        Metadata = chunk.Props,
                        Vector = embedding
                    });
                }
            }

            // Display progress
            if (result.Progress != null)
            {
                Console.WriteLine($"Progress: {result.Progress.PercentComplete:F1}%");
            }
        }
    }
}
```

### Batch Processing

```csharp
public async Task ProcessMultipleDocumentsAsync(string[] filePaths)
{
    var tasks = filePaths.Select(async filePath =>
    {
        var chunks = new List<DocumentChunk>();

        await foreach (var result in processor.ProcessStreamAsync(filePath))
        {
            if (result.IsSuccess && result.Result != null)
            {
                chunks.AddRange(result.Result);
            }
        }

        return new { FilePath = filePath, Chunks = chunks };
    });

    var results = await Task.WhenAll(tasks);

    foreach (var result in results)
    {
        Console.WriteLine($"{result.FilePath}: {result.Chunks.Count} chunks");
    }
}
```

## Error Handling

### Exception Handling

```csharp
try
{
    var chunks = await processor.ProcessAsync("document.pdf");
}
catch (UnsupportedFileFormatException ex)
{
    Console.WriteLine($"Unsupported format: {ex.FileName}");
}
catch (DocumentProcessingException ex)
{
    Console.WriteLine($"Processing error: {ex.Message}");
    Console.WriteLine($"File: {ex.FileName}");
}
catch (FileNotFoundException)
{
    Console.WriteLine("File not found");
}
```

### Streaming Error Handling

```csharp
await foreach (var result in processor.ProcessStreamAsync("document.pdf"))
{
    if (!result.IsSuccess)
    {
        Console.WriteLine($"Error: {result.Error}");
        continue; // Continue with next chunk
    }

    if (result.Result != null)
    {
        foreach (var chunk in result.Result)
        {
            Console.WriteLine($"Chunk {chunk.Index} processed successfully");
        }
    }
}
```

### Validation

```csharp
public async Task<bool> ValidateAndProcessAsync(string filePath)
{
    // Check file exists
    if (!File.Exists(filePath))
    {
        Console.WriteLine("File not found");
        return false;
    }

    // Check extension
    var factory = provider.GetRequiredService<IDocumentReaderFactory>();
    var extension = Path.GetExtension(filePath);

    if (!factory.IsExtensionSupported(extension))
    {
        Console.WriteLine($"Unsupported extension: {extension}");
        return false;
    }

    // Process
    try
    {
        var chunks = await processor.ProcessAsync(filePath);
        Console.WriteLine($"Processed: {chunks.Count()} chunks");
        return true;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        return false;
    }
}
```

## Customization

### Custom Chunking Strategy

```csharp
public class CustomChunkingStrategy : IChunkingStrategy
{
    public string StrategyName => "Custom";

    public async Task<IEnumerable<DocumentChunk>> ChunkAsync(
        ParsedDocumentContent content,
        ChunkingOptions options,
        CancellationToken cancellationToken = default)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = content.Content.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid(),
                Content = sentence.Trim(),
                Index = chunkIndex++,
                Location = new SourceLocation
                {
                    StartChar = 0,
                    EndChar = sentence.Length
                },
                Quality = CalculateQuality(sentence),
                Props = new Dictionary<string, object>
                {
                    ["Length"] = sentence.Length
                }
            });
        }

        return chunks;
    }

    private double CalculateQuality(string text)
    {
        return text.Length > 50 ? 0.8 : 0.5;
    }
}

// Register
services.AddTransient<IChunkingStrategy, CustomChunkingStrategy>();
```

### Custom Document Reader

```csharp
public class CustomDocumentReader : IDocumentReader
{
    public string ReaderType => "CustomReader";
    public IEnumerable<string> SupportedExtensions => [".custom"];

    public bool CanRead(string fileName) =>
        Path.GetExtension(fileName).Equals(".custom", StringComparison.OrdinalIgnoreCase);

    public async Task<RawContent> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);

        return new RawContent
        {
            Text = content,
            File = new SourceFileInfo
            {
                Name = Path.GetFileName(filePath),
                Extension = Path.GetExtension(filePath),
                Size = new FileInfo(filePath).Length
            },
            ReaderType = "CustomReader",
            ExtractedAt = DateTime.UtcNow
        };
    }
}

// Register
services.AddTransient<IDocumentReader, CustomDocumentReader>();
```

### Custom AI Service

```csharp
public class CustomTextCompletionService : ITextCompletionService
{
    public async Task<string> GenerateAsync(
        string prompt,
        TextCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Implement your LLM integration
        // Examples: OpenAI, Anthropic, Azure OpenAI, local models

        await Task.Delay(100, cancellationToken);
        return "Generated response";
    }
}

// Register
services.AddScoped<ITextCompletionService, CustomTextCompletionService>();
```

## Related Documentation

- [Architecture](ARCHITECTURE.md) - System design and architecture
- [RAG Design](RAG-DESIGN.md) - RAG system integration patterns
- [Building](../BUILDING.md) - Build and test guide
- [GitHub Repository](https://github.com/iyulab/FileFlux)
- [NuGet Package](https://www.nuget.org/packages/FileFlux)
