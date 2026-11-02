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

    public static async Task<ProbeResult> ProbeUrlAsync(string url, string? referer = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new RangeHeaderValue(0, 0);
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
                var uri = new Uri(url);
                detectedFilename = Path.GetFileName(uri.LocalPath);
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(detectedFilename))
        {
            detectedFilename = "download_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }

        detectedFilename = SanitizeFilename(detectedFilename);

        return new ProbeResult
        {
            Filename = detectedFilename,
            TotalBytes = totalBytes,
            SupportsRange = supportsRange,
            Category = CategoryHelper.DetectCategory(detectedFilename),
            ContentType = resp.Content.Headers.ContentType?.MediaType
        };
    }

    public async Task<DownloadTask> ProbeAndCreateTaskAsync(string url, string saveDir, string? filename = null, int connections = 8, string? referer = null, string? category = null)
    {
        var probe = await ProbeUrlAsync(url, referer);

        string finalFilename = !string.IsNullOrWhiteSpace(filename) ? SanitizeFilename(filename) : probe.Filename;
        string filePath = Path.Combine(saveDir, finalFilename);

        int workerCount = (probe.SupportsRange && probe.TotalBytes is > 0) ? Math.Max(1, Math.Min(connections, 32)) : 1;
        var segments = CalculateSegments(probe.TotalBytes, workerCount);

        var task = new DownloadTask
        {
            Url = url,
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
            Referer = referer
        };

        _database.UpsertTask(task);
        return task;
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
        var cts = new CancellationTokenSource();
        _runningTasks[task.Id] = cts;
        task.Status = DownloadStatus.Downloading;
        _database.UpsertTask(task);

        Task.Run(async () =>
        {
            try
            {
                await ExecuteDownloadAsync(task, cts.Token);
            }
            catch (OperationCanceledException)
            {
                task.Status = DownloadStatus.Paused;
                _database.UpsertTask(task);
                TaskPaused?.Invoke(this, task.Id);
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.ErrorMessage = ex.Message;
                _database.UpsertTask(task);
                TaskFailed?.Invoke(this, (task.Id, ex.Message));
            }
            finally
            {
                _runningTasks.TryRemove(task.Id, out _);
            }
        });
    }

    public void PauseDownload(string taskId)
    {
        if (_runningTasks.TryRemove(taskId, out var cts))
        {
            cts.Cancel();
        }
    }

    private async Task ExecuteDownloadAsync(DownloadTask task, CancellationToken ct)
    {
        if (task.Url.StartsWith("tg://", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteTelegramDownloadAsync(task, ct);
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
                    }
                    else
                    {
                        break;
                    }
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

        // Mark Completed
        task.Status = DownloadStatus.Completed;
        task.CompletedAt = DateTime.Now.ToString("o");
        if (task.TotalBytes is > 0)
        {
            task.DownloadedBytes = task.TotalBytes.Value;
        }
        foreach (var s in task.Segments) s.IsFinished = true;

        _database.UpsertTask(task);
        TaskCompleted?.Invoke(this, task.Id);
    }

    private async Task ExecuteTelegramDownloadAsync(DownloadTask task, CancellationToken ct)
    {
        var raw = task.Url.Replace("tg://chat/", "");
        var parts = raw.Split("/msg/");
        if (parts.Length != 2 || !long.TryParse(parts[0], out long chatId) || !int.TryParse(parts[1], out int messageId))
        {
            throw new FormatException($"Format URL Telegram tidak valid: {task.Url}");
        }

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

