using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Mdm.Services;

public class BrowserDownloadRequest
{
    public string? Url { get; set; }
    public string? Filename { get; set; }
    public string? Referer { get; set; }
    public string? Cookies { get; set; }
    public string? UserAgent { get; set; }
    public string? Quality { get; set; }
    public bool IsAudioOnly { get; set; }
    public int Threads { get; set; } = 8;
    public string? RayId { get; set; }
    public bool IsDrmProtected { get; set; }
    public bool IsHoneypotDetected { get; set; }
    public bool IsDecoyCanvas { get; set; }
}

public class IpcServer
{
    public int HttpPort { get; }
    public string PipeName { get; }
    private readonly CancellationTokenSource _cts = new();
    private HttpListener? _httpListener;
    private NamedPipeServerStream? _currentPipe;

    public event EventHandler<BrowserDownloadRequest>? DownloadRequested;

    public IpcServer(int httpPort = 18888, string pipeName = "my_own_idm_ipc")
    {
        HttpPort = httpPort;
        PipeName = pipeName;
    }

    public void Start(bool startPipe = true)
    {
        StartHttpServer();
        if (startPipe)
        {
            StartNamedPipeServer();
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch { }

        try
        {
            _currentPipe?.Dispose();
        }
        catch { }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(50);
        }
        catch { }
    }

    private void StartHttpServer()
    {
        Task.Run(async () =>
        {
            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://127.0.0.1:{HttpPort}/");
                _httpListener.Start();
                Logger.Info($"HTTP IPC listener aktif di http://127.0.0.1:{HttpPort}/", "IPC");

                while (!_cts.IsCancellationRequested)
                {
                    var ctx = await _httpListener.GetContextAsync();
                    _ = ProcessHttpContextAsync(ctx);
                }
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Logger.Warn($"HTTP IPC error: {ex.Message}", "IPC");
            }
        });
    }

    private async Task ProcessHttpContextAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;

        // CORS headers
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        resp.Headers.Add("Access-Control-Allow-Private-Network", "true");
        resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Range, X-Requested-With, *");

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 204;
            resp.Close();
            return;
        }

        if (req.HttpMethod == "GET")
        {
            byte[] b = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"service\":\"MDM Turbo Desktop\",\"version\":\"1.0.0\"}");
            resp.ContentType = "application/json";
            resp.ContentLength64 = b.Length;
            await resp.OutputStream.WriteAsync(b);
            resp.Close();
            return;
        }

        if (req.HttpMethod == "POST")
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            string body = await reader.ReadToEndAsync();

            try
            {
                string path = req.Url?.AbsolutePath?.ToLowerInvariant() ?? "";
                if (path == "/error-report")
                {
                    var errJson = JsonDocument.Parse(body).RootElement;
                    string errType = errJson.TryGetProperty("error_type", out var et) ? et.GetString() ?? "Unknown" : "Unknown";
                    string errMsg = errJson.TryGetProperty("message", out var em) ? em.GetString() ?? "" : "";
                    string? rayId = errJson.TryGetProperty("ray_id", out var ry) ? ry.GetString() : null;
                    Logger.Warn($"Diagnostik dari ekstensi browser [{errType}]: {errMsg}", "EXT", rayId);

                    byte[] reply = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    resp.ContentType = "application/json";
                    resp.ContentLength64 = reply.Length;
                    await resp.OutputStream.WriteAsync(reply);
                    resp.Close();
                    return;
                }

                if (path == "/log")
                {
                    var logJson = JsonDocument.Parse(body).RootElement;
                    string level = logJson.TryGetProperty("level", out var l) ? l.GetString()?.ToUpperInvariant() ?? "INFO" : "INFO";
                    string logMsg = logJson.TryGetProperty("message", out var lm) ? lm.GetString() ?? "" : "";
                    string comp = logJson.TryGetProperty("component", out var cp) ? cp.GetString() ?? "EXT" : "EXT";
                    string? rayId = logJson.TryGetProperty("ray_id", out var ry) ? ry.GetString() : null;
                    if (level == "DEBUG") Logger.Debug(logMsg, comp, rayId);
                    else if (level == "WARN") Logger.Warn(logMsg, comp, rayId);
                    else if (level == "ERROR") Logger.Error(logMsg, null, comp, rayId);
                    else Logger.Info(logMsg, comp, rayId);

                    byte[] reply = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    resp.ContentType = "application/json";
                    resp.ContentLength64 = reply.Length;
                    await resp.OutputStream.WriteAsync(reply);
                    resp.Close();
                    return;
                }

                var json = JsonDocument.Parse(body).RootElement;
                string? url = json.TryGetProperty("url", out var u) ? u.GetString() : null;
                string? filename = json.TryGetProperty("filename", out var f) ? f.GetString() : null;
                string? quality = json.TryGetProperty("quality", out var q) ? q.GetString() : null;
                bool isAudioOnly = json.TryGetProperty("is_audio_only", out var ao) && ao.GetBoolean();
                int threads = json.TryGetProperty("threads", out var th) && th.TryGetInt32(out int t) ? t : 8;
                bool isDrmProtected = json.TryGetProperty("is_drm_protected", out var dp) && dp.GetBoolean();
                bool isHoneypot = json.TryGetProperty("is_honeypot", out var hp) && hp.GetBoolean();
                bool isDecoyCanvas = json.TryGetProperty("is_decoy_canvas", out var dc) && dc.GetBoolean();
                string? rayIdVal = json.TryGetProperty("ray_id", out var rId) ? rId.GetString() : null;
                string assignedRayId = !string.IsNullOrWhiteSpace(rayIdVal) ? rayIdVal : Logger.NewRayId("ray_ext");
                string? referer = null;
                string? cookies = null;
                string? userAgent = null;

                if (json.TryGetProperty("headers", out var headers) && headers.ValueKind == JsonValueKind.Object)
                {
                    if (headers.TryGetProperty("referer", out var r) || headers.TryGetProperty("Referer", out r))
                    {
                        referer = r.GetString();
                    }
                    if (headers.TryGetProperty("cookie", out var c) || headers.TryGetProperty("Cookie", out c))
                    {
                        cookies = c.GetString();
                    }
                    if (headers.TryGetProperty("user-agent", out var ua) || headers.TryGetProperty("User-Agent", out ua))
                    {
                        userAgent = ua.GetString();
                    }
                }

                if (!string.IsNullOrEmpty(url))
                {
                    string protTag = (isDrmProtected ? ", DRM" : "") + (isHoneypot ? ", Honeypot" : "") + (isDecoyCanvas ? ", Canvas" : "");
                    Logger.Info($"Permintaan unduhan diterima via HTTP IPC: {url} (filename={filename ?? "auto-detect"}{protTag})", "IPC", assignedRayId);
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            DownloadRequested?.Invoke(this, new BrowserDownloadRequest
                            {
                                Url = url,
                                Filename = filename,
                                Referer = referer,
                                Cookies = cookies,
                                UserAgent = userAgent,
                                Quality = quality,
                                IsAudioOnly = isAudioOnly,
                                Threads = threads,
                                RayId = assignedRayId,
                                IsDrmProtected = isDrmProtected,
                                IsHoneypotDetected = isHoneypot,
                                IsDecoyCanvas = isDecoyCanvas
                            });
                        }
                        catch { }
                    });
                }

                byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { status = "ok", ray_id = assignedRayId }));
                resp.ContentType = "application/json";
                resp.ContentLength64 = b.Length;
                await resp.OutputStream.WriteAsync(b);
                resp.Close();
                return;
            }
            catch { }

            byte[] fallback = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            resp.ContentType = "application/json";
            resp.ContentLength64 = fallback.Length;
            await resp.OutputStream.WriteAsync(fallback);
            resp.Close();
            return;
        }

        resp.StatusCode = 404;
        resp.Close();
    }

    private void StartNamedPipeServer()
    {
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    _currentPipe = pipe;

                    await pipe.WaitForConnectionAsync(_cts.Token);

                    byte[] lenBuf = new byte[4];
                    int read = await pipe.ReadAsync(lenBuf, 0, 4);
                    if (read == 4)
                    {
                        int msgLen = BitConverter.ToInt32(lenBuf, 0);
                        if (msgLen is > 0 and < 10 * 1024 * 1024)
                        {
                            byte[] msgBuf = new byte[msgLen];
                            await pipe.ReadExactlyAsync(msgBuf, 0, msgLen);
                            string jsonStr = Encoding.UTF8.GetString(msgBuf);

                            try
                            {
                                var json = JsonDocument.Parse(jsonStr).RootElement;
                                string? url = json.TryGetProperty("url", out var u) ? u.GetString() : null;
                                string? filename = json.TryGetProperty("filename", out var f) ? f.GetString() : null;
                                string? quality = json.TryGetProperty("quality", out var q) ? q.GetString() : null;
                                string? referer = json.TryGetProperty("referer", out var r) ? r.GetString() : null;
                                bool isDrmProtected = json.TryGetProperty("is_drm_protected", out var dp) && dp.GetBoolean();
                                bool isHoneypot = json.TryGetProperty("is_honeypot", out var hp) && hp.GetBoolean();
                                bool isDecoyCanvas = json.TryGetProperty("is_decoy_canvas", out var dc) && dc.GetBoolean();
                                string? rayIdVal = json.TryGetProperty("ray_id", out var rId) ? rId.GetString() : null;
                                string assignedRayId = !string.IsNullOrWhiteSpace(rayIdVal) ? rayIdVal : Logger.NewRayId("ray_ipc");

                                if (!string.IsNullOrEmpty(url))
                                {
                                    string protTag = (isDrmProtected ? ", DRM" : "") + (isHoneypot ? ", Honeypot" : "") + (isDecoyCanvas ? ", Canvas" : "");
                                    Logger.Info($"Permintaan unduhan diterima via Named Pipe IPC: {url} (filename={filename ?? "auto-detect"}{protTag})", "IPC", assignedRayId);
                                    DownloadRequested?.Invoke(this, new BrowserDownloadRequest
                                    {
                                        Url = url,
                                        Filename = filename,
                                        Quality = quality,
                                        Referer = referer,
                                        RayId = assignedRayId,
                                        IsDrmProtected = isDrmProtected,
                                        IsHoneypotDetected = isHoneypot,
                                        IsDecoyCanvas = isDecoyCanvas
                                    });
                                }

                                byte[] reply = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { status = "ok", ray_id = assignedRayId }));
                                byte[] replyLen = BitConverter.GetBytes(reply.Length);
                                await pipe.WriteAsync(replyLen);
                                await pipe.WriteAsync(reply);
                            }
                            catch { }
                        }
                    }
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    private static void DebugWriteLine(string msg) => System.Diagnostics.Debug.WriteLine(msg);
}

