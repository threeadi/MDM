namespace Mdm.Models;

public class DownloadTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Url { get; set; } = string.Empty;
    public string Filename { get; set; } = string.Empty;
    public string SaveDir { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long? TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Downloading;
    public int Connections { get; set; } = 8;
    public bool SupportsRange { get; set; } = true;
    
    private string _category = "Umum";
    public string Category
    {
        get
        {
            if ((string.IsNullOrWhiteSpace(_category) || _category.Equals("Umum", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(Filename))
            {
                return Services.CategoryHelper.DetectCategory(Filename);
            }
            return _category;
        }
        set => _category = value;
    }

    public string CreatedAt { get; set; } = DateTime.Now.ToString("o");
    public string? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public List<DownloadSegment> Segments { get; set; } = [];
    public string? Referer { get; set; }
    public long? SpeedLimitBps { get; set; }
    public string? Source { get; set; }
    public string? TelegramChannelTitle { get; set; }

    public double Progress
    {
        get
        {
            if (Status == DownloadStatus.Completed) return 1.0;
            if (TotalBytes is > 0)
                return Math.Clamp((double)DownloadedBytes / TotalBytes.Value, 0.0, 1.0);
            return 0.0;
        }
    }
}

