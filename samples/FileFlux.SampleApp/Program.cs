using FileFlux;
using FileFlux.SampleApp.Data;
using FileFlux.SampleApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace FileFlux.SampleApp;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Command-line interface setup
        var rootCommand = new RootCommand("FileFlux RAG 데모 애플리케이션");

        var processCommand = CreateProcessCommand();
        var processProgressCommand = CreateProcessWithProgressCommand();
        var queryCommand = CreateQueryCommand();
        var listCommand = CreateListCommand();
        var historyCommand = CreateHistoryCommand();
        var readersCommand = CreateReadersTestCommand();
        var benchmarkCommand = CreateBenchmarkCommand();
        var visionTestCommand = CreateVisionTestCommand();

        rootCommand.Subcommands.Add(processCommand);
        rootCommand.Subcommands.Add(processProgressCommand);
        rootCommand.Subcommands.Add(queryCommand);
        rootCommand.Subcommands.Add(listCommand);
        rootCommand.Subcommands.Add(historyCommand);
        rootCommand.Subcommands.Add(readersCommand);
        rootCommand.Subcommands.Add(benchmarkCommand);
        rootCommand.Subcommands.Add(visionTestCommand);

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync();
    }

    private static Command CreateProcessCommand()
    {
        var filePathArgument = new Argument<string>("file-path")
        {
            Description = "처리할 파일 경로"
        };

        var strategyOption = new Option<string>("--strategy")
        {
            Description = "청킹 전략",
            DefaultValueFactory = _ => "Intelligent",
            AllowMultipleArgumentsPerToken = false
        };

        var processCommand = new Command("process", "문서를 처리하여 벡터 스토어에 저장");
        processCommand.Arguments.Add(filePathArgument);
        processCommand.Options.Add(strategyOption);

        processCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var filePath = parseResult.GetValue(filePathArgument);
            var strategy = parseResult.GetValue(strategyOption);

            if (filePath != null)
            {
                using var host = CreateHost();
                await host.StartAsync(cancellationToken);
                var app = host.Services.GetRequiredService<FileFluxApp>();
                await app.ProcessDocumentAsync(filePath, strategy!);
                await host.StopAsync(cancellationToken);
            }
        });

        return processCommand;
    }

    private static Command CreateProcessWithProgressCommand()
    {
        var filePathArgument = new Argument<string>("file-path")
        {
            Description = "처리할 파일 경로"
        };

        var strategyOption = new Option<string>("--strategy")
        {
            Description = "청킹 전략",
            DefaultValueFactory = _ => "Intelligent",
            AllowMultipleArgumentsPerToken = false
        };

        var processProgressCommand = new Command("process-progress", "문서를 진행률 추적과 함께 처리하여 벡터 스토어에 저장");
        processProgressCommand.Arguments.Add(filePathArgument);
        processProgressCommand.Options.Add(strategyOption);

        processProgressCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var filePath = parseResult.GetValue(filePathArgument);
            var strategy = parseResult.GetValue(strategyOption);

            if (filePath != null)
            {
                using var host = CreateHost();
                await host.StartAsync(cancellationToken);
                var app = host.Services.GetRequiredService<FileFluxApp>();
                await app.ProcessDocumentStreamAsync(filePath, strategy!);
                await host.StopAsync(cancellationToken);
            }
        });

        return processProgressCommand;
    }

    private static Command CreateQueryCommand()
    {
        var queryArgument = new Argument<string>("query")
        {
            Description = "검색할 쿼리"
        };

        var topKOption = new Option<int>("--top-k")
        {
            Description = "반환할 최대 결과 수",
            DefaultValueFactory = _ => 5
        };

        var queryCommand = new Command("query", "RAG 쿼리 실행");
        queryCommand.Arguments.Add(queryArgument);
        queryCommand.Options.Add(topKOption);

        queryCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var query = parseResult.GetValue(queryArgument);
            var topK = parseResult.GetValue(topKOption);

            if (query != null)
            {
                using var host = CreateHost();
                await host.StartAsync(cancellationToken);
                var app = host.Services.GetRequiredService<FileFluxApp>();
                await app.ExecuteQueryAsync(query, topK);
                await host.StopAsync(cancellationToken);
            }
        });

        return queryCommand;
    }

    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "저장된 문서 목록 조회");

        listCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            using var host = CreateHost();
            await host.StartAsync(cancellationToken);
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ListDocumentsAsync();
            await host.StopAsync(cancellationToken);
        });

        return listCommand;
    }

    private static Command CreateHistoryCommand()
    {
        var limitOption = new Option<int>("--limit")
        {
            Description = "표시할 쿼리 기록 수",
            DefaultValueFactory = _ => 10
        };

        var historyCommand = new Command("history", "쿼리 기록 조회");
        historyCommand.Options.Add(limitOption);

        historyCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var limit = parseResult.GetValue(limitOption);

            using var host = CreateHost();
            await host.StartAsync(cancellationToken);
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ShowQueryHistoryAsync(limit);
            await host.StopAsync(cancellationToken);
        });

        return historyCommand;
    }


    private static Command CreateReadersTestCommand()
    {
        var folderPathArgument = new Argument<string>("folder-path")
        {
            Description = "테스트할 문서들이 있는 폴더 경로"
        };

        var readersCommand = new Command("test-readers", "전문 문서 리더 테스트 (Word, Excel, PDF, HTML)");
        readersCommand.Arguments.Add(folderPathArgument);

        readersCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var folderPath = parseResult.GetValue(folderPathArgument);

            if (folderPath != null)
            {
                using var host = CreateHost();
                await host.StartAsync(cancellationToken);
                var app = host.Services.GetRequiredService<FileFluxApp>();
                await app.TestProfessionalReadersAsync(folderPath);
                await host.StopAsync(cancellationToken);
            }
        });

        return readersCommand;
    }

    private static Command CreateBenchmarkCommand()
    {
        var testDirOption = new Option<string>("--test-dir")
        {
            Description = "테스트 파일이 있는 디렉토리 경로",
            DefaultValueFactory = _ => @"D:\data\FileFlux\test"
        };

        var benchmarkCommand = new Command("benchmark", "종합 벤치마크 실행 - 모든 테스트 파일에 대한 성능 측정");
        benchmarkCommand.Options.Add(testDirOption);

        benchmarkCommand.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var testDir = parseResult.GetValue(testDirOption);
            Console.WriteLine($"Benchmark functionality simplified. Test directory: {testDir}");
            return Task.FromResult(0);
        });

        return benchmarkCommand;
    }

    private static Command CreateVisionTestCommand()
    {
        var filePathArgument = new Argument<string>("file-path")
        {
            Description = "이미지가 포함된 PDF 파일 경로"
        };

        var visionTestCommand = new Command("test-vision", "OpenAI Vision을 사용한 PDF 이미지 텍스트 추출 테스트");
        visionTestCommand.Arguments.Add(filePathArgument);

        visionTestCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var filePath = parseResult.GetValue(filePathArgument);

            if (filePath != null)
            {
                using var host = CreateHost();
                await host.StartAsync(cancellationToken);
                var app = host.Services.GetRequiredService<FileFluxApp>();
                await app.TestVisionProcessingAsync(filePath);
                await host.StopAsync(cancellationToken);
            }
        });

        return visionTestCommand;
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // FileFlux services
                // 새 아키텍처: Reader/Parser 분리 
                services.AddFileFlux();

                // SQLite Database
                services.AddDbContext<FileFluxDbContext>(options =>
                    options.UseSqlite("Data Source=fileflux.db"));

                // OpenAI clients
                var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException(
                        "OPENAI_API_KEY 환경변수를 설정해주세요. " +
                        "예시: set OPENAI_API_KEY=your-api-key-here");
                }

                var openAiClient = new OpenAIClient(apiKey);
                services.AddSingleton(openAiClient.GetEmbeddingClient("text-embedding-3-small"));

                // Phase 5: OpenAI LLM 제공업체 등록 (소비 애플리케이션에서 구현)
                var chatClient = openAiClient.GetChatClient("gpt-5-nano");
                services.AddSingleton(chatClient);
                // LLM Provider를 직접 등록
                services.AddScoped<IDocumentAnalysisService, OpenAITextGenerationService>();

                // Phase 6: OpenAI Vision 서비스 등록 (소비 애플리케이션에서 구현)
                services.AddScoped<IImageToTextService>(provider => 
                    new OpenAiImageToTextService(apiKey));

                // Application services
                services.AddScoped<IVectorStoreService, VectorStoreService>();
                services.AddScoped<FileFluxApp>();
            })
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();
    }
}
