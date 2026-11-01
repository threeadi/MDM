using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace Mdm.Services;

public static class Logger
{
    private const int MaxRingBufferLines = 1000;
    private static readonly ConcurrentQueue<string> RingBuffer = new();
    private static readonly object FileLock = new();
    private static readonly string LogDir;

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

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}" : message;
        Log("ERROR", text);
    }

    private static void Log(string level, string message)
    {
        string sanitizedMsg = Sanitize(message);
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {sanitizedMsg}";

        // In-memory ring buffer
        RingBuffer.Enqueue(line);
        while (RingBuffer.Count > MaxRingBufferLines && RingBuffer.TryDequeue(out _)) { }

        // File output with daily rotation
        try
        {
            string fileName = $"mdm_{DateTime.Now:yyyyMMdd}.log";
            string filePath = Path.Combine(LogDir, fileName);

            lock (FileLock)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch { }
    }

    public static List<string> GetRecentLogs(int count = 100)
    {
        return RingBuffer.TakeLast(count).ToList();
    }
}

