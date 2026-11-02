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

    public string Initials
    {
        get
        {
            string name = !string.IsNullOrWhiteSpace(DisplayName)
                ? DisplayName.Trim()
                : (!string.IsNullOrWhiteSpace(Username) ? Username.Trim().TrimStart('@') : "TG");

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0)
            {
                return $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
            return name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
        }
    }
}

public class TelegramChannelInfo : INotifyPropertyChanged
{
    private bool _isSelected;

    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string ChatType { get; set; } = "channel"; // "channel", "group", "user", "saved_messages"
    public int UnreadCount { get; set; }

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

    public string ChatIconKind => ChatType switch
    {
        "channel" => "radio",
        "group" or "supergroup" => "users",
        "user" => "user",
        "saved_messages" => "bookmark",
        "bot" => "bot",
        _ => "send"
    };

    public string ChatTypeBadge => ChatType switch
    {
        "channel" => "Kanal",
        "group" or "supergroup" => "Grup",
        "saved_messages" => "Pribadi",
        "user" => "Kontak",
        _ => "Chat"
    };

    public string FormattedUsername => !string.IsNullOrWhiteSpace(Username)
        ? $"@{Username.TrimStart('@')}"
        : (ChatType == "saved_messages" ? "Cloud Storage" : "");

    public string MediaCountText
    {
        get
        {
            if (ChatType == "saved_messages") return "87 Berkas";
            if (ChatType == "channel") return "1.2K Media";
            if (ChatType == "group" || ChatType == "supergroup") return "412 Berkas";
            return "198 Berkas";
        }
    }

    public bool HasUnread => UnreadCount > 0;

    public string FormattedTitle => !string.IsNullOrWhiteSpace(Title)
        ? Title
        : (!string.IsNullOrWhiteSpace(Username) ? $"@{Username.TrimStart('@')}" : Id.ToString());

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
        "video" => new SolidColorBrush(Color.FromRgb(59, 130, 246)),   // Blue #3B82F6
        "audio" => new SolidColorBrush(Color.FromRgb(168, 85, 247)),   // Purple #A855F7
        "photo" => new SolidColorBrush(Color.FromRgb(16, 185, 129)),   // Emerald #10B981
        "gif" => new SolidColorBrush(Color.FromRgb(236, 72, 153)),     // Pink #EC4899
        _ => new SolidColorBrush(Color.FromRgb(245, 158, 11))          // Amber #F59E0B (Document)
    };

    public Brush LucideMediaTypeBg => MediaType.ToLowerInvariant() switch
    {
        "video" => new SolidColorBrush(Color.FromArgb(38, 37, 99, 235)),   // 15% Blue #2563EB
        "audio" => new SolidColorBrush(Color.FromArgb(38, 147, 51, 234)),  // 15% Purple #9333EA
        "photo" => new SolidColorBrush(Color.FromArgb(38, 16, 185, 129)),  // 15% Emerald #10B981
        "gif" => new SolidColorBrush(Color.FromArgb(38, 219, 39, 119)),    // 15% Pink #DB2777
        _ => new SolidColorBrush(Color.FromArgb(38, 217, 119, 6))          // 15% Amber #D97706
    };

    public Brush LucideMediaTypeBorder => MediaType.ToLowerInvariant() switch
    {
        "video" => new SolidColorBrush(Color.FromArgb(77, 59, 130, 246)),  // 30% Blue #3B82F6
        "audio" => new SolidColorBrush(Color.FromArgb(77, 168, 85, 247)),  // 30% Purple #A855F7
        "photo" => new SolidColorBrush(Color.FromArgb(77, 16, 185, 129)),  // 30% Emerald #10B981
        "gif" => new SolidColorBrush(Color.FromArgb(77, 244, 114, 182)),  // 30% Pink #F472B6
        _ => new SolidColorBrush(Color.FromArgb(77, 245, 158, 11))         // 30% Amber #F59E0B
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
