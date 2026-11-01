using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Mdm.ViewModels;

namespace Mdm.Models;

public class TelegramAuthStatus
{
    public bool IsAuthenticated { get; set; }
    public bool IsNotAuthenticated => !IsAuthenticated;
    public string? PhoneNumber { get; set; }
    public long? UserId { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public bool NeedsPassword { get; set; }
    public bool NeedsOtp { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TelegramChannelInfo
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string ChatType { get; set; } = "channel"; // "channel", "group", "user", "saved_messages"
    public int UnreadCount { get; set; }

    public string ChatIconKind => ChatType switch
    {
        "channel" => "globe",
        "user" => "user",
        _ => "message-square"
    };

    public string FormattedTitle => !string.IsNullOrWhiteSpace(Title)
        ? Title
        : (!string.IsNullOrWhiteSpace(Username) ? $"@{Username.TrimStart('@')}" : Id.ToString());
}

public class TelegramMediaItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status = "Siap Unduh";
    private double _progress;

    public int MessageId { get; set; }
    public long ChatId { get; set; }
    public string ChatTitle { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public string? SubchatName { get; set; }
    public string? Resolution { get; set; }
    public string? DurationText { get; set; }
    public string? Crc32Hash { get; set; }
    public string Filename { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public string MediaType { get; set; } = "document"; // "video", "document", "audio", "photo", "gif"
    public DateTime CreatedAt { get; set; }
    public string TgUrl { get; set; } = string.Empty;

    public string Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDownloading));
                OnPropertyChanged(nameof(IsCompleted));
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(IsFailed));
                OnPropertyChanged(nameof(IsReady));
            }
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (Math.Abs(_progress - value) > 0.001)
            {
                _progress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProgressPercentText));
            }
        }
    }

    public string ProgressPercentText => $"{(_progress * 100):0.0}%";

    public bool IsDownloading => Status.Contains("Mengunduh", StringComparison.OrdinalIgnoreCase) || Status.Contains("Downloading", StringComparison.OrdinalIgnoreCase);
    public bool IsCompleted => Status.Contains("Selesai", StringComparison.OrdinalIgnoreCase) || Status.Contains("Terunduh", StringComparison.OrdinalIgnoreCase) || Status.Contains("Completed", StringComparison.OrdinalIgnoreCase);
    public bool IsPaused => Status.Contains("Jeda", StringComparison.OrdinalIgnoreCase) || Status.Contains("Paused", StringComparison.OrdinalIgnoreCase);
    public bool IsFailed => Status.Contains("Gagal", StringComparison.OrdinalIgnoreCase) || Status.Contains("Failed", StringComparison.OrdinalIgnoreCase);
    public bool IsReady => !IsDownloading && !IsCompleted && !IsPaused && !IsFailed;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string SizeFormatted => DownloadItemViewModel.FormatBytes(FileSize);
    public string DateFormatted => CreatedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public string MediaIconText => MediaType.ToLowerInvariant() switch
    {
        "video" => "Video",
        "audio" => "Musik",
        "photo" => "Foto",
        "gif" => "GIF",
        _ => "Dokumen"
    };

    public string LucideMediaTypeIcon => MediaType.ToLowerInvariant() switch
    {
        "video" => "video",
        "audio" => "music",
        "photo" => "archive",
        "gif" => "play",
        _ => "file-text"
    };

    public Brush LucideMediaTypeColor => MediaType.ToLowerInvariant() switch
    {
        "video" => new SolidColorBrush(Color.FromRgb(78, 222, 163)),
        "audio" => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        "photo" => new SolidColorBrush(Color.FromRgb(192, 132, 252)),
        "gif" => new SolidColorBrush(Color.FromRgb(244, 114, 182)),
        _ => new SolidColorBrush(Color.FromRgb(173, 198, 255))
    };

    public Brush LucideMediaTypeBg => MediaType.ToLowerInvariant() switch
    {
        "video" => new SolidColorBrush(Color.FromArgb(35, 78, 222, 163)),
        "audio" => new SolidColorBrush(Color.FromArgb(35, 245, 158, 11)),
        "photo" => new SolidColorBrush(Color.FromArgb(35, 192, 132, 252)),
        "gif" => new SolidColorBrush(Color.FromArgb(35, 244, 114, 182)),
        _ => new SolidColorBrush(Color.FromArgb(35, 173, 198, 255))
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
