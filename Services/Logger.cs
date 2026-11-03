using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace Mdm.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

public record LogEntry(
    DateTime Timestamp,
    LogLevel Level,
    string? Component,
    string Message,
    string? ExceptionDetails
)
{
    public string Formatted =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level.ToString().ToUpperInvariant()}]" +
        (!string.IsNullOrEmpty(Component) ? $" [{Component}]" : "") +
        $" {Message}" +
        (!string.IsNullOrEmpty(ExceptionDetails) ? $" | Exception: {ExceptionDetails}" : "");
}

public static class Logger
{
    private const int MaxRingBufferLines = 2000;
    private static readonly ConcurrentQueue<LogEntry> RingBuffer = new();
    private static readonly object FileLock = new();
    private static readonly string LogDir;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    private static readonly Regex TokenRegex = new(
        @"(?i)(token|auth|api_key|secret|signature|key|password)=([^&\s]+)",
        RegexOptions.Compiled
    );

    private static readonly Regex UserPathRegex = new(
        @"(?i)(C:\\Users\\)[^\\]+(\\)",
        RegexOptions.Compiled
    );

    static Logger()
    {
        LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MDM",
            "logs"
        );

        try
        {
            if (!Directory.Exists(LogDir))
            {
                Directory.CreateDirectory(LogDir);
            }
        }
        catch { }
    }

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Mask query tokens
        string sanitized = TokenRegex.Replace(input, "$1=***");

        // Mask user home directories
        sanitized = UserPathRegex.Replace(sanitized, "$1***$2");

        return sanitized;
    }

    public static void Debug(string message, string? component = null) => Log(LogLevel.Debug, message, component);
    public static void Info(string message, string? component = null) => Log(LogLevel.Info, message, component);
    public static void Warn(string message, string? component = null) => Log(LogLevel.Warn, message, component);
    public static void Error(string message, Exception? ex = null, string? component = null) => Log(LogLevel.Error, message, component, ex);

    public static void Log(LogLevel level, string message, string? component = null, Exception? ex = null)
    {
        if (level < MinimumLevel) return;

        string sanitizedMsg = Sanitize(message);
        string? exText = ex != null ? $"{ex.GetType().Name}: {Sanitize(ex.Message)}" : null;

        var entry = new LogEntry(DateTime.Now, level, component, sanitizedMsg, exText);
        string formattedLine = entry.Formatted;

        // In-memory ring buffer
        RingBuffer.Enqueue(entry);
        while (RingBuffer.Count > MaxRingBufferLines && RingBuffer.TryDequeue(out _)) { }

        // Diagnostic trace for debugger/console
        System.Diagnostics.Trace.WriteLine(formattedLine);

        // File output with daily rotation
        try
        {
            string fileName = $"mdm_{DateTime.Now:yyyyMMdd}.log";
            string filePath = Path.Combine(LogDir, fileName);

            lock (FileLock)
            {
                File.AppendAllText(filePath, formattedLine + Environment.NewLine);
            }
        }
        catch { }
    }

    public static List<string> GetRecentLogs(int count = 100, LogLevel minLevel = LogLevel.Debug)
    {
        return RingBuffer
            .Where(e => e.Level >= minLevel)
            .TakeLast(count)
            .Select(e => e.Formatted)
            .ToList();
    }

    public static List<LogEntry> GetRecentEntries(int count = 100, LogLevel minLevel = LogLevel.Debug)
    {
        return RingBuffer
            .Where(e => e.Level >= minLevel)
            .TakeLast(count)
            .ToList();
    }

    public static void ClearLogs() => RingBuffer.Clear();
}
