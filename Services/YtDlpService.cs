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

    public static async Task<ProbeResult> ProbeAsync(string url, CancellationToken ct = default)
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

        psi.ArgumentList.Add("--no-warnings");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("%(title)s\t%(filesize,filesize_approx)s");
        if (!string.IsNullOrEmpty(ffmpegDir))
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(ffmpegDir);
        }
        psi.ArgumentList.Add(url);

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
        if (!safeFilename.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
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
        psi.ArgumentList.Add("IDM_PROGRESS:%(progress.downloaded_bytes)s");
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

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Continuous drain of standard error to prevent Windows pipe buffer deadlock
        var stderrLines = new List<string>();
        var stderrLock = new object();
        var stderrTask = Task.Run(async () =>
        {
            try
            {
                while (await process.StandardError.ReadLineAsync(CancellationToken.None) is { } errLine)
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
        });

        long lastBytes = task.DownloadedBytes;
        var lastReportInstant = Stopwatch.StartNew();
        long speedBps = 0;
        long bytesSinceSpeedCalc = 0;

        var readerTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
            {
                if (ct.IsCancellationRequested) break;

                if (line.StartsWith("IDM_PROGRESS:"))
                {
                    string content = line["IDM_PROGRESS:".Length..].Trim();
                    if (long.TryParse(content, out long downloaded) && downloaded > 0)
                    {
                        if (downloaded > lastBytes)
                        {
                            long delta = downloaded - lastBytes;
                            bytesSinceSpeedCalc += delta;
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
        }, ct);

        // Background physical disk polling loop (1:1 with my-own-idm lines 470-484)
        string partPath = outputFile + ".part";
        var diskPollTask = Task.Run(async () =>
        {
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct);

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
                        // Check if any temp file matches
                        string stem = Path.GetFileNameWithoutExtension(outputFile);
                        var tempFiles = Directory.EnumerateFiles(targetDir, $"{stem}*.part").ToList();
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
        }, ct);

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

        if (process.ExitCode != 0)
        {
            string errSummary;
            lock (stderrLock)
            {
                errSummary = string.Join(" | ", stderrLines);
            }
            throw new InvalidOperationException($"yt-dlp failed (exit code {process.ExitCode}): {errSummary}");
        }

        // Output file resolution: check if merged to .mp4 or .mkv
        if (!File.Exists(outputFile))
        {
            string stem = Path.GetFileNameWithoutExtension(outputFile);
            string candidateMp4 = Path.Combine(targetDir ?? "", stem + ".mp4");
            string candidateMkv = Path.Combine(targetDir ?? "", stem + ".mkv");

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
        }
    }
}
