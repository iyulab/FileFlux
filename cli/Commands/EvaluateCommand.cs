using FileFlux.CLI.Services;
using FluxImprover;
using FluxImprover.Options;
using FluxImprover.Evaluation;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;

namespace FileFlux.CLI.Commands;

/// <summary>
/// Evaluate command - Evaluates QA pairs for quality metrics (Faithfulness, Relevancy, Answerability)
/// Uses FluxImprover for LLM-based evaluation
/// </summary>
public class EvaluateCommand : Command
{
    public EvaluateCommand() : base("evaluate", "Evaluate QA pairs for quality metrics")
    {
        var inputArg = new Argument<string>("input")
        {
            Description = "Input file path (QA pairs JSON/JSONL)"
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

        var faithfulnessOpt = new Option<bool>("--faithfulness")
        {
            Description = "Evaluate faithfulness (default: true)",
            DefaultValueFactory = _ => true
        };

        var relevancyOpt = new Option<bool>("--relevancy")
        {
            Description = "Evaluate relevancy (default: true)",
            DefaultValueFactory = _ => true
        };

        var answerabilityOpt = new Option<bool>("--answerability")
        {
            Description = "Evaluate answerability (default: true)",
            DefaultValueFactory = _ => true
        };

        var thresholdOpt = new Option<float>("--threshold")
        {
            Description = "Pass threshold for quality metrics (0.0-1.0, default: 0.7)",
            DefaultValueFactory = _ => 0.7f
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
        Options.Add(faithfulnessOpt);
        Options.Add(relevancyOpt);
        Options.Add(answerabilityOpt);
        Options.Add(thresholdOpt);
        Options.Add(quietOpt);
        Options.Add(verboseOpt);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var input = parseResult.GetValue(inputArg);
            var output = parseResult.GetValue(outputOpt);
            var format = parseResult.GetValue(formatOpt);
            var faithfulness = parseResult.GetValue(faithfulnessOpt);
            var relevancy = parseResult.GetValue(relevancyOpt);
            var answerability = parseResult.GetValue(answerabilityOpt);
            var threshold = parseResult.GetValue(thresholdOpt);
            var quiet = parseResult.GetValue(quietOpt);
            var verbose = parseResult.GetValue(verboseOpt);

            if (input != null)
            {
                await ExecuteAsync(input, output, format, faithfulness, relevancy, answerability, threshold, quiet, verbose, cancellationToken);
            }
        });
    }

    private static async Task ExecuteAsync(
        string input,
        string? output,
        string? format,
        bool evalFaithfulness,
        bool evalRelevancy,
        bool evalAnswerability,
        float threshold,
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
        output ??= Path.ChangeExtension(input, $".evaluated.{format}");

        var metricsEnabled = new List<string>();
        if (evalFaithfulness) metricsEnabled.Add("Faithfulness");
        if (evalRelevancy) metricsEnabled.Add("Relevancy");
        if (evalAnswerability) metricsEnabled.Add("Answerability");

        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]FileFlux CLI - QA Evaluation[/]");
            AnsiConsole.MarkupLine($"  Input:      {Markup.Escape(input)}");
            AnsiConsole.MarkupLine($"  Output:     {Markup.Escape(output)}");
            AnsiConsole.MarkupLine($"  Provider:   {factory.GetProviderStatus()}");
            AnsiConsole.MarkupLine($"  Metrics:    {string.Join(", ", metricsEnabled)}");
            AnsiConsole.MarkupLine($"  Threshold:  {threshold:P0}");
            AnsiConsole.WriteLine();
        }

        try
        {
            // Load QA pairs from input file
            var qaPairs = await LoadQAPairsAsync(input, cancellationToken);

            if (qaPairs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]Warning:[/] No QA pairs found in input file.");
                return;
            }

            if (!quiet)
            {
                AnsiConsole.MarkupLine($"[grey]Loaded {qaPairs.Count} QA pairs[/]");
            }

            var evaluatedPairs = new List<EvaluatedQAResult>();
            var evalOptions = new EvaluationOptions
            {
                EnableFaithfulness = evalFaithfulness,
                EnableRelevancy = evalRelevancy,
                EnableAnswerability = evalAnswerability,
                PassThreshold = threshold
            };

            int passed = 0;
            int failed = 0;
            double totalFaithfulness = 0;
            double totalRelevancy = 0;
            double totalAnswerability = 0;
            int faithfulnessCount = 0;
            int relevancyCount = 0;
            int answerabilityCount = 0;

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
                    var task = ctx.AddTask("[green]Evaluating QA pairs[/]", maxValue: qaPairs.Count);

                    foreach (var qa in qaPairs)
                    {
                        var context = qa.Context ?? string.Empty;
                        var result = new EvaluatedQAResult
                        {
                            Id = qa.Id,
                            Question = qa.Question,
                            Answer = qa.Answer,
                            Context = qa.Context,
                            SourceChunkId = qa.SourceChunkId
                        };

                        // Evaluate Faithfulness
                        if (evalFaithfulness && !string.IsNullOrWhiteSpace(context))
                        {
                            var faithfulnessResult = await fluxImprover.Faithfulness.EvaluateAsync(
                                context, qa.Answer, evalOptions, cancellationToken);
                            result.Faithfulness = faithfulnessResult.Score;
                            result.FaithfulnessDetails = faithfulnessResult.Details.TryGetValue("reasoning", out var fr)
                                ? fr?.ToString()
                                : null;
                            totalFaithfulness += faithfulnessResult.Score;
                            faithfulnessCount++;
                        }

                        // Evaluate Relevancy
                        if (evalRelevancy)
                        {
                            var relevancyResult = await fluxImprover.Relevancy.EvaluateAsync(
                                qa.Question, qa.Answer, evalOptions, context, cancellationToken);
                            result.Relevancy = relevancyResult.Score;
                            result.RelevancyDetails = relevancyResult.Details.TryGetValue("reasoning", out var rr)
                                ? rr?.ToString()
                                : null;
                            totalRelevancy += relevancyResult.Score;
                            relevancyCount++;
                        }

                        // Evaluate Answerability
                        if (evalAnswerability && !string.IsNullOrWhiteSpace(context))
                        {
                            var answerabilityResult = await fluxImprover.Answerability.EvaluateAsync(
                                context, qa.Question, evalOptions, cancellationToken);
                            result.Answerability = answerabilityResult.Score;
                            result.AnswerabilityDetails = answerabilityResult.Details.TryGetValue("reasoning", out var ar)
                                ? ar?.ToString()
                                : null;
                            totalAnswerability += answerabilityResult.Score;
                            answerabilityCount++;
                        }

                        // Calculate overall pass/fail
                        var scores = new List<double>();
                        if (result.Faithfulness.HasValue) scores.Add(result.Faithfulness.Value);
                        if (result.Relevancy.HasValue) scores.Add(result.Relevancy.Value);
                        if (result.Answerability.HasValue) scores.Add(result.Answerability.Value);

                        if (scores.Count > 0)
                        {
                            result.OverallScore = scores.Average();
                            result.Passed = result.OverallScore >= threshold;
                            if (result.Passed) passed++; else failed++;
                        }

                        evaluatedPairs.Add(result);
                        task.Increment(1);

                        if (verbose)
                        {
                            var status = result.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
                            AnsiConsole.MarkupLine($"[grey]  {status} ({result.OverallScore:P0}): {Markup.Escape(qa.Question.Length > 50 ? qa.Question[..50] + "..." : qa.Question)}[/]");
                        }
                    }
                });

            // Write output
            await WriteOutputAsync(output, format, evaluatedPairs, cancellationToken);

            if (!quiet)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[green]✓[/] Evaluated {evaluatedPairs.Count} QA pairs");
                AnsiConsole.MarkupLine($"[green]✓[/] Output: {Markup.Escape(output)}");

                // Summary statistics
                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Average");
                table.AddColumn("Count");

                if (faithfulnessCount > 0)
                    table.AddRow("Faithfulness", $"{totalFaithfulness / faithfulnessCount:P1}", faithfulnessCount.ToString());
                if (relevancyCount > 0)
                    table.AddRow("Relevancy", $"{totalRelevancy / relevancyCount:P1}", relevancyCount.ToString());
                if (answerabilityCount > 0)
                    table.AddRow("Answerability", $"{totalAnswerability / answerabilityCount:P1}", answerabilityCount.ToString());

                table.AddEmptyRow();
                table.AddRow("[bold]Pass Rate[/]", $"[bold]{(double)passed / evaluatedPairs.Count:P1}[/]", $"[bold]{passed}/{evaluatedPairs.Count}[/]");

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

    private static async Task<List<QAInput>> LoadQAPairsAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        var extension = Path.GetExtension(path).ToLowerInvariant();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        if (extension == ".jsonl")
        {
            var pairs = new List<QAInput>();
            using var reader = new StringReader(content);
            string? line;
            int index = 0;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var qa = JsonSerializer.Deserialize<QAInput>(line, options);
                if (qa != null)
                {
                    qa.Id ??= $"qa_{index++}";
                    pairs.Add(qa);
                }
            }
            return pairs;
        }
        else
        {
            try
            {
                var pairs = JsonSerializer.Deserialize<List<QAInput>>(content, options) ?? new List<QAInput>();
                for (int i = 0; i < pairs.Count; i++)
                {
                    pairs[i].Id ??= $"qa_{i}";
                }
                return pairs;
            }
            catch
            {
                var single = JsonSerializer.Deserialize<QAInput>(content, options);
                if (single != null)
                {
                    single.Id ??= "qa_0";
                    return new List<QAInput> { single };
                }
                return new List<QAInput>();
            }
        }
    }

    private static async Task WriteOutputAsync(
        string path,
        string format,
        List<EvaluatedQAResult> results,
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
            foreach (var result in results)
            {
                var line = JsonSerializer.Serialize(result, options);
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            }
        }
        else
        {
            var json = JsonSerializer.Serialize(results, options);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
    }

    private class QAInput
    {
        public string? Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string? Context { get; set; }
        public string? SourceChunkId { get; set; }
    }

    private class EvaluatedQAResult
    {
        public string? Id { get; set; }
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string? Context { get; set; }
        public string? SourceChunkId { get; set; }
        public double? Faithfulness { get; set; }
        public string? FaithfulnessDetails { get; set; }
        public double? Relevancy { get; set; }
        public string? RelevancyDetails { get; set; }
        public double? Answerability { get; set; }
        public string? AnswerabilityDetails { get; set; }
        public double? OverallScore { get; set; }
        public bool Passed { get; set; }
    }
}
