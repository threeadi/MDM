using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Mdm.Models;

namespace Mdm.Services;

public static class YtDlpService
{
    private static readonly Regex VideoUrlRegex = new(
        @"(https?://)?(www\.)?(youtube\.com/watch\?|youtu\.be/|youtube\.com/shorts/|youtube\.com/embed/|tiktok\.com/@|instagram\.com/(reel|p)/|twitter\.com/\w+/status/|x\.com/\w+/status/|vimeo\.com/|facebook\.com/watch)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static bool IsVideoPlatformUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("instagram.com/reel/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("instagram.com/p/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("x.com/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("twitter.com/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("vimeo.com/", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("mediadelivery.net", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("b-cdn.net", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return VideoUrlRegex.IsMatch(url);
    }

    public static string BuildFormatSelector(string? quality, string filename)
    {
        string qStr = !string.IsNullOrWhiteSpace(quality)
            ? quality.Trim().ToLowerInvariant()
            : string.Empty;

        if (string.IsNullOrEmpty(qStr))
        {
            string fnLower = filename.ToLowerInvariant();
            if (fnLower.Contains("2160p") || fnLower.Contains("4k")) qStr = "2160p";
            else if (fnLower.Contains("1440p") || fnLower.Contains("2k")) qStr = "1440p";
            else if (fnLower.Contains("1080p")) qStr = "1080p";
            else if (fnLower.Contains("720p")) qStr = "720p";
            else if (fnLower.Contains("480p")) qStr = "480p";
            else if (fnLower.Contains("360p")) qStr = "360p";
            else if (fnLower.Contains("audio")) qStr = "audio";
        }

        if (qStr.Contains("2160") || qStr.Contains("4k"))
            return "bv*[height<=2160]+ba/b[height<=2160]/best";
        if (qStr.Contains("1440") || qStr.Contains("2k"))
            return "bv*[height<=1440]+ba/b[height<=1440]/best";
        if (qStr.Contains("1080"))
            return "bv*[height<=1080]+ba/b[height<=1080]/best";
        if (qStr.Contains("720"))
            return "bv*[height<=720]+ba/b[height<=720]/best";
        if (qStr.Contains("480"))
            return "bv*[height<=480]+ba/b[height<=480]/best";
        if (qStr.Contains("360"))
            return "bv*[height<=360]+ba/b[height<=360]/best";
        if (qStr.Contains("audio"))
            return "ba/b";

        return "bv*+ba/b/best";
    }

    public static string? FindYtDlp()
    {
        // 1. App base directory
        string localExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
        if (File.Exists(localExe)) return localExe;

        string localBinExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "yt-dlp.exe");
        if (File.Exists(localBinExe)) return localBinExe;

        // 2. Project bin folder
        string devBin = @"D:\Development\MDM\bin\yt-dlp.exe";
        if (File.Exists(devBin)) return devBin;

        // 3. WinGet Links
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wingetExe = Path.Combine(localAppData, @"Microsoft\WinGet\Links\yt-dlp.exe");
        if (File.Exists(wingetExe)) return wingetExe;

        // 4. User local packages
        string wingetPackages = Path.Combine(localAppData, @"Microsoft\WinGet\Packages");
        if (Directory.Exists(wingetPackages))
        {
            try
            {
                var match = Directory.EnumerateFiles(wingetPackages, "yt-dlp.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (match != null) return match;
            }
            catch { }
        }

        // 5. Fallback to system PATH
        return "yt-dlp.exe";
    }

    public static string? FindFfmpegDir()
    {
        // 1. App base directory
        string localFfmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(localFfmpeg)) return AppDomain.CurrentDomain.BaseDirectory;

        string localBinFfmpeg = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "ffmpeg.exe");
        if (File.Exists(localBinFfmpeg)) return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin");

        // 2. WinGet Links
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wingetFfmpeg = Path.Combine(localAppData, @"Microsoft\WinGet\Links\ffmpeg.exe");
        if (File.Exists(wingetFfmpeg))
        {
            return Path.GetDirectoryName(wingetFfmpeg);
        }

        // 3. WinGet Packages
        string wingetPackages = Path.Combine(localAppData, @"Microsoft\WinGet\Packages");
        if (Directory.Exists(wingetPackages))
        {
            try
            {
                var match = Directory.EnumerateFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (match != null) return Path.GetDirectoryName(match);
            }
            catch { }
        }

        return null;
    }

    public static async Task<ProbeResult> ProbeAsync(string url, string? quality = null, CancellationToken ct = default)
    {
        string ytDlp = FindYtDlp() ?? "yt-dlp.exe";
        string? ffmpegDir = FindFfmpegDir();

        var psi = new ProcessStartInfo
        {
            FileName = ytDlp,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--no-playlist");

        if (!string.IsNullOrWhiteSpace(quality))
        {
            string format = BuildFormatSelector(quality, "");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(format);
        }

        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("%(title)s\t%(filesize,filesize_approx)s");
        if (!string.IsNullOrEmpty(ffmpegDir))
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(ffmpegDir);
        }
        psi.ArgumentList.Add(url);

        Logger.Debug($"yt-dlp probing URL: {url}", "YTDLP");
        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errDrainTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        string output = await outputTask;
        await errDrainTask;

        string trimmed = output.Trim();
        string[] parts = trimmed.Split('\t');
        string rawTitle = parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]) ? parts[0] : "Video Stream";
        long? totalBytes = null;
        if (parts.Length > 1 && long.TryParse(parts[1], out long size) && size > 0)
        {
            totalBytes = size;
        }

        string safeFilename = DownloadEngine.SanitizeFilename(rawTitle);
        if (safeFilename.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            safeFilename = Path.ChangeExtension(safeFilename, ".mp4");
        }
        else if (!safeFilename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            safeFilename += ".mp4";
        }

        return new ProbeResult
        {
            Filename = safeFilename,
            TotalBytes = totalBytes,
            SupportsRange = true,
            Category = "Video",
            ContentType = "video/mp4"
        };
    }

    public static async Task DownloadAsync(
        DownloadTask task,
        TokenBucketRateLimiter? limiter,
        Action<long, long?, long, long?> onProgress,
        CancellationToken ct)
    {
        string ytDlp = FindYtDlp() ?? "yt-dlp.exe";
        string? ffmpegDir = FindFfmpegDir();

        string outputFile = task.FilePath;
        if (outputFile.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            outputFile = Path.ChangeExtension(outputFile, ".mp4");
            task.FilePath = outputFile;
            task.Filename = Path.GetFileName(outputFile);
        }

        string? targetDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        string format = BuildFormatSelector(task.Quality, task.Filename);

        var psi = new ProcessStartInfo
        {
            FileName = ytDlp,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--newline");
        psi.ArgumentList.Add("-c"); // Resume partially downloaded files
        psi.ArgumentList.Add("--downloader-args");
        psi.ArgumentList.Add("ffmpeg_i:-extension_picky false");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(format);
        psi.ArgumentList.Add("--merge-output-format");
        psi.ArgumentList.Add("mp4");
        psi.ArgumentList.Add("--progress-delta");
        psi.ArgumentList.Add("0.2");
        psi.ArgumentList.Add("--progress-template");
        psi.ArgumentList.Add("IDM_PROGRESS:%(info.format_id)s:%(progress.downloaded_bytes)s:%(progress.total_bytes,progress.total_bytes_estimate)s");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputFile);

        if (!string.IsNullOrEmpty(ffmpegDir))
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(ffmpegDir);
        }

        if (limiter != null && limiter.BytesPerSecond > 0)
        {
            psi.ArgumentList.Add("--limit-rate");
            psi.ArgumentList.Add(limiter.BytesPerSecond.ToString());
        }

        if (!string.IsNullOrEmpty(task.Referer))
        {
            psi.ArgumentList.Add("--add-header");
            psi.ArgumentList.Add($"Referer: {task.Referer}");
        }

        psi.ArgumentList.Add(task.Url);

        Logger.Info($"Memulai unduhan yt-dlp: {task.Filename} -> {outputFile}", "YTDLP");
        Logger.Debug($"yt-dlp parameter: {string.Join(" ", psi.ArgumentList)}", "YTDLP");
        using var process = new Process { StartInfo = psi };
        process.Start();

        using var localCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Continuous drain of standard error to prevent Windows pipe buffer deadlock
        var stderrLines = new List<string>();
        var stderrLock = new object();
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(localCts.Token) is { } errLine)
                {
                    if (!string.IsNullOrWhiteSpace(errLine))
                    {
                        lock (stderrLock)
                        {
                            if (stderrLines.Count >= 10) stderrLines.RemoveAt(0);
                            stderrLines.Add(errLine.Trim());
                        }
                    }
                }
            }
            catch { }
        }, localCts.Token);

        var formatDownloaded = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
        var formatTotals = new System.Collections.Concurrent.ConcurrentDictionary<string, long>();

        long lastBytes = task.DownloadedBytes;
        var lastReportInstant = Stopwatch.StartNew();
        long speedBps = 0;
        long bytesSinceSpeedCalc = 0;

        var readerTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(localCts.Token) is { } line)
            {
                if (localCts.IsCancellationRequested) break;

                if (line.StartsWith("IDM_PROGRESS:"))
                {
                    string content = line["IDM_PROGRESS:".Length..].Trim();
                    string[] parts = content.Split(':');

                    if (parts.Length >= 3)
                    {
                        string fId = parts[0];
                        if (long.TryParse(parts[1], out long fDown) && fDown >= 0)
                        {
                            formatDownloaded[fId] = fDown;
                        }
                        if (long.TryParse(parts[2], out long fTot) && fTot > 0)
                        {
                            formatTotals[fId] = fTot;
                        }

                        long currentTotal = formatTotals.Values.Sum();
                        long currentDownloaded = formatDownloaded.Values.Sum();

                        if (currentTotal > 0 && (task.TotalBytes == null || task.TotalBytes <= 0 || task.TotalBytes != currentTotal))
                        {
                            task.TotalBytes = currentTotal;
                        }

                        if (currentDownloaded > lastBytes)
                        {
                            long delta = currentDownloaded - lastBytes;
                            bytesSinceSpeedCalc += delta;
                            lastBytes = currentDownloaded;
                        }

                        if (lastReportInstant.ElapsedMilliseconds >= 300)
                        {
                            double elapsed = lastReportInstant.Elapsed.TotalSeconds;
                            speedBps = elapsed > 0 ? (long)(bytesSinceSpeedCalc / elapsed) : 0;
                            bytesSinceSpeedCalc = 0;
                            lastReportInstant.Restart();

                            long? eta = null;
                            if (task.TotalBytes is > 0 && speedBps > 0)
                            {
                                long remaining = Math.Max(0, task.TotalBytes.Value - lastBytes);
                                eta = remaining / speedBps;
                            }

                            onProgress(lastBytes, task.TotalBytes, speedBps, eta);
                        }
                    }
                    else if (parts.Length >= 2)
                    {
                        long downloaded = 0;
                        if (long.TryParse(parts[0], out long d))
                        {
                            downloaded = d;
                        }
                        if (long.TryParse(parts[1], out long estTotal) && estTotal > 0)
                        {
                            if (task.TotalBytes == null || task.TotalBytes <= 0 || task.TotalBytes != estTotal)
                            {
                                task.TotalBytes = estTotal;
                            }
                        }
                        if (downloaded > 0)
                        {
                            if (downloaded > lastBytes)
                            {
                                bytesSinceSpeedCalc += (downloaded - lastBytes);
                                lastBytes = downloaded;
                            }

                            if (lastReportInstant.ElapsedMilliseconds >= 300)
                            {
                                double elapsed = lastReportInstant.Elapsed.TotalSeconds;
                                speedBps = elapsed > 0 ? (long)(bytesSinceSpeedCalc / elapsed) : 0;
                                bytesSinceSpeedCalc = 0;
                                lastReportInstant.Restart();

                                long? eta = null;
                                if (task.TotalBytes is > 0 && speedBps > 0)
                                {
                                    long remaining = Math.Max(0, task.TotalBytes.Value - downloaded);
                                    eta = remaining / speedBps;
                                }

                                onProgress(downloaded, task.TotalBytes, speedBps, eta);
                            }
                        }
                    }
                }
            }
        }, localCts.Token);

        // Background physical disk polling loop
        string partPath = outputFile + ".part";
        var diskPollTask = Task.Run(async () =>
        {
            while (!localCts.IsCancellationRequested)
            {
                try
                {
                    if (process.HasExited) break;
                }
                catch { break; }

                try
                {
                    await Task.Delay(250, localCts.Token);
                }
                catch { break; }

                try
                {
                    long diskBytes = 0;
                    if (File.Exists(outputFile))
                    {
                        diskBytes = new FileInfo(outputFile).Length;
                    }
                    else if (File.Exists(partPath))
                    {
                        diskBytes = new FileInfo(partPath).Length;
                    }
                    else if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
                    {
                        string stem = Path.GetFileNameWithoutExtension(outputFile);
                        var tempFiles = Directory.EnumerateFiles(targetDir, $"{stem}*")
                            .Where(f => f.EndsWith(".part") || f.EndsWith(".temp") || f.Contains(".f"))
                            .ToList();
                        if (tempFiles.Count > 0)
                        {
                            diskBytes = tempFiles.Sum(f => new FileInfo(f).Length);
                        }
                    }

                    if (diskBytes > lastBytes)
                    {
                        long delta = diskBytes - lastBytes;
                        bytesSinceSpeedCalc += delta;
                        lastBytes = diskBytes;

                        if (lastReportInstant.ElapsedMilliseconds >= 300)
                        {
                            double elapsed = lastReportInstant.Elapsed.TotalSeconds;
                            speedBps = elapsed > 0 ? (long)(bytesSinceSpeedCalc / elapsed) : 0;
                            bytesSinceSpeedCalc = 0;
                            lastReportInstant.Restart();

                            long? eta = null;
                            if (task.TotalBytes is > 0 && speedBps > 0)
                            {
                                long remaining = Math.Max(0, task.TotalBytes.Value - diskBytes);
                                eta = remaining / speedBps;
                            }

                            onProgress(diskBytes, task.TotalBytes, speedBps, eta);
                        }
                    }
                }
                catch { }
            }
        }, localCts.Token);

        try
        {
            await process.WaitForExitAsync(ct);
            await readerTask;
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch { }
            throw;
        }
        finally
        {
            localCts.Cancel();
            try { await diskPollTask; } catch { }
            try { await readerTask; } catch { }
            try { await stderrTask; } catch { }
        }

        if (process.ExitCode != 0)
        {
            string errSummary;
            lock (stderrLock)
            {
                errSummary = string.Join(" | ", stderrLines);
            }
            throw new InvalidOperationException($"yt-dlp failed (exit code {process.ExitCode}): {errSummary}");
        }

        // Output file resolution: check if merged to .mp4, .mkv, or .webm
        if (!File.Exists(outputFile))
        {
            string stem = Path.GetFileNameWithoutExtension(outputFile);
            string candidateMp4 = Path.Combine(targetDir ?? "", stem + ".mp4");
            string candidateMkv = Path.Combine(targetDir ?? "", stem + ".mkv");
            string candidateWebm = Path.Combine(targetDir ?? "", stem + ".webm");

            if (File.Exists(candidateMp4))
            {
                task.FilePath = candidateMp4;
                task.Filename = Path.GetFileName(candidateMp4);
            }
            else if (File.Exists(candidateMkv))
            {
                task.FilePath = candidateMkv;
                task.Filename = Path.GetFileName(candidateMkv);
            }
            else if (File.Exists(candidateWebm))
            {
                task.FilePath = candidateWebm;
                task.Filename = Path.GetFileName(candidateWebm);
            }
        }
    }
}
