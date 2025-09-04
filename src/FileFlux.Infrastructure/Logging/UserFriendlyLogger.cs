using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FileFlux.Infrastructure.Logging;

/// <summary>
/// 사용자 친화적인 로그 메시지를 파일로 저장하는 로거
/// 기술적 세부사항을 제외하고 사용자가 이해하기 쉬운 진행 상황을 기록
/// </summary>
public class UserFriendlyLogger : IDisposable
{
    private readonly StreamWriter _logWriter;
    private readonly string _logFilePath;
    private readonly object _lockObject = new object();

    public UserFriendlyLogger(string logDirectoryPath, string fileName = "processing-log.txt")
    {
        if (!Directory.Exists(logDirectoryPath))
        {
            Directory.CreateDirectory(logDirectoryPath);
        }

        _logFilePath = Path.Combine(logDirectoryPath, fileName);
        _logWriter = new StreamWriter(_logFilePath, append: false, encoding: System.Text.Encoding.UTF8);

        WriteHeader();
    }

    private void WriteHeader()
    {
        lock (_lockObject)
        {
            _logWriter.WriteLine("=".PadRight(80, '='));
            _logWriter.WriteLine($"FileFlux 문서 처리 로그");
            _logWriter.WriteLine($"시작 시간: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logWriter.WriteLine("=".PadRight(80, '='));
            _logWriter.WriteLine();
            _logWriter.Flush();
        }
    }

    /// <summary>
    /// 처리 시작 알림
    /// </summary>
    public void LogProcessingStart(string fileName, long fileSize)
    {
        var message = $"📄 문서 처리 시작: {fileName}";
        if (fileSize > 0)
        {
            message += $" (크기: {FormatFileSize(fileSize)})";
        }

        WriteLog("INFO", message);
    }

    /// <summary>
    /// 단계별 진행 상황
    /// </summary>
    public void LogStage(string stage, string message, double progressPercent = -1)
    {
        var icon = GetStageIcon(stage);
        var logMessage = $"{icon} {stage}";

        if (progressPercent >= 0)
        {
            logMessage += $" ({progressPercent:F1}%)";
        }

        logMessage += $": {message}";

        WriteLog("PROGRESS", logMessage);
    }

    /// <summary>
    /// 성공 메시지
    /// </summary>
    public void LogSuccess(string message)
    {
        WriteLog("SUCCESS", $"✅ {message}");
    }

    /// <summary>
    /// 경고 메시지
    /// </summary>
    public void LogWarning(string message)
    {
        WriteLog("WARNING", $"⚠️  {message}");
    }

    /// <summary>
    /// 오류 메시지
    /// </summary>
    public void LogError(string message, Exception? exception = null)
    {
        var logMessage = $"❌ {message}";
        if (exception != null)
        {
            logMessage += $"\n   세부 오류: {exception.Message}";
        }

        WriteLog("ERROR", logMessage);
    }

    /// <summary>
    /// 통계 정보
    /// </summary>
    public void LogStatistics(object statistics)
    {
        WriteLog("STATS", $"📊 처리 통계:");

        var json = JsonSerializer.Serialize(statistics, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        // JSON을 더 읽기 쉽게 포맷
        var lines = json.Split('\n');
        foreach (var line in lines)
        {
            WriteRawLine($"   {line}");
        }
    }

    /// <summary>
    /// 처리 완료
    /// </summary>
    public void LogProcessingComplete(string fileName, int chunkCount, TimeSpan duration)
    {
        WriteLog("SUCCESS", $"🎉 처리 완료: {fileName}");
        WriteRawLine($"   • 생성된 청크: {chunkCount}개");
        WriteRawLine($"   • 소요 시간: {FormatDuration(duration)}");
        WriteRawLine("");
    }

    private void WriteLog(string level, string message)
    {
        lock (_lockObject)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logWriter.WriteLine($"[{timestamp}] [{level}] {message}");
            _logWriter.Flush();
        }
    }

    private void WriteRawLine(string line)
    {
        lock (_lockObject)
        {
            _logWriter.WriteLine(line);
            _logWriter.Flush();
        }
    }

    private static string GetStageIcon(string stage)
    {
        return stage.ToLower() switch
        {
            "reading" => "📖",
            "extracting" => "🔍",
            "parsing" => "⚙️",
            "chunking" => "✂️",
            "validating" => "✔️",
            "completed" => "🏁",
            _ => "📋"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.TotalMinutes:F1}분";
        }
        else
        {
            return $"{duration.TotalSeconds:F1}초";
        }
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            WriteRawLine("");
            WriteRawLine("=".PadRight(80, '='));
            WriteRawLine($"로그 종료: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteRawLine("=".PadRight(80, '='));

            _logWriter?.Dispose();
        }
    }
}