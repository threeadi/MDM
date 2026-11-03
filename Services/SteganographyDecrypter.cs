using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Mdm.Services;

public static class SteganographyDecrypter
{
    public static bool IsSteganographyDecoy(ReadOnlySpan<byte> head)
    {
        // JPEG SOI
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
        {
            return true;
        }

        // PNG Magic signature (\x89PNG\r\n\x1a\n)
        if (head.Length >= 8 &&
            head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 &&
            head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A)
        {
            return true;
        }

        return false;
    }

    public static async Task<bool> TryDecryptInPlaceAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            // Verify if file begins with JPEG SOI or PNG magic
            using (var fs = File.OpenRead(filePath))
            {
                if (fs.Length < 1024) return false;
                byte[] head = new byte[8];
                int r = fs.Read(head, 0, 8);
                if (r < 3 || !IsSteganographyDecoy(head.AsSpan(0, r)))
                {
                    return false;
                }
            }

            // Find stegano_decrypt.js script
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, "Scripts", "stegano_decrypt.js");
            if (!File.Exists(scriptPath))
            {
                // Fallback to project root directory in development
                string devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Scripts", "stegano_decrypt.js"));
                if (File.Exists(devPath))
                {
                    scriptPath = devPath;
                }
                else
                {
                    Logger.Warn("Skrip 'stegano_decrypt.js' tidak ditemukan.", "DECRYPTER");
                    return false;
                }
            }

            string nodeExe = "node";
            var psi = new ProcessStartInfo
            {
                FileName = nodeExe,
                Arguments = $"\"{scriptPath}\" \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string stdout = await outTask;
            string stderr = await errTask;

            if (process.ExitCode == 0)
            {
                // Verify new container header
                using var fs = File.OpenRead(filePath);
                byte[] sample = new byte[Math.Min(1024, (int)fs.Length)];
                int read = fs.Read(sample, 0, sample.Length);
                if (read >= 8 && HoneypotDetector.HasValidMediaContainerHeader(sample.AsSpan(0, read)))
                {
                    Logger.Info($"Dekripsi in-place berhasil untuk '{Path.GetFileName(filePath)}'", "DECRYPTER");
                    return true;
                }
            }

            Logger.Warn($"Gagal mendekripsi decoy: exit={process.ExitCode}, err={stderr}", "DECRYPTER");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Exception saat mencoba dekripsi decoy: {ex.Message}", "DECRYPTER");
            return false;
        }
    }
}
