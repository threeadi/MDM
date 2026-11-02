using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Mdm.Models;
using Mdm.Services;

namespace Mdm.ViewModels;

public class DownloadItemViewModel : INotifyPropertyChanged
{
    private double _progress;
    private string _progressText = "0.0%";
    private string _speedFormatted = "--";
    private string _etaFormatted = "--";
    private string _downloadedFormatted = "0 B";
    private long _speedBps;
    private long? _etaSeconds;
    private DownloadStatus _status;
    private List<DownloadSegment> _segments = [];

    public DownloadTask Task { get; }

    public string Id => Task.Id;
    public string Filename => Task.Filename;
    public string FilePath => Task.FilePath;
    public string Url => Task.Url;
    public string Category => Task.Category;
    public string CreatedAt => Task.CreatedAt;
    public long TotalBytes => Task.TotalBytes ?? 0;

    public string LucideCategoryIcon
    {
        get
        {
            if (Status == DownloadStatus.Completed)
            {
                return "task_alt";
            }

            string fn = (Filename ?? Task.Filename ?? string.Empty).ToLowerInvariant();
            if (fn.EndsWith(".iso") || fn.EndsWith(".img") || fn.EndsWith(".vmdk"))
            {
                return "disc_full";
            }
            if (fn.EndsWith(".exe") || fn.EndsWith(".msi") || fn.EndsWith(".apk") || fn.EndsWith(".appx") || fn.EndsWith(".bat") || fn.EndsWith(".cmd"))
            {
                return "widgets";
            }
            if (fn.EndsWith(".zip") || fn.EndsWith(".rar") || fn.EndsWith(".7z") || fn.EndsWith(".tar") || fn.EndsWith(".gz") || fn.EndsWith(".bz2"))
            {
                return "folder_zip";
            }
            if (fn.EndsWith(".mp4") || fn.EndsWith(".mkv") || fn.EndsWith(".avi") || fn.EndsWith(".mov") || fn.EndsWith(".webm") || fn.EndsWith(".flv"))
            {
                return "movie";
            }
            if (fn.EndsWith(".mp3") || fn.EndsWith(".flac") || fn.EndsWith(".wav") || fn.EndsWith(".aac") || fn.EndsWith(".m4a") || fn.EndsWith(".ogg"))
            {
                return "music_note";
            }
            if (fn.EndsWith(".pdf") || fn.EndsWith(".docx") || fn.EndsWith(".doc") || fn.EndsWith(".txt") || fn.EndsWith(".xlsx") || fn.EndsWith(".pptx"))
            {
                return "description";
            }

            return Category?.ToLowerInvariant() switch
            {
                "video" => "movie",
                "musik" or "audio" => "music_note",
                "kompresi" or "compressed" => "folder_zip",
                "program" or "programs" => "widgets",
                "dokumen" or "documents" => "description",
                _ => "description"
            };
        }
    }

    public Brush LucideCategoryColor => Status switch
    {
        DownloadStatus.Completed => new SolidColorBrush(Color.FromRgb(16, 185, 129)), // Emerald #10B981
        DownloadStatus.Paused => new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate #94A3B8
        _ => new SolidColorBrush(Color.FromRgb(59, 130, 246)) // Primary Blue #3B82F6
    };

    public bool IsTelegram => Task.Url?.StartsWith("tg://", StringComparison.OrdinalIgnoreCase) == true 
                              || string.Equals(Task.Source, "telegram", StringComparison.OrdinalIgnoreCase);

    public string TelegramChannelTitle => Task.TelegramChannelTitle ?? Task.Referer ?? "Telegram";

    public string TelegramBadgeText => !string.IsNullOrEmpty(Task.TelegramChannelTitle)
        ? $"Telegram • {Task.TelegramChannelTitle}"
        : "Telegram";

    public string SizeFormatted => Task.TotalBytes.HasValue
        ? FormatBytes(Task.TotalBytes.Value)
        : I18n.Tr("common.unknownSize", "Ukuran Tdk Diketahui");

    public long? SpeedLimitBps
    {
        get => Task.SpeedLimitBps;
        set
        {
            if (Task.SpeedLimitBps != value)
            {
                Task.SpeedLimitBps = value;
                OnPropertyChanged();
            }
        }
    }

    public string DownloadedFormatted
    {
        get => _downloadedFormatted;
        set => SetField(ref _downloadedFormatted, value);
    }

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }

    public string SpeedFormatted
    {
        get => _speedFormatted;
        set => SetField(ref _speedFormatted, value);
    }

    public long SpeedBps
    {
        get => _speedBps;
        set => SetField(ref _speedBps, value);
    }

    public string EtaFormatted
    {
        get => _etaFormatted;
        set => SetField(ref _etaFormatted, value);
    }

    public long? EtaSeconds
    {
        get => _etaSeconds;
        set => SetField(ref _etaSeconds, value);
    }

    public DownloadStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusBgBrush));
                OnPropertyChanged(nameof(TopAccentBrush));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsInFlight));
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }
    }

    public bool IsCompleted => Status == DownloadStatus.Completed;
    public bool IsFailed => Status == DownloadStatus.Failed;
    public bool IsDownloading => Status == DownloadStatus.Downloading;
    public bool IsPaused => Status == DownloadStatus.Paused;
    public bool IsInFlight => Status != DownloadStatus.Completed && Status != DownloadStatus.Failed;
    public string? ErrorMessage => Task.ErrorMessage;

    public Brush TopAccentBrush => Status switch
    {
        DownloadStatus.Completed => new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // Emerald
        DownloadStatus.Failed => new SolidColorBrush(Color.FromRgb(255, 82, 82)),      // Rose
        _ => new SolidColorBrush(Color.FromRgb(0, 229, 255))                           // Cyan
    };

    public string StatusText => Status switch
    {
        DownloadStatus.Downloading => I18n.Tr("status.downloading", "Sedang Mengunduh"),
        DownloadStatus.Completed => I18n.Tr("status.completed", "Selesai"),
        DownloadStatus.Paused => I18n.Tr("status.paused", "Dijeda"),
        DownloadStatus.Failed => I18n.Tr("status.failed", "Gagal"),
        _ => I18n.Tr("status.waiting", "Menunggu")
    };

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SizeFormatted));
    }

    public Brush StatusBrush => Status switch
    {
        DownloadStatus.Downloading => new SolidColorBrush(Color.FromRgb(0, 229, 255)), // Cyan
        DownloadStatus.Completed => new SolidColorBrush(Color.FromRgb(16, 185, 129)),  // Emerald
        DownloadStatus.Paused => new SolidColorBrush(Color.FromRgb(245, 158, 11)),     // Amber
        DownloadStatus.Failed => new SolidColorBrush(Color.FromRgb(255, 82, 82)),      // Rose
        _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
    };

    public Brush StatusBgBrush => Status switch
    {
        DownloadStatus.Downloading => new SolidColorBrush(Color.FromArgb(40, 0, 229, 255)),
        DownloadStatus.Completed => new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
        DownloadStatus.Paused => new SolidColorBrush(Color.FromArgb(40, 245, 158, 11)),
        DownloadStatus.Failed => new SolidColorBrush(Color.FromArgb(40, 255, 82, 82)),
        _ => new SolidColorBrush(Color.FromArgb(40, 148, 163, 184))
    };

    public List<DownloadSegment> Segments
    {
        get => _segments;
        set => SetField(ref _segments, value);
    }

    public DownloadItemViewModel(DownloadTask task)
    {
        Task = task;
        _status = task.Status;
        _segments = task.Segments.ToList();
        _downloadedFormatted = FormatBytes(task.DownloadedBytes);

        if (task.Status == DownloadStatus.Completed)
        {
            _progress = 1.0;
            _progressText = "100.0%";
        }
        else if (task.TotalBytes is > 0)
        {
            _progress = Math.Clamp((double)task.DownloadedBytes / task.TotalBytes.Value, 0.0, 1.0);
            _progressText = (_progress * 100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
    }

    public void UpdateMetrics(long downloaded, long? total, long speedBps, long? etaSeconds, List<DownloadSegment> segments)
    {
        DownloadedFormatted = FormatBytes(downloaded);
        Segments = segments;
        SpeedBps = speedBps;
        EtaSeconds = etaSeconds;

        if (total is > 0)
        {
            Progress = Math.Clamp((double)downloaded / total.Value, 0.0, 1.0);
            ProgressText = (Progress * 100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        SpeedFormatted = $"{FormatBytes(speedBps)}/s";
        EtaFormatted = FormatEta(etaSeconds);
        Status = DownloadStatus.Downloading;
    }

    public void MarkCompleted()
    {
        Progress = 1.0;
        ProgressText = "100.0%";
        SpeedFormatted = "--";
        SpeedBps = 0;
        EtaFormatted = "--";
        EtaSeconds = null;
        Status = DownloadStatus.Completed;
    }

    public void MarkPaused()
    {
        SpeedFormatted = "--";
        SpeedBps = 0;
        EtaFormatted = "--";
        EtaSeconds = null;
        Status = DownloadStatus.Paused;
    }

    public void MarkFailed(string err)
    {
        SpeedFormatted = "--";
        SpeedBps = 0;
        EtaFormatted = "--";
        EtaSeconds = null;
        Status = DownloadStatus.Failed;
    }

    public static string FormatBytes(long bytes)
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

    public static string FormatEta(long? seconds)
    {
        if (seconds is null or <= 0 or > 86400 * 7) return "--:--";
        long s = seconds.Value;
        long h = s / 3600;
        long m = (s % 3600) / 60;
        long sec = s % 60;
        return h > 0 ? $"{h:D2}:{m:D2}:{sec:D2}" : $"{m:D2}:{sec:D2}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
