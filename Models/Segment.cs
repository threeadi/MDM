namespace Mdm.Models;

public class DownloadSegment
{
    public int Index { get; set; }
    public long StartByte { get; set; }
    public long EndByte { get; set; }
    public long DownloadedBytes { get; set; }
    public bool IsFinished { get; set; }
    public bool IsActive => !IsFinished && DownloadedBytes > 0;

    public double Progress
    {
        get
        {
            if (IsFinished) return 1.0;
            long total = EndByte >= StartByte && EndByte != long.MaxValue ? EndByte - StartByte + 1 : 1;
            return total > 0 ? Math.Clamp((double)DownloadedBytes / total, 0.0, 1.0) : 0.0;
        }
    }

    public string ProgressText => $"{(Progress * 100):F0}%";
    public string DownloadedFormatted => FormatBytes(DownloadedBytes);
    public string StatusText => IsFinished ? "Selesai" : (IsActive ? $"Aktif ({ProgressText})" : "Menunggu");

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = 1024 * KB;
        const long GB = 1024 * MB;

        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (bytes >= GB) return ((double)bytes / GB).ToString("F2", culture) + " GB";
        if (bytes >= MB) return ((double)bytes / MB).ToString("F1", culture) + " MB";
        if (bytes >= KB) return ((double)bytes / KB).ToString("F0", culture) + " KB";
        return $"{bytes} B";
    }
}

