using FileFlux.Core;
using FileFlux.Infrastructure;
using FileFlux.SampleApp.Data;
using FileFlux.SampleApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.CommandLine;

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

        rootCommand.AddCommand(processCommand);
        rootCommand.AddCommand(processProgressCommand);
        rootCommand.AddCommand(queryCommand);
        rootCommand.AddCommand(listCommand);
        rootCommand.AddCommand(historyCommand);
        rootCommand.AddCommand(readersCommand);
        rootCommand.AddCommand(benchmarkCommand);

        return await rootCommand.InvokeAsync(args);
    }

    private static Command CreateProcessCommand()
    {
        var filePathArgument = new Argument<string>(
            name: "file-path",
            description: "처리할 파일 경로");

        var strategyOption = new Option<string>(
            name: "--strategy",
            description: "청킹 전략",
            getDefaultValue: () => "Intelligent")
        {
            AllowMultipleArgumentsPerToken = false
        };

        var processCommand = new Command("process", "문서를 처리하여 벡터 스토어에 저장")
        {
            filePathArgument,
            strategyOption
        };

        processCommand.SetHandler(async (filePath, strategy) =>
        {
            using var host = CreateHost();
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ProcessDocumentAsync(filePath, strategy);
            await host.StopAsync();
        }, filePathArgument, strategyOption);

        return processCommand;
    }

    private static Command CreateProcessWithProgressCommand()
    {
        var filePathArgument = new Argument<string>(
            name: "file-path",
            description: "처리할 파일 경로");

        var strategyOption = new Option<string>(
            name: "--strategy",
            description: "청킹 전략",
            getDefaultValue: () => "Intelligent")
        {
            AllowMultipleArgumentsPerToken = false
        };

        var processProgressCommand = new Command("process-progress", "문서를 진행률 추적과 함께 처리하여 벡터 스토어에 저장")
        {
            filePathArgument,
            strategyOption
        };

        processProgressCommand.SetHandler(async (filePath, strategy) =>
        {
            using var host = CreateHost();
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ProcessDocumentWithProgressAsync(filePath, strategy);
            await host.StopAsync();
        }, filePathArgument, strategyOption);

        return processProgressCommand;
    }

    private static Command CreateQueryCommand()
    {
        var queryArgument = new Argument<string>(
            name: "query",
            description: "검색할 쿼리");

        var topKOption = new Option<int>(
            name: "--top-k",
            description: "반환할 최대 결과 수",
            getDefaultValue: () => 5);

        var queryCommand = new Command("query", "RAG 쿼리 실행")
        {
            queryArgument,
            topKOption
        };

        queryCommand.SetHandler(async (query, topK) =>
        {
            using var host = CreateHost();
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ExecuteQueryAsync(query, topK);
            await host.StopAsync();
        }, queryArgument, topKOption);

        return queryCommand;
    }

    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "저장된 문서 목록 조회");

        listCommand.SetHandler(async () =>
        {
            using var host = CreateHost();
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ListDocumentsAsync();
            await host.StopAsync();
        });

        return listCommand;
    }

    private static Command CreateHistoryCommand()
    {
        var limitOption = new Option<int>(
            name: "--limit",
            description: "표시할 쿼리 기록 수",
            getDefaultValue: () => 10);

        var historyCommand = new Command("history", "쿼리 기록 조회")
        {
            limitOption
        };

        historyCommand.SetHandler(async (limit) =>
        {
            using var host = CreateHost();
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.ShowQueryHistoryAsync(limit);
            await host.StopAsync();
        }, limitOption);

        return historyCommand;
    }


    private static Command CreateReadersTestCommand()
    {
        var folderPathArgument = new Argument<string>(
            name: "folder-path",
            description: "테스트할 문서들이 있는 폴더 경로");

        var readersCommand = new Command("test-readers", "전문 문서 리더 테스트 (Word, Excel, PDF, HTML)")
        {
            folderPathArgument
        };

        readersCommand.SetHandler(async (folderPath) =>
        {
            using var host = CreateHost();
            await host.StartAsync();
            var app = host.Services.GetRequiredService<FileFluxApp>();
            await app.TestProfessionalReadersAsync(folderPath);
            await host.StopAsync();
        }, folderPathArgument);

        return readersCommand;
    }

    private static Command CreateBenchmarkCommand()
    {
        var testDirOption = new Option<string>(
            name: "--test-dir",
            description: "테스트 파일이 있는 디렉토리 경로",
            getDefaultValue: () => @"D:\data\FileFlux\test");

        var benchmarkCommand = new Command("benchmark", "종합 벤치마크 실행 - 모든 테스트 파일에 대한 성능 측정")
        {
            testDirOption
        };

        benchmarkCommand.SetHandler(async (testDir) =>
        {
            await BenchmarkProgram.RunBenchmarkAsync(new[] { testDir });
        }, testDirOption);

        return benchmarkCommand;
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
                var chatClient = openAiClient.GetChatClient("gpt-4o-mini");
                services.AddSingleton(chatClient);
                // LLM Provider를 직접 등록
                services.AddScoped<ITextCompletionService, OpenAiTextCompletionService>();

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
