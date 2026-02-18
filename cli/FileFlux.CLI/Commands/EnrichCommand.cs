using FileFlux.CLI.Services;
using System.Globalization;
using FileFlux.CLI.Output;
using FileFlux.Domain;
using FluxImprover;
using FluxImprover.Models;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Enrich command - Adds AI-generated summaries and keywords to document chunks
/// Uses FluxImprover for LLM-based enrichment
/// </summary>
public class EnrichCommand : Command
{
    public EnrichCommand() : base("enrich", "Enrich document chunks with AI-generated summaries and keywords")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path (document or chunks JSON/JSONL)"
        };

        var outputOpt = new Option<string>("--output", "-o")
        {
            Description = "Output file path"
        };

        var formatOpt = new Option<string>("--format", "-f")
        {
            Description = "Output format (json, jsonl)",
            DefaultValueFactory = _ => "json"
        };

        var summaryOpt = new Option<bool>("--summary")
        {
            Description = "Generate summaries for each chunk (default: true)",
            DefaultValueFactory = _ => true
        };

        var keywordsOpt = new Option<bool>("--keywords")
        {
            Description = "Extract keywords for each chunk (default: true)",
            DefaultValueFactory = _ => true
        };

        var maxKeywordsOpt = new Option<int>("--max-keywords")
        {
            Description = "Maximum number of keywords to extract (default: 5)",
            DefaultValueFactory = _ => 5
        };

        var quietOpt = new Option<bool>("--quiet", "-q")
        {
            Description = "Minimal output"
        };

        var verboseOpt = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed processing information"
        };

        Arguments.Add(inputArg);
        Options.Add(outputOpt);
        Options.Add(formatOpt);
        Options.Add(summaryOpt);
        Options.Add(keywordsOpt);
        Options.Add(maxKeywordsOpt);
        Options.Add(quietOpt);
        Options.Add(verboseOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var summary = parseResult.GetValue(summaryOpt);
            var keywords = parseResult.GetValue(keywordsOpt);
            var maxKeywords = parseResult.GetValue(maxKeywordsOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, summary, keywords, maxKeywords, quiet, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool generateSummary,
        bool extractKeywords,
        int maxKeywords,
        bool quiet,
        bool verbose,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(input))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(input)}");
            return;
        }

        // Check AI provider
        var config = new CliEnvironmentConfig();
        var factory = new AIProviderFactory(config);

        if (!factory.HasAIProvider())
        {
            AnsiConsole.MarkupLine("[red]Error:[/] No AI provider configured. Set OPENAI_API_KEY or ANTHROPIC_API_KEY.");
            return;
        }

        FluxImproverResult? fluxImproverResult = null;
        try
        {
            fluxImproverResult = factory.CreateFluxImproverServices();
            if (fluxImproverResult is null)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Failed to initialize FluxImprover services.");
                return;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Failed to initialize AI services: {ex.Message}");
            return;
        }

        var fluxImprover = fluxImproverResult.Services;
        format ??= "json";
        output ??= Path.ChangeExtension(input, $".enriched.{format}");

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - Enrich[/]");
            AnsiConsole.MarkupLine($"  Input:    {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:   {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"  Provider: {factory.GetProviderStatus()}");
            AnsiConsole.MarkupLine($"  Summary:  {(generateSummary ? "Yes" : "No")}");
            AnsiConsole.MarkupLine($"  Keywords: {(extractKeywords ? $"Yes (max {maxKeywords})" : "No")}");
            AnsiConsole.WriteLine();
        }

        try
        {
            // Load chunks from input file
            var chunks = await LoadChunksAsync(input, cancellationToken);

            if (chunks.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] No chunks found in input file.");
                return;
            }

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[grey]Loaded {chunks.Count} chunks[/]");
            }

            var enrichedChunks = new List<EnrichedChunkResult>();
            var enrichmentOptions = new FluxImprover.Options.EnrichmentOptions
            {
                EnableSummarization = generateSummary,
                EnableKeywordExtraction = extractKeywords,
                MaxKeywords = maxKeywords
            };

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Enriching chunks[/]", maxValue: chunks.Count);

                    foreach (var chunk in chunks)
                    {
                        // Convert metadata to Dictionary<string, object>
                        Dictionary<string, object>? metadata = null;
                        if (chunk.Metadata != null)
                        {
                            metadata = chunk.Metadata.ToDictionary(
                                kvp => kvp.Key,
                                kvp => (object)kvp.Value);
                        }

                        // Add source document to metadata if present
                        if (!string.IsNullOrEmpty(chunk.SourceDocument))
                        {
                            metadata ??= new Dictionary<string, object>();
                            metadata["sourceDocument"] = chunk.SourceDocument;
                        }

                        var fluxChunk = new Chunk
                        {
                            Id = chunk.Id ?? $"chunk_{chunks.IndexOf(chunk)}",
                            Content = chunk.Content,
                            Metadata = metadata
                        };

                        var enriched = await fluxImprover.ChunkEnrichment.EnrichAsync(
                            fluxChunk, enrichmentOptions, cancellationToken);

                        enrichedChunks.Add(new EnrichedChunkResult
                        {
                            Id = chunk.Id,
                            Content = chunk.Content,
                            Summary = enriched.Summary,
                            Keywords = enriched.Keywords?.ToList() ?? new List<string>(),
                            SourceDocument = chunk.SourceDocument,
                            OriginalMetadata = chunk.Metadata
                        });

                        task.Increment(1);

                        if (verbose)
                        {
                            var preview = chunk.Content.Length > 50
                                ? chunk.Content[..50] + "..."
                                : chunk.Content;
                            AnsiConsole.MarkupLine($"[grey]  Enriched: {Markup.Escape(preview)}[/]");
                        }
                    }
                });

            // Write output
            await WriteOutputAsync(output, format, enrichedChunks, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]✓[/] Enriched {enrichedChunks.Count} chunks");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(output)}");

                // Summary statistics
                var withSummary = enrichedChunks.Count(c => !string.IsNullOrEmpty(c.Summary));
                var withKeywords = enrichedChunks.Count(c => c.Keywords.Count > 0);
                var totalKeywords = enrichedChunks.Sum(c => c.Keywords.Count);

                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Total chunks", enrichedChunks.Count.ToString(CultureInfo.InvariantCulture));
                table.AddRow("With summaries", withSummary.ToString(CultureInfo.InvariantCulture));
                table.AddRow("With keywords", withKeywords.ToString(CultureInfo.InvariantCulture));
                table.AddRow("Total keywords", totalKeywords.ToString(CultureInfo.InvariantCulture));
                table.AddRow("Avg keywords/chunk", enrichedChunks.Count > 0
                    ? (totalKeywords / (double)enrichedChunks.Count).ToString("F1", CultureInfo.InvariantCulture)
                    : "0");

                AnsiConsole.Write(table);
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
        finally
        {
            // Dispose FluxImprover resources (including ONNX GenAI models)
            if (fluxImproverResult != null)
            {
                await fluxImproverResult.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<List<ChunkInput>> LoadChunksAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var extension = Path.GetExtension(path).ToLowerInvariant();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        if (extension == ".jsonl")
        {
            var chunks = new List<ChunkInput>();
            using var reader = new StringReader(content);
            string? line;
            int index = 0;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var chunk = JsonSerializer.Deserialize<ChunkInput>(line, options);
                if (chunk != null)
                {
                    chunk.Id ??= $"chunk_{index++}";
                    chunks.Add(chunk);
                }
            }
            return chunks;
        }
        else
        {
            // Try JSON array format
            try
            {
                var chunks = JsonSerializer.Deserialize<List<ChunkInput>>(content, options) ?? new List<ChunkInput>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    chunks[i].Id ??= $"chunk_{i}";
                }
                return chunks;
            }
            catch
            {
                // Try single chunk object
                var single = JsonSerializer.Deserialize<ChunkInput>(content, options);
                if (single != null)
                {
                    single.Id ??= "chunk_0";
                    return new List<ChunkInput> { single };
                }
                return new List<ChunkInput>();
            }
        }
    }

    private static async Task WriteOutputAsync(
        string path,
        string format,
        List<EnrichedChunkResult> chunks,
        CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = format == "json",
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        if (format == "jsonl")
        {
            await using var writer = new StreamWriter(path);
            foreach (var chunk in chunks)
            {
                var line = JsonSerializer.Serialize(chunk, options);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }
        else
        {
            var json = JsonSerializer.Serialize(chunks, options);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
    }

    private class ChunkInput
    {
        public string? Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? SourceDocument { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    private class EnrichedChunkResult
    {
        public string? Id { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public List<string> Keywords { get; set; } = new();
        public string? SourceDocument { get; set; }
        public Dictionary<string, string>? OriginalMetadata { get; set; }
    }
}
