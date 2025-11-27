using FileFlux.CLI.Services;
using FileFlux.CLI.Output;
using FluxImprover;
using FluxImprover.Abstractions.Models;
using FluxImprover.Abstractions.Options;
using FluxImprover.QAGeneration;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;

namespace FileFlux.CLI.Commands;

/// <summary>
/// QA command - Generates question-answer pairs from document chunks
/// Uses FluxImprover for LLM-based QA generation
/// </summary>
public class QACommand : Command
{
    public QACommand() : base("qa", "Generate question-answer pairs from document chunks")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path (chunks JSON/JSONL)"
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

        var pairsOpt = new Option<int>("--pairs-per-chunk")
        {
            Description = "Number of QA pairs to generate per chunk (default: 3)",
            DefaultValueFactory = _ => 3
        };

        var skipFilterOpt = new Option<bool>("--skip-filter")
        {
            Description = "Skip quality filtering of generated QA pairs"
        };

        var includeMultiHopOpt = new Option<bool>("--multi-hop")
        {
            Description = "Include multi-hop questions requiring reasoning across chunks"
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
        Options.Add(pairsOpt);
        Options.Add(skipFilterOpt);
        Options.Add(includeMultiHopOpt);
        Options.Add(quietOpt);
        Options.Add(verboseOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var pairsPerChunk = parseResult.GetValue(pairsOpt);
            var skipFilter = parseResult.GetValue(skipFilterOpt);
            var includeMultiHop = parseResult.GetValue(includeMultiHopOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, pairsPerChunk, skipFilter, includeMultiHop, quiet, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        int pairsPerChunk,
        bool skipFilter,
        bool includeMultiHop,
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

        FluxImproverServices? fluxImprover;
        try
        {
            fluxImprover = factory.CreateFluxImproverServices();
            if (fluxImprover is null)
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

        format ??= "json";
        output ??= Path.ChangeExtension(input, $".qa.{format}");

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - QA Generation[/]");
            AnsiConsole.MarkupLine($"  Input:          {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:         {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"  Provider:       {factory.GetProviderStatus()}");
            AnsiConsole.MarkupLine($"  Pairs/Chunk:    {pairsPerChunk}");
            AnsiConsole.MarkupLine($"  Skip Filter:    {(skipFilter ? "Yes" : "No")}");
            AnsiConsole.MarkupLine($"  Multi-hop:      {(includeMultiHop ? "Yes" : "No")}");
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

            var allQAPairs = new List<QAOutputResult>();
            var pipelineOptions = new QAPipelineOptions
            {
                GenerationOptions = new QAGenerationOptions
                {
                    PairsPerChunk = pairsPerChunk,
                    IncludeMultiHop = includeMultiHop
                },
                SkipFiltering = skipFilter
            };

            int totalGenerated = 0;
            int totalFiltered = 0;

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
                    var task = ctx.AddTask("[green]Generating QA pairs[/]", maxValue: chunks.Count);

                    foreach (var chunk in chunks)
                    {
                        // Convert metadata to IDictionary<string, object>
                        IDictionary<string, object>? metadata = null;
                        if (chunk.Metadata != null)
                        {
                            metadata = chunk.Metadata.ToDictionary(
                                kvp => kvp.Key,
                                kvp => (object)kvp.Value);
                        }

                        var fluxChunk = new Chunk
                        {
                            Id = chunk.Id ?? $"chunk_{chunks.IndexOf(chunk)}",
                            Content = chunk.Content,
                            Metadata = metadata
                        };

                        var result = await fluxImprover.QAPipeline.ExecuteFromChunkAsync(
                            fluxChunk, pipelineOptions, cancellationToken);

                        totalGenerated += result.GeneratedCount;
                        totalFiltered += result.FilteredCount;

                        foreach (var qa in result.QAPairs)
                        {
                            allQAPairs.Add(new QAOutputResult
                            {
                                Id = qa.Id,
                                Question = qa.Question,
                                Answer = qa.Answer,
                                SourceChunkId = chunk.Id,
                                Context = qa.Context,
                                Evaluation = qa.Evaluation != null ? new QAEvaluationResult
                                {
                                    Faithfulness = qa.Evaluation.Faithfulness,
                                    Relevancy = qa.Evaluation.Relevancy,
                                    Answerability = qa.Evaluation.Answerability
                                } : null
                            });
                        }

                        task.Increment(1);

                        if (verbose)
                        {
                            var preview = chunk.Content.Length > 50
                                ? chunk.Content[..50] + "..."
                                : chunk.Content;
                            AnsiConsole.MarkupLine($"[grey]  Generated {result.FilteredCount} QA pairs from: {Markup.Escape(preview)}[/]");
                        }
                    }
                });

            // Write output
            await WriteOutputAsync(output, format, allQAPairs, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]✓[/] Generated {allQAPairs.Count} QA pairs");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(output)}");

                // Summary statistics
                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Value");
                table.AddRow("Source chunks", chunks.Count.ToString());
                table.AddRow("Total generated", totalGenerated.ToString());
                table.AddRow("After filtering", totalFiltered.ToString());
                table.AddRow("Filter pass rate", totalGenerated > 0
                    ? $"{(double)totalFiltered / totalGenerated:P1}"
                    : "N/A");
                table.AddRow("Avg QA per chunk", chunks.Count > 0
                    ? (allQAPairs.Count / (double)chunks.Count).ToString("F1")
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
        List<QAOutputResult> qaPairs,
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
            foreach (var qa in qaPairs)
            {
                var line = JsonSerializer.Serialize(qa, options);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }
        else
        {
            var json = JsonSerializer.Serialize(qaPairs, options);
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

    private class QAOutputResult
    {
        public string? Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string? SourceChunkId { get; set; }
        public string? Context { get; set; }
        public QAEvaluationResult? Evaluation { get; set; }
    }

    private class QAEvaluationResult
    {
        public double? Faithfulness { get; set; }
        public double? Relevancy { get; set; }
        public double? Answerability { get; set; }
    }
}
