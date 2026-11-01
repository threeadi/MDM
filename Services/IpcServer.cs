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

                while (!_cts.IsCancellationRequested)
                {
                    var ctx = await _httpListener.GetContextAsync();
                    _ = ProcessHttpContextAsync(ctx);
                }
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                DebugWriteLine($"HTTP IPC error: {ex.Message}");
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
                var json = JsonDocument.Parse(body).RootElement;
                string? url = json.TryGetProperty("url", out var u) ? u.GetString() : null;
                string? filename = json.TryGetProperty("filename", out var f) ? f.GetString() : null;
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
                    DownloadRequested?.Invoke(this, new BrowserDownloadRequest
                    {
                        Url = url,
                        Filename = filename,
                        Referer = referer,
                        Cookies = cookies,
                        UserAgent = userAgent
                    });
                }
            }
            catch { }

            byte[] b = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            resp.ContentType = "application/json";
            resp.ContentLength64 = b.Length;
            await resp.OutputStream.WriteAsync(b);
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

                                if (!string.IsNullOrEmpty(url))
                                {
                                    DownloadRequested?.Invoke(this, new BrowserDownloadRequest
                                    {
                                        Url = url,
                                        Filename = filename
                                    });
                                }

                                byte[] reply = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
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

