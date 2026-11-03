using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Mdm.Models;

namespace Mdm.Services;

public class DownloadProgressEventArgs : EventArgs
{
    public required string TaskId { get; init; }
    public long DownloadedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public long SpeedBps { get; init; }
    public long? EtaSeconds { get; init; }
    public required List<DownloadSegment> Segments { get; init; }
}

public class ProbeResult
{
    public string Filename { get; set; } = string.Empty;
    public long? TotalBytes { get; set; }
    public bool SupportsRange { get; set; }
    public string Category { get; set; } = "Umum";
    public string? ContentType { get; set; }
    public bool IsHls { get; set; }
    public bool IsDrmProtected { get; set; }
    public string? DrmType { get; set; }
    public bool IsHoneypotDetected { get; set; }
    public string? HoneypotReason { get; set; }
    public bool IsDecoyCanvas { get; set; }
}

public static class HoneypotDetector
{
    public static double CalculateShannonEntropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return 0.0;

        Span<int> counts = stackalloc int[256];
        foreach (byte b in data)
        {
            counts[b]++;
        }

        double entropy = 0.0;
        double total = data.Length;
        for (int i = 0; i < 256; i++)
        {
            if (counts[i] == 0) continue;
            double p = counts[i] / total;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    public static bool HasRepetitiveFiller(ReadOnlySpan<byte> data, out double ratio)
    {
        ratio = 0.0;
        if (data.Length == 0) return false;

        Span<int> counts = stackalloc int[256];
        int maxCount = 0;
        foreach (byte b in data)
        {
            int c = ++counts[b];
            if (c > maxCount) maxCount = c;
        }

        ratio = maxCount / (double)data.Length;
        return ratio >= 0.50;
    }

    public static bool HasValidMediaContainerHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return false;

        // MP4 / QuickTime / ISO-BMFF: box at 4..8 is ftyp, moov, mdat, free, or skip
        if (data.Length >= 8)
        {
            var boxType = data.Slice(4, 4);
            if (boxType.SequenceEqual("ftyp"u8) ||
                boxType.SequenceEqual("moov"u8) ||
                boxType.SequenceEqual("mdat"u8) ||
                boxType.SequenceEqual("free"u8) ||
                boxType.SequenceEqual("skip"u8) ||
                boxType.SequenceEqual("wide"u8))
            {
                return true;
            }
        }

        // Search within first 64 bytes for ftyp or moov (some MP4 streams have small lead-in offsets)
        if (data.Length >= 32)
        {
            var lead = data.Slice(0, Math.Min(64, data.Length));
            if (lead.IndexOf("ftyp"u8) >= 0 || lead.IndexOf("moov"u8) >= 0)
            {
                return true;
            }
        }

        // WebM / Matroska (EBML: 0x1A 0x45 0xDF 0xA3)
        if (data.Length >= 4 && data[0] == 0x1A && data[1] == 0x45 && data[2] == 0xDF && data[3] == 0xA3)
        {
            return true;
        }

        // MPEG-TS (Sync byte 0x47 at 0 and 188)
        if (data.Length >= 189 && data[0] == 0x47 && data[188] == 0x47)
        {
            return true;
        }

        // FLV header
        if (data.Length >= 3 && data[0] == (byte)'F' && data[1] == (byte)'L' && data[2] == (byte)'V')
        {
            return true;
        }

        // AVI / WAV (RIFF header)
        if (data.Length >= 12 && data.Slice(0, 4).SequenceEqual("RIFF"u8))
        {
            var riffType = data.Slice(8, 4);
            if (riffType.SequenceEqual("AVI "u8) || riffType.SequenceEqual("WAVE"u8))
            {
                return true;
            }
        }

        // Ogg header
        if (data.Length >= 4 && data.Slice(0, 4).SequenceEqual("OggS"u8))
        {
            return true;
        }

        return false;
    }

    public static bool IsHtmlErrorPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < 15) return false;
        var slice = data.Slice(0, Math.Min(256, data.Length));
        if (slice.IndexOf("<!DOCTYPE html"u8) >= 0 ||
            slice.IndexOf("<html"u8) >= 0 ||
            slice.IndexOf("<HTML"u8) >= 0 ||
            slice.IndexOf("<head"u8) >= 0)
        {
            return true;
        }
        return false;
    }

    public static bool DetectCdnBotMitigationHeaders(HttpResponseMessage resp, out string? headerDetail)
    {
        headerDetail = null;
        var headers = resp.Headers;

        // Cloudflare: check for challenge or block
        if (headers.TryGetValues("cf-mitigated", out var cfVals))
        {
            string val = string.Join(" ", cfVals);
            if (val.Contains("challenge", StringComparison.OrdinalIgnoreCase) || val.Contains("block", StringComparison.OrdinalIgnoreCase))
            {
                headerDetail = "Cloudflare Challenge / Block Active";
                return true;
            }
        }
        if (headers.Contains("cf-chl-bypass"))
        {
            headerDetail = "Cloudflare Challenge Bypass Required";
            return true;
        }

        // DataDome: check for block
        if (headers.TryGetValues("X-Datadome", out var ddVals))
        {
            string val = string.Join(" ", ddVals);
            if (val.Contains("block", StringComparison.OrdinalIgnoreCase) || val.Contains("captcha", StringComparison.OrdinalIgnoreCase))
            {
                headerDetail = "DataDome Bot Block";
                return true;
            }
        }

        if (headers.Contains("X-Challenge"))
        {
            headerDetail = "WAF Challenge Header";
            return true;
        }

        return false;
    }

    public static bool DetectHoneypot(HttpResponseMessage? resp, ReadOnlySpan<byte> sampleBytes, out string? reason)
    {
        reason = null;

        // 1. CDN Bot Mitigation Challenge Headers
        if (resp != null && DetectCdnBotMitigationHeaders(resp, out var headerDetail))
        {
            reason = $"CDN bot mitigation terdeteksi ({headerDetail})";
            return true;
        }

        // 2. Server returned HTML error/captcha page instead of video binary
        if (sampleBytes.Length >= 15 && IsHtmlErrorPayload(sampleBytes))
        {
            reason = "Server merespons dokumen HTML (bukan berkas media), terindikasi halaman blokir / bot challenge";
            return true;
        }

        // 3. Valid media containers (MP4, MKV, WebM, TS) naturally have zero padding in 'free' or table atoms
        if (HasValidMediaContainerHeader(sampleBytes))
        {
            return false;
        }

        // 4. Raw uncontainerized stream with abnormal filler or very low entropy
        if (sampleBytes.Length >= 64)
        {
            if (HasRepetitiveFiller(sampleBytes, out double ratio))
            {
                reason = $"Pola byte berulang/filler abnormal tanpa header media ({ratio * 100:0.#}% byte identik, indikasi dummy stream)";
                return true;
            }

            double entropy = CalculateShannonEntropy(sampleBytes);
            if (entropy < 4.0)
            {
                reason = $"Entropi data sangat rendah tanpa header media ({entropy:0.00} bit/byte) - terindikasi dummy honeypot stream";
                return true;
            }
        }

        return false;
    }
}

public static class DrmDetector
{
    private static readonly byte[] PsshBox = "pssh"u8.ToArray();
    private static readonly byte[] EncvBox = "encv"u8.ToArray();
    private static readonly byte[] EncaBox = "enca"u8.ToArray();
    private static readonly byte[] SinfBox = "sinf"u8.ToArray();

    public static bool DetectFromBytes(ReadOnlySpan<byte> data, out string? drmType)
    {
        drmType = null;
        if (data.Length < 8) return false;

        if (data.IndexOf(PsshBox) >= 0)
        {
            drmType = "MP4 pssh (Protection System Specific Header - CENC / Widevine / PlayReady)";
            return true;
        }
        if (data.IndexOf(EncvBox) >= 0)
        {
            drmType = "MP4 encv (Encrypted Video Track Sample)";
            return true;
        }
        if (data.IndexOf(EncaBox) >= 0)
        {
            drmType = "MP4 enca (Encrypted Audio Track Sample)";
            return true;
        }
        if (data.IndexOf(SinfBox) >= 0)
        {
            drmType = "MP4 sinf (Protection Scheme Information)";
            return true;
        }

        try
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            if (text.Contains("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Contains("METHOD=SAMPLE-AES", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("edef8ba9-79d6-4ace-a3c8-27dcd51d21ed", StringComparison.OrdinalIgnoreCase) || // Widevine
                    text.Contains("9a04f079-9840-4286-ab92-e65be0885f95", StringComparison.OrdinalIgnoreCase) || // PlayReady
                    text.Contains("METHOD=CBCS", StringComparison.OrdinalIgnoreCase))
                {
                    drmType = "HLS Sample-AES / Widevine DRM Key System";
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}

public static class CategoryHelper
{
    public static string DetectCategory(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return "Umum";
        int q = filename.IndexOf('?');
        if (q >= 0) filename = filename[..q];
        int hash = filename.IndexOf('#');
        if (hash >= 0) filename = filename[..hash];
        string ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext switch
        {
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".tgz" or ".zst" or ".cab" or ".iso" or ".img" or ".vmdk" => "Kompresi",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".csv" or ".rtf" or ".epub" or ".md" or ".odt" or ".ods" or ".odp" or ".json" or ".xml" or ".html" or ".htm" or ".log" => "Dokumen",
            ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".flv" or ".wmv" or ".m4v" or ".ts" or ".3gp" or ".vob" or ".mpg" or ".mpeg" => "Video",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma" or ".opus" or ".alac" or ".aiff" or ".mid" or ".midi" => "Musik",
            ".exe" or ".msi" or ".apk" or ".appx" or ".deb" or ".rpm" or ".bat" or ".cmd" or ".ps1" or ".jar" or ".dmg" or ".pkg" => "Program",
            _ => "Umum"
        };
    }
}

public class DownloadEngine
{
    public const long MinSplitSize = 2 * 1024 * 1024; // 2 MB minimum remaining to split

    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        MaxConnectionsPerServer = 64,
        EnableMultipleHttp2Connections = true
    })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTasks = new();
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _taskLimiters = new();
    private readonly Database _database;
    private readonly TelegramService _telegramService;

    public TokenBucketRateLimiter GlobalLimiter { get; } = new(0);
    public TelegramService TelegramService => _telegramService;

    public TokenBucketRateLimiter GetOrCreateTaskLimiter(string taskId, long initialBps = 0)
    {
        return _taskLimiters.GetOrAdd(taskId, _ => new TokenBucketRateLimiter(initialBps));
    }

    public void SetTaskSpeedLimit(string taskId, long bytesPerSecond)
    {
        var limiter = GetOrCreateTaskLimiter(taskId, bytesPerSecond);
        limiter.SetRate(bytesPerSecond);

        _database.UpdateTaskSpeedLimit(taskId, bytesPerSecond > 0 ? bytesPerSecond : null);
    }

    public event EventHandler<DownloadProgressEventArgs>? ProgressUpdated;
    public event EventHandler<string>? TaskCompleted;
    public event EventHandler<string>? TaskPaused;
    public event EventHandler<(string TaskId, string Error)>? TaskFailed;

    public DownloadEngine(Database database)
    {
        _database = database;
        _telegramService = new TelegramService(database);
    }

    public static string CleanStreamUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (!url.Contains("bytestart") && !url.Contains("byteend"))
        {
            return url;
        }

        try
        {
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            query.Remove("bytestart");
            query.Remove("byteend");
            var builder = new UriBuilder(uri)
            {
                Query = query.ToString()
            };
            return builder.Uri.ToString();
        }
        catch
        {
            return System.Text.RegularExpressions.Regex.Replace(url, @"[?&]bytestart=\d+", "")
                        .Replace(@"[?&]byteend=\d+", "");
        }
    }

    public static string GetEffectiveUrl(string url, string? referer)
    {
        string clean = CleanStreamUrl(url);
        if (clean.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(referer) && (referer.Contains("youtube.com") || referer.Contains("youtu.be")))
            {
                return referer;
            }
        }
        return clean;
    }

    public static Task<ProbeResult> ProbeUrlAsync(string url, string? referer, CancellationToken ct) =>
        ProbeUrlAsync(url, referer, null, ct);

    public static Task<ProbeResult> ProbeUrlAsync(string url, CancellationToken ct) =>
        ProbeUrlAsync(url, null, null, ct);

    public static async Task<ProbeResult> ProbeUrlAsync(string url, string? referer = null, string? quality = null, CancellationToken ct = default)
    {
        string effectiveUrl = GetEffectiveUrl(url, referer);

        if (YtDlpService.IsVideoPlatformUrl(effectiveUrl) || effectiveUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            var ytProbe = await YtDlpService.ProbeAsync(effectiveUrl, quality, ct);
            ytProbe.IsHls = true;
            if (ytProbe.Filename.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                ytProbe.Filename = Path.ChangeExtension(ytProbe.Filename, ".mp4");
            }
            return ytProbe;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, effectiveUrl);
        req.Headers.Range = new RangeHeaderValue(0, 4095);
        if (!string.IsNullOrEmpty(referer))
        {
            req.Headers.Referrer = new Uri(referer);
        }

        using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        long? totalBytes = null;
        bool supportsRange = resp.StatusCode == System.Net.HttpStatusCode.PartialContent;

        if (resp.Content.Headers.ContentRange?.Length.HasValue == true)
        {
            totalBytes = resp.Content.Headers.ContentRange.Length.Value;
        }
        else if (resp.Content.Headers.ContentLength.HasValue && resp.Content.Headers.ContentLength.Value > 1)
        {
            totalBytes = resp.Content.Headers.ContentLength.Value;
        }

        string detectedFilename = string.Empty;
        if (resp.Content.Headers.ContentDisposition?.FileNameStar is { Length: > 0 } fnStar)
        {
            detectedFilename = fnStar;
        }
        else if (resp.Content.Headers.ContentDisposition?.FileName is { Length: > 0 } fn)
        {
            detectedFilename = fn.Trim('"');
        }
        else
        {
            try
            {
                var uri = new Uri(effectiveUrl);
                detectedFilename = Path.GetFileName(uri.LocalPath);
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(detectedFilename))
        {
            detectedFilename = "download_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        detectedFilename = SanitizeFilename(detectedFilename);

        byte[] probeBuffer = ArrayPool<byte>.Shared.Rent(4096);
        int probeBytesRead = 0;
        try
        {
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            probeBytesRead = await stream.ReadAsync(probeBuffer.AsMemory(0, 4096), ct);
        }
        catch { }

        bool isDrm = false;
        string? drmType = null;
        if (probeBytesRead > 0 && DrmDetector.DetectFromBytes(probeBuffer.AsSpan(0, probeBytesRead), out drmType))
        {
            isDrm = true;
            Logger.Warn($"Proteksi DRM terdeteksi pada berkas '{detectedFilename}': {drmType}", "ENGINE");
        }

        bool isHoneypot = false;
        string? honeypotReason = null;
        if (HoneypotDetector.DetectHoneypot(resp, probeBuffer.AsSpan(0, probeBytesRead), out honeypotReason))
        {
            isHoneypot = true;
            Logger.Warn($"Honeypot / bot mitigation terdeteksi pada '{detectedFilename}': {honeypotReason}", "ENGINE");
        }
        ArrayPool<byte>.Shared.Return(probeBuffer);

        string? mediaType = resp.Content.Headers.ContentType?.MediaType;
        bool isHls = (mediaType != null && (mediaType.Contains("mpegurl") || mediaType.Contains("m3u8"))) ||
                     effectiveUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);

        return new ProbeResult
        {
            Filename = detectedFilename,
            TotalBytes = totalBytes,
            SupportsRange = supportsRange,
            Category = CategoryHelper.DetectCategory(detectedFilename),
            ContentType = mediaType,
            IsHls = isHls,
            IsDrmProtected = isDrm,
            DrmType = drmType,
            IsHoneypotDetected = isHoneypot,
            HoneypotReason = honeypotReason
        };
    }

    public async Task<DownloadTask> ProbeAndCreateTaskAsync(
        string url,
        string saveDir,
        string? filename = null,
        int connections = 8,
        string? referer = null,
        string? category = null,
        string? quality = null,
        string? rayId = null,
        bool isDrmProtected = false,
        bool isDecoyCanvas = false,
        bool isHoneypot = false)
    {
        string effectiveRayId = !string.IsNullOrWhiteSpace(rayId) ? rayId : Logger.NewRayId();
        using (Logger.WithRayId(effectiveRayId))
        {
            string effectiveUrl = GetEffectiveUrl(url, referer);
            var probe = await ProbeUrlAsync(effectiveUrl, referer, quality);

            string finalFilename = !string.IsNullOrWhiteSpace(filename) ? SanitizeFilename(filename) : probe.Filename;
            if (probe.IsHls || effectiveUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) || finalFilename.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                if (finalFilename.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                {
                    finalFilename = Path.ChangeExtension(finalFilename, ".mp4");
                }
            }
            string filePath = Path.Combine(saveDir, finalFilename);

            int workerCount = CalculateOptimalThreads(probe.TotalBytes, probe.SupportsRange, probe.IsHls, quality);
            var segments = CalculateSegments(probe.TotalBytes, workerCount);

            bool drm = probe.IsDrmProtected || isDrmProtected;
            bool honeypot = probe.IsHoneypotDetected || isHoneypot;
            bool decoyCanvas = isDecoyCanvas || probe.IsDecoyCanvas;

            string? protectionWarning = null;
            if (drm) protectionWarning = probe.DrmType ?? "Proteksi DRM Terdeteksi (Widevine/CENC)";
            else if (honeypot) protectionWarning = probe.HoneypotReason ?? "Honeypot / Stream Poisoning Terdeteksi";
            else if (decoyCanvas) protectionWarning = "Decoy Player Terdeteksi (Video dirender via Canvas / WebAssembly)";

            var task = new DownloadTask
            {
                Url = effectiveUrl,
                Filename = finalFilename,
                SaveDir = saveDir,
                FilePath = filePath,
                TotalBytes = probe.TotalBytes,
                DownloadedBytes = 0,
                Status = DownloadStatus.Downloading,
                Connections = workerCount,
                SupportsRange = probe.SupportsRange,
                Category = category ?? probe.Category,
                Segments = segments,
                Referer = referer,
                Quality = quality,
                IsHls = probe.IsHls,
                RayId = effectiveRayId,
                IsDrmProtected = drm,
                IsHoneypotDetected = honeypot,
                IsDecoyCanvas = decoyCanvas,
                ProtectionWarning = protectionWarning
            };

            _database.UpsertTask(task);
            return task;
        }
    }

    public static int CalculateOptimalThreads(long? totalBytes, bool supportsRange, bool isHls, string? quality = null)
    {
        if (!supportsRange || isHls) return 1;
        if (!totalBytes.HasValue || totalBytes.Value <= 0) return 8;

        long mb = totalBytes.Value / (1024 * 1024);
        if (mb >= 500) return 32;
        if (mb >= 100) return 24;
        if (mb >= 30) return 16;
        if (mb >= 10) return 8;
        if (mb >= 2) return 4;
        return 2;
    }

    public static List<DownloadSegment> CalculateSegments(long? totalBytes, int workerCount)
    {
        if (totalBytes is null or <= 0 || workerCount <= 1)
        {
            return [new DownloadSegment { Index = 0, StartByte = 0, EndByte = totalBytes ?? long.MaxValue }];
        }

        var list = new List<DownloadSegment>();
        long total = totalBytes.Value;
        long chunkSize = total / workerCount;

        for (int i = 0; i < workerCount; i++)
        {
            long start = i * chunkSize;
            long end = (i == workerCount - 1) ? total - 1 : (start + chunkSize - 1);
            list.Add(new DownloadSegment
            {
                Index = i,
                StartByte = start,
                EndByte = end,
                DownloadedBytes = 0,
                IsFinished = false
            });
        }

        return list;
    }

    public void StartDownload(DownloadTask task)
    {
        using (Logger.WithRayId(task.RayId))
        {
            if (task.IsDrmProtected)
            {
                Logger.Warn($"Tugas unduhan '{task.Id}' ({task.Filename}) teridentifikasi menggunakan enkripsi DRM. Video mungkin berupa stream terenkripsi (gambar statis/macet di player biasa).", "ENGINE", task.RayId);
            }
            Logger.Info($"Memulai unduhan tugas: {task.Id} ({task.Filename}) -> {task.FilePath}", "ENGINE", task.RayId);
            var cts = new CancellationTokenSource();
            _runningTasks[task.Id] = cts;
            task.Status = DownloadStatus.Downloading;
            _database.UpsertTask(task);

            Task.Run(async () =>
            {
                using (Logger.WithRayId(task.RayId))
                {
                    try
                    {
                        await ExecuteDownloadAsync(task, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warn($"Unduhan tugas dijeda/dibatalkan: {task.Id}", "ENGINE", task.RayId);
                        task.Status = DownloadStatus.Paused;
                        _database.UpsertTask(task);
                        TaskPaused?.Invoke(this, task.Id);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Tugas unduhan gagal: {task.Id} ({task.Filename}) - {ex.Message}", ex, "ENGINE", task.RayId);
                        task.Status = DownloadStatus.Failed;
                        task.ErrorMessage = ex.Message;
                        _database.UpsertTask(task);
                        TaskFailed?.Invoke(this, (task.Id, ex.Message));
                    }
                    finally
                    {
                        _runningTasks.TryRemove(task.Id, out _);
                    }
                }
            });
        }
    }

    public void PauseDownload(string taskId)
    {
        Logger.Warn($"Menjeda unduhan tugas: {taskId}", "ENGINE");
        if (_runningTasks.TryRemove(taskId, out var cts))
        {
            cts.Cancel();
        }
    }

    private async Task ExecuteDownloadAsync(DownloadTask task, CancellationToken ct)
    {
        string effectiveUrl = GetEffectiveUrl(task.Url, task.Referer);
        task.Url = effectiveUrl;

        if (effectiveUrl.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteTelegramDownloadAsync(task, ct);
            return;
        }

        bool isStream = YtDlpService.IsVideoPlatformUrl(effectiveUrl) || task.IsHls || effectiveUrl.Contains(".m3u8", StringComparison.OrdinalIgnoreCase);
        if (isStream)
        {
            await ExecuteYtDlpDownloadAsync(task, ct);
            return;
        }

        var taskLimiter = GetOrCreateTaskLimiter(task.Id, task.SpeedLimitBps ?? 0);
        if (task.SpeedLimitBps.HasValue)
        {
            taskLimiter.SetRate(task.SpeedLimitBps.Value);
        }

        using var writer = new SparseFileWriter(task.FilePath, task.TotalBytes);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<(int SegIndex, int Bytes)>();
        var dynamicEnds = new ConcurrentDictionary<int, long>();
        object splitLock = new();

        foreach (var seg in task.Segments)
        {
            dynamicEnds[seg.Index] = seg.EndByte;
        }

        var activeWorkers = new List<Task>();

        foreach (var seg in task.Segments.ToList())
        {
            if (seg.IsFinished) continue;

            activeWorkers.Add(Task.Run(async () =>
            {
                string workerRayId = $"{task.RayId}:w{seg.Index + 1:D2}";
                using (Logger.WithRayId(workerRayId))
                {
                    Logger.Debug($"Worker #{seg.Index + 1} aktif (rentang: {seg.StartByte}-{seg.EndByte})", "ENGINE", workerRayId);
                    var currentSeg = seg;
                    while (!ct.IsCancellationRequested)
                    {
                        await DownloadSegmentRangeAsync(task, currentSeg, writer, channel.Writer, dynamicEnds, taskLimiter, ct);
                        currentSeg.IsFinished = true;

                        if (!task.SupportsRange || task.TotalBytes is null or <= 0) break;

                        DownloadSegment? nextSeg = null;
                        lock (splitLock)
                        {
                            DownloadSegment? candidate = null;
                            long maxRemaining = 0;

                            foreach (var s in task.Segments)
                            {
                                if (s.IsFinished) continue;
                                if (!dynamicEnds.TryGetValue(s.Index, out long endBound)) continue;

                                long currentPos = s.StartByte + s.DownloadedBytes;
                                long remaining = endBound - currentPos + 1;
                                if (remaining >= MinSplitSize && remaining > maxRemaining)
                                {
                                    maxRemaining = remaining;
                                    candidate = s;
                                }
                            }

                            if (candidate != null && maxRemaining >= MinSplitSize)
                            {
                                long currentPos = candidate.StartByte + candidate.DownloadedBytes;
                                long oldEnd = dynamicEnds[candidate.Index];
                                long splitPoint = currentPos + (maxRemaining / 2);

                                dynamicEnds[candidate.Index] = splitPoint;
                                candidate.EndByte = splitPoint;

                                int newIdx = task.Segments.Count;
                                nextSeg = new DownloadSegment
                                {
                                    Index = newIdx,
                                    StartByte = splitPoint + 1,
                                    EndByte = oldEnd,
                                    DownloadedBytes = 0,
                                    IsFinished = false
                                };

                                task.Segments.Add(nextSeg);
                                dynamicEnds[newIdx] = oldEnd;
                            }
                        }

                        if (nextSeg != null)
                        {
                            currentSeg = nextSeg;
                            string newWorkerRayId = $"{task.RayId}:w{currentSeg.Index + 1:D2}";
                            Logger.Debug($"Segmen #{currentSeg.Index + 1} dialokasikan ke worker (split rentang: {currentSeg.StartByte}-{currentSeg.EndByte})", "ENGINE", newWorkerRayId);
                        }
                        else
                        {
                            break;
                        }
                    }
                    Logger.Debug($"Worker #{seg.Index + 1} selesai", "ENGINE", workerRayId);
                }
            }, ct));
        }

        long bytesSinceLastSpeedCalc = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastDbSave = Stopwatch.StartNew();
        long currentSpeedBps = 0;

        var progressReader = Task.Run(async () =>
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var item))
                {
                    task.DownloadedBytes += item.Bytes;
                    bytesSinceLastSpeedCalc += item.Bytes;

                    if (item.SegIndex < task.Segments.Count)
                    {
                        var s = task.Segments[item.SegIndex];
                        long expectedLen = s.EndByte >= s.StartByte && s.EndByte != long.MaxValue ? s.EndByte - s.StartByte + 1 : 0;
                        if (expectedLen > 0 && s.DownloadedBytes >= expectedLen)
                        {
                            s.IsFinished = true;
                        }
                    }
                }

                if (stopwatch.ElapsedMilliseconds >= 300)
                {
                    double seconds = stopwatch.Elapsed.TotalSeconds;
                    currentSpeedBps = seconds > 0 ? (long)(bytesSinceLastSpeedCalc / seconds) : 0;
                    bytesSinceLastSpeedCalc = 0;
                    stopwatch.Restart();

                    long? eta = null;
                    if (currentSpeedBps > 0 && task.TotalBytes is > 0)
                    {
                        long remaining = Math.Max(0, task.TotalBytes.Value - task.DownloadedBytes);
                        eta = remaining / currentSpeedBps;
                    }

                    ProgressUpdated?.Invoke(this, new DownloadProgressEventArgs
                    {
                        TaskId = task.Id,
                        DownloadedBytes = task.DownloadedBytes,
                        TotalBytes = task.TotalBytes,
                        SpeedBps = currentSpeedBps,
                        EtaSeconds = eta,
                        Segments = task.Segments.ToList()
                    });
                }

                if (lastDbSave.ElapsedMilliseconds >= 1500)
                {
                    _database.UpsertTask(task);
                    lastDbSave.Restart();
                }
            }
        }, ct);

        await Task.WhenAll(activeWorkers);
        channel.Writer.Complete();
        await progressReader;

        writer.Flush();

        // Safety check for corrupt/stub downloads:
        // If file is in Video category or has video extension, and downloaded bytes is tiny (< 10 KB)
        bool isVideo = task.Category == "Video" ||
                       task.Filename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                       task.Filename.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                       task.Filename.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);

        if (isVideo && task.DownloadedBytes < 10240)
        {
            task.Status = DownloadStatus.Failed;
            task.ErrorMessage = $"Berkas video tidak valid ({task.DownloadedBytes} bytes, kemungkinan tracking beacon atau token kedaluwarsa)";
            Logger.Warn($"Tugas unduhan gagal validasi ukuran video: {task.Id} ({task.Filename}), hanya {task.DownloadedBytes} bytes", "ENGINE");
            _database.UpsertTask(task);
            TaskFailed?.Invoke(this, (task.Id, task.ErrorMessage));
            return;
        }

        if (isVideo && File.Exists(task.FilePath))
        {
            try
            {
                using var fs = File.OpenRead(task.FilePath);
                byte[] sample = new byte[Math.Min(1024, (int)fs.Length)];
                int read = fs.Read(sample, 0, sample.Length);
                if (read >= 8 && SteganographyDecrypter.IsSteganographyDecoy(sample.AsSpan(0, read)))
                {
                    Logger.Info($"Mencoba membongkar decoy steganografi untuk unduhan '{task.Filename}'...", "ENGINE");
                    bool decrypted = await SteganographyDecrypter.TryDecryptInPlaceAsync(task.FilePath);
                    if (decrypted)
                    {
                        long actualLen = new FileInfo(task.FilePath).Length;
                        task.DownloadedBytes = actualLen;
                        task.TotalBytes = actualLen;
                        task.IsDecoyCanvas = false;
                        task.ProtectionWarning = null;
                        Logger.Info($"Berhasil membongkar decoy steganografi untuk '{task.Filename}' (ukuran: {actualLen} B)!", "ENGINE");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Gagal mengevaluasi decoy steganografi pada unduhan reguler: {ex.Message}", "ENGINE");
            }
        }

        Logger.Info($"Tugas unduhan selesai: {task.Id} ({task.Filename}), total={task.DownloadedBytes} bytes", "ENGINE");
        // Mark Completed
        task.Status = DownloadStatus.Completed;
        task.CompletedAt = DateTime.Now.ToString("o");
        if (task.TotalBytes is > 0)
        {
            task.DownloadedBytes = task.TotalBytes.Value;
        }
        else
        {
            task.TotalBytes = task.DownloadedBytes;
        }
        foreach (var s in task.Segments) s.IsFinished = true;

        _database.UpsertTask(task);
        TaskCompleted?.Invoke(this, task.Id);
    }

    private async Task ExecuteYtDlpDownloadAsync(DownloadTask task, CancellationToken ct)
    {
        using (Logger.WithRayId(task.RayId))
        {
            var taskLimiter = GetOrCreateTaskLimiter(task.Id, task.SpeedLimitBps ?? 0);
            if (task.SpeedLimitBps.HasValue)
            {
                taskLimiter.SetRate(task.SpeedLimitBps.Value);
            }

            void OnProgress(long downloaded, long? total, long speed, long? eta)
            {
                task.DownloadedBytes = downloaded;
                if (total.HasValue && total.Value > 0)
                {
                    task.TotalBytes = total.Value;
                }

                ProgressUpdated?.Invoke(this, new DownloadProgressEventArgs
                {
                    TaskId = task.Id,
                    DownloadedBytes = downloaded,
                    TotalBytes = task.TotalBytes,
                    SpeedBps = speed,
                    EtaSeconds = eta,
                    Segments = task.Segments
                });
            }

            if (task.Filename.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) || task.FilePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                task.Filename = Path.ChangeExtension(task.Filename, ".mp4");
                task.FilePath = Path.ChangeExtension(task.FilePath, ".mp4");
                _database.UpsertTask(task);
            }

            Logger.Info($"Memulai unduhan streaming yt-dlp: '{task.Filename}' (kualitas={task.Quality ?? "best"})", "ENGINE", task.RayId);
            await YtDlpService.DownloadAsync(task, taskLimiter, OnProgress, ct);

            // Synchronize actual file size from disk (1:1 with my-own-idm)
            long actualLen = File.Exists(task.FilePath) ? new FileInfo(task.FilePath).Length : 0;
            if (actualLen <= 0)
            {
                string? dir = Path.GetDirectoryName(task.FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    string stem = Path.GetFileNameWithoutExtension(task.FilePath);
                    string candMp4 = Path.Combine(dir, stem + ".mp4");
                    string candMkv = Path.Combine(dir, stem + ".mkv");
                    string candWebm = Path.Combine(dir, stem + ".webm");

                    if (File.Exists(candMp4))
                    {
                        task.FilePath = candMp4;
                        task.Filename = Path.GetFileName(candMp4);
                        actualLen = new FileInfo(candMp4).Length;
                    }
                    else if (File.Exists(candMkv))
                    {
                        task.FilePath = candMkv;
                        task.Filename = Path.GetFileName(candMkv);
                        actualLen = new FileInfo(candMkv).Length;
                    }
                    else if (File.Exists(candWebm))
                    {
                        task.FilePath = candWebm;
                        task.Filename = Path.GetFileName(candWebm);
                        actualLen = new FileInfo(candWebm).Length;
                    }
                }
            }

            if (actualLen > 0)
            {
                task.DownloadedBytes = actualLen;
                task.TotalBytes = actualLen;
                task.Status = DownloadStatus.Completed;
                task.CompletedAt = DateTime.Now.ToString("o");
                task.ErrorMessage = null;
                foreach (var s in task.Segments) s.IsFinished = true;

                // Validate container format of output file and auto-decrypt steganographic decoys (JPEG or PNG)
                bool isDecoyStegano = false;
                bool isHtmlError = false;
                try
                {
                    using (var fs = File.OpenRead(task.FilePath))
                    {
                        byte[] sample = new byte[Math.Min(1024, (int)fs.Length)];
                        int read = fs.Read(sample, 0, sample.Length);
                        var sampleSpan = sample.AsSpan(0, read);

                        if (SteganographyDecrypter.IsSteganographyDecoy(sampleSpan))
                        {
                            isDecoyStegano = true;
                        }
                        else if (read >= 15 && HoneypotDetector.IsHtmlErrorPayload(sampleSpan))
                        {
                            isHtmlError = true;
                        }
                    }

                    if (isDecoyStegano)
                    {
                        Logger.Info($"Mencoba membongkar decoy steganografi untuk '{task.Filename}'...", "ENGINE", task.RayId);
                        bool decrypted = await SteganographyDecrypter.TryDecryptInPlaceAsync(task.FilePath);
                        if (decrypted)
                        {
                            actualLen = new FileInfo(task.FilePath).Length;
                            task.DownloadedBytes = actualLen;
                            task.TotalBytes = actualLen;
                            task.IsDecoyCanvas = false;
                            task.ProtectionWarning = null;
                            Logger.Info($"Berhasil membongkar decoy steganografi untuk '{task.Filename}' (ukuran: {actualLen} B)!", "ENGINE", task.RayId);
                        }
                        else
                        {
                            task.IsDecoyCanvas = true;
                            task.ProtectionWarning = "Decoy Steganografi Terdeteksi: Video disamarkan sebagai stream gambar (JPEG/PNG) oleh server";
                            Logger.Warn($"Unduhan '{task.Filename}' terdeteksi sebagai decoy steganografi yang belum didukung.", "ENGINE", task.RayId);
                        }
                    }
                    else if (isHtmlError)
                    {
                        task.IsHoneypotDetected = true;
                        task.ProtectionWarning = "File hasil unduhan berisi dokumen HTML (bukan video asli)";
                        Logger.Warn($"Unduhan '{task.Filename}' terdeteksi sebagai HTML error payload.", "ENGINE", task.RayId);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Gagal memvalidasi/mendekripsi container output yt-dlp: {ex.Message}", "ENGINE", task.RayId);
                }

                _database.UpsertTask(task);
                TaskCompleted?.Invoke(this, task.Id);
            }
            else
            {
                throw new InvalidOperationException("Unduhan stream selesai tetapi berkas hasil akhir tidak ditemukan di disk.");
            }
        }
    }

    private async Task ExecuteTelegramDownloadAsync(DownloadTask task, CancellationToken ct)
    {
        using (Logger.WithRayId(task.RayId))
        {
            var raw = task.Url.Replace("tg://chat/", "");
            var parts = raw.Split("/msg/");
            if (parts.Length != 2 || !long.TryParse(parts[0], out long chatId) || !int.TryParse(parts[1], out int messageId))
            {
                throw new FormatException($"Format URL Telegram tidak valid: {task.Url}");
            }

            Logger.Info($"Memulai unduhan media Telegram: Chat ID {chatId}, Message ID {messageId} -> '{task.FilePath}'", "ENGINE", task.RayId);

            var taskLimiter = GetOrCreateTaskLimiter(task.Id, task.SpeedLimitBps ?? 0);
            if (task.SpeedLimitBps.HasValue)
            {
                taskLimiter.SetRate(task.SpeedLimitBps.Value);
            }

            var channel = System.Threading.Channels.Channel.CreateUnbounded<(int SegIndex, int Bytes)>();
            long bytesSinceLastSpeedCalc = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastDbSave = Stopwatch.StartNew();
            long currentSpeedBps = 0;

            var progressReader = Task.Run(async () =>
            {
                while (await channel.Reader.WaitToReadAsync(ct))
                {
                    while (channel.Reader.TryRead(out var item))
                    {
                        bytesSinceLastSpeedCalc += item.Bytes;
                    }

                    if (stopwatch.ElapsedMilliseconds >= 300)
                    {
                        double seconds = stopwatch.Elapsed.TotalSeconds;
                        currentSpeedBps = seconds > 0 ? (long)(bytesSinceLastSpeedCalc / seconds) : 0;
                        bytesSinceLastSpeedCalc = 0;
                        stopwatch.Restart();

                        long? eta = null;
                        if (currentSpeedBps > 0 && task.TotalBytes is > 0)
                        {
                            long remaining = Math.Max(0, task.TotalBytes.Value - task.DownloadedBytes);
                            eta = remaining / currentSpeedBps;
                        }

                        ProgressUpdated?.Invoke(this, new DownloadProgressEventArgs
                        {
                            TaskId = task.Id,
                            DownloadedBytes = task.DownloadedBytes,
                            TotalBytes = task.TotalBytes,
                            SpeedBps = currentSpeedBps,
                            EtaSeconds = eta,
                            Segments = task.Segments.ToList()
                        });
                    }

                    if (lastDbSave.ElapsedMilliseconds >= 1500)
                    {
                        _database.UpsertTask(task);
                        lastDbSave.Restart();
                    }
                }
            }, ct);

            long lastDone = task.DownloadedBytes;
            await _telegramService.DownloadMediaMessageAsync(chatId, messageId, task.FilePath, async (done, total) =>
            {
                if (total > 0) task.TotalBytes = total;
                task.DownloadedBytes = done;

                long delta = done - lastDone;
                if (delta > 0)
                {
                    lastDone = done;
                    if (taskLimiter.BytesPerSecond > 0)
                    {
                        await taskLimiter.ConsumeAsync((int)Math.Min(delta, int.MaxValue), ct);
                    }
                    if (GlobalLimiter.BytesPerSecond > 0)
                    {
                        await GlobalLimiter.ConsumeAsync((int)Math.Min(delta, int.MaxValue), ct);
                    }
                    channel.Writer.TryWrite((0, (int)Math.Min(delta, int.MaxValue)));
                }
            }, ct);

            channel.Writer.Complete();
            await progressReader;

            task.Status = DownloadStatus.Completed;
            task.CompletedAt = DateTime.Now.ToString("o");
            if (task.TotalBytes is > 0)
            {
                task.DownloadedBytes = task.TotalBytes.Value;
            }

            _database.UpsertTask(task);
            TaskCompleted?.Invoke(this, task.Id);
        }
    }

    private async Task DownloadSegmentRangeAsync(
        DownloadTask task,
        DownloadSegment seg,
        SparseFileWriter writer,
        System.Threading.Channels.ChannelWriter<(int, int)> channelWriter,
        ConcurrentDictionary<int, long> dynamicEnds,
        TokenBucketRateLimiter taskLimiter,
        CancellationToken ct)
    {
        long currentOffset = seg.StartByte + seg.DownloadedBytes;
        if (dynamicEnds.TryGetValue(seg.Index, out long endBound) && endBound != long.MaxValue && currentOffset > endBound)
        {
            seg.IsFinished = true;
            return;
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, task.Url);
        if (!string.IsNullOrEmpty(task.Referer))
        {
            req.Headers.Referrer = new Uri(task.Referer);
        }

        if (dynamicEnds.TryGetValue(seg.Index, out long targetEnd) && targetEnd != long.MaxValue)
        {
            req.Headers.Range = new RangeHeaderValue(currentOffset, targetEnd);
        }
        else if (currentOffset > 0)
        {
            req.Headers.Range = new RangeHeaderValue(currentOffset, null);
        }

        using var resp = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);

        try
        {
            long fileOffset = currentOffset;
            while (!ct.IsCancellationRequested)
            {
                long effectiveLimit = 0;
                if (taskLimiter.BytesPerSecond > 0)
                    effectiveLimit = taskLimiter.BytesPerSecond;
                if (GlobalLimiter.BytesPerSecond > 0)
                    effectiveLimit = effectiveLimit > 0 ? Math.Min(effectiveLimit, GlobalLimiter.BytesPerSecond) : GlobalLimiter.BytesPerSecond;

                long maxToRead = buffer.Length;
                if (effectiveLimit > 0)
                {
                    long slice = Math.Max(1024, effectiveLimit / 4);
                    maxToRead = Math.Min(buffer.Length, slice);
                }

                if (dynamicEnds.TryGetValue(seg.Index, out long curEnd) && curEnd != long.MaxValue)
                {
                    long remainingInChunk = curEnd - fileOffset + 1;
                    if (remainingInChunk <= 0) break;
                    maxToRead = Math.Min(maxToRead, remainingInChunk);
                }

                int read = await stream.ReadAsync(buffer.AsMemory(0, (int)maxToRead), ct);
                if (read <= 0) break;

                if (taskLimiter.BytesPerSecond > 0)
                {
                    await taskLimiter.ConsumeAsync(read, ct);
                }
                if (GlobalLimiter.BytesPerSecond > 0)
                {
                    await GlobalLimiter.ConsumeAsync(read, ct);
                }

                writer.WriteAt(fileOffset, buffer, read);
                fileOffset += read;
                seg.DownloadedBytes += read;

                channelWriter.TryWrite((seg.Index, read));

                if (dynamicEnds.TryGetValue(seg.Index, out long finalEnd) && finalEnd != long.MaxValue && fileOffset > finalEnd)
                {
                    break;
                }
            }

            seg.IsFinished = true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string SanitizeFilename(string name)
    {
        var invalids = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalids.Contains(c) ? '_' : c));
    }
}

