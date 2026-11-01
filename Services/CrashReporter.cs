using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mdm.Services;

public static class CrashReporter
{
    private const string GlitchTipStoreUrl = "https://app.glitchtip.com/api/28025/store/";
    private const string ProjectKey = "2e56fa98dc824baaa843dff62a18b3d2";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static void Initialize()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.Error("Fatal unhandled AppDomain exception", ex);
                SendCrashReport(ex, "AppDomain.UnhandledException");
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Logger.Error("Unobserved Task exception", e.Exception);
            SendCrashReport(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
    }

    public static void SendCrashReport(Exception ex, string? context = null)
    {
        Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    event_id = Guid.NewGuid().ToString("N"),
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    platform = "csharp",
                    level = "error",
                    logger = "MDM.CrashReporter",
                    release = "mdm@1.0.0-net10",
                    environment = "production",
                    message = Logger.Sanitize($"{context ?? "Exception"}: {ex.Message}"),
                    exception = new
                    {
                        values = new[]
                        {
                            new
                            {
                                type = ex.GetType().FullName,
                                value = Logger.Sanitize(ex.Message),
                                stacktrace = new
                                {
                                    frames = (ex.StackTrace ?? string.Empty)
                                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(f => new { filename = Logger.Sanitize(f.Trim()) })
                                        .ToArray()
                                }
                            }
                        }
                    },
                    extra = new
                    {
                        context,
                        os = Environment.OSVersion.ToString(),
                        dotnet_version = Environment.Version.ToString(),
                        recent_logs = Logger.GetRecentLogs(30)
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, GlitchTipStoreUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                req.Headers.Add("X-Sentry-Auth", $"Sentry sentry_version=7, sentry_client=mdm-net10/1.0.0, sentry_key={ProjectKey}");

                using var resp = await HttpClient.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    Logger.Info("Laporan crash/diagnostik berhasil dikirim ke GlitchTip.");
                }
            }
            catch (Exception sendEx)
            {
                Logger.Warn($"Gagal mengirim laporan crash ke GlitchTip: {sendEx.Message}");
            }
        });
    }
}

