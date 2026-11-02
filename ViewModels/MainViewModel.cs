using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mdm.Models;
using Mdm.Services;

namespace Mdm.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly Database _database;
    private readonly DownloadEngine _engine;
    private readonly IpcServer? _ipcServer;

    private string _activeView = "downloads"; // "downloads" | "telegram"
    private string _activeCategory = "all";
    private string _searchQuery = string.Empty;
    private DownloadItemViewModel? _selectedTask;
    private string _globalSpeedText = "0.0 KB/s";

    private readonly ObservableCollection<DownloadItemViewModel> _allTasks = [];

    public ICollectionView FilteredTasks { get; }
    public ObservableCollection<DownloadItemViewModel> AllTasks => _allTasks;

    public string ActiveView
    {
        get => _activeView;
        set
        {
            if (SetField(ref _activeView, value))
            {
                OnPropertyChanged(nameof(IsDownloadsView));
                OnPropertyChanged(nameof(IsTelegramView));
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(IsDownloadingSelected));
                OnPropertyChanged(nameof(IsCompletedSelected));
                OnPropertyChanged(nameof(IsPausedSelected));
                OnPropertyChanged(nameof(IsFailedSelected));
                OnPropertyChanged(nameof(IsKompresiSelected));
                OnPropertyChanged(nameof(IsDokumenSelected));
                OnPropertyChanged(nameof(IsVideoSelected));
                OnPropertyChanged(nameof(IsMusikSelected));
                OnPropertyChanged(nameof(IsProgramSelected));
                if (value == "telegram")
                {
                    UpdateTelegramCounts();
                }
            }
        }
    }

    public bool IsDownloadsView => ActiveView == "downloads";
    public bool IsTelegramView => ActiveView == "telegram";

    public DownloadItemViewModel? SelectedTask
    {
        get => _selectedTask;
        set => SetField(ref _selectedTask, value);
    }

    private List<DownloadItemViewModel> _selectedTasks = [];
    public List<DownloadItemViewModel> SelectedTasks
    {
        get => _selectedTasks;
        set
        {
            if (SetField(ref _selectedTasks, value))
            {
                OnPropertyChanged(nameof(SelectedCountText));
                OnPropertyChanged(nameof(HasMultipleSelection));
            }
        }
    }

    public bool HasMultipleSelection => _selectedTasks.Count > 1;

    public string FilteredCountText => $"{_allTasks.Count} Berkas Terlihat";

    public string SelectedCountText => _selectedTasks.Count switch
    {
        > 1 => string.Format(I18n.Tr("table.filesCountSelected", "{0} berkas ({1} dipilih)"), _allTasks.Count, _selectedTasks.Count),
        _ => string.Format(I18n.Tr("table.filesCount", "{0} berkas"), _allTasks.Count)
    };

    public string ActiveCategory
    {
        get => _activeCategory;
        set
        {
            if (SetField(ref _activeCategory, value))
            {
                FilteredTasks.Refresh();
                OnPropertyChanged(nameof(IsAllSelected));
                OnPropertyChanged(nameof(IsDownloadingSelected));
                OnPropertyChanged(nameof(IsCompletedSelected));
                OnPropertyChanged(nameof(IsPausedSelected));
                OnPropertyChanged(nameof(IsFailedSelected));
                OnPropertyChanged(nameof(IsKompresiSelected));
                OnPropertyChanged(nameof(IsDokumenSelected));
                OnPropertyChanged(nameof(IsVideoSelected));
                OnPropertyChanged(nameof(IsMusikSelected));
                OnPropertyChanged(nameof(IsProgramSelected));
                if (value == "telegram")
                {
                    UpdateTelegramCounts();
                }
            }
        }
    }

    public bool IsAllSelected => IsDownloadsView && (ActiveCategory == "all" || string.IsNullOrEmpty(ActiveCategory));
    public bool IsDownloadingSelected => IsDownloadsView && ActiveCategory == "downloading";
    public bool IsCompletedSelected => IsDownloadsView && ActiveCategory == "completed";
    public bool IsPausedSelected => IsDownloadsView && ActiveCategory == "paused";
    public bool IsFailedSelected => IsDownloadsView && ActiveCategory == "failed";
    public bool IsKompresiSelected => IsDownloadsView && (ActiveCategory == "kompresi" || ActiveCategory == "compressed" || ActiveCategory == "arsip");
    public bool IsDokumenSelected => IsDownloadsView && (ActiveCategory == "dokumen" || ActiveCategory == "document" || ActiveCategory == "documents");
    public bool IsVideoSelected => IsDownloadsView && (ActiveCategory == "video" || ActiveCategory == "videos");
    public bool IsMusikSelected => IsDownloadsView && (ActiveCategory == "musik" || ActiveCategory == "music" || ActiveCategory == "audio");
    public bool IsProgramSelected => IsDownloadsView && (ActiveCategory == "program" || ActiveCategory == "aplikasi" || ActiveCategory == "programs");

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value))
            {
                FilteredTasks.Refresh();
            }
        }
    }

    public string GlobalSpeedText
    {
        get => _globalSpeedText;
        set => SetField(ref _globalSpeedText, value);
    }

    public long TotalVolumeBytes => _allTasks.Sum(t => t.Task.DownloadedBytes);
    public string TotalVolumeFormatted => DownloadItemViewModel.FormatBytes(TotalVolumeBytes);

    public int AllCount => _allTasks.Count;
    public int ActiveCount => _allTasks.Count(t => t.Status == DownloadStatus.Downloading);
    public int CompletedCount => _allTasks.Count(t => t.Status == DownloadStatus.Completed);
    public int PausedCount => _allTasks.Count(t => t.Status == DownloadStatus.Paused);
    public int FailedCount => _allTasks.Count(t => t.Status == DownloadStatus.Failed);

    public string ActiveCountText => string.Format(I18n.Tr("statusbar.active", "Aktif: {0} berkas"), ActiveCount);
    public string CompletedCountText => string.Format(I18n.Tr("statusbar.completed", "Selesai: {0} berkas"), CompletedCount);
    public string TotalVolumeText => string.Format(I18n.Tr("statusbar.downloadedData", "Data Terunduh: {0}"), TotalVolumeFormatted);

    public int KompresiCount => _allTasks.Count(t => 
        string.Equals(t.Category, "Kompresi", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Compressed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(t.Category, "Arsip", StringComparison.OrdinalIgnoreCase));

    public int DokumenCount => _allTasks.Count(t => 
        string.Equals(t.Category, "Dokumen", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Document", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Documents", StringComparison.OrdinalIgnoreCase));

    public int VideoCount => _allTasks.Count(t => 
        string.Equals(t.Category, "Video", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Videos", StringComparison.OrdinalIgnoreCase));

    public int MusikCount => _allTasks.Count(t => 
        string.Equals(t.Category, "Musik", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Music", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Audio", StringComparison.OrdinalIgnoreCase));

    public int ProgramCount => _allTasks.Count(t => 
        string.Equals(t.Category, "Program", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Aplikasi", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Programs", StringComparison.OrdinalIgnoreCase) || 
        string.Equals(t.Category, "Applications", StringComparison.OrdinalIgnoreCase));

    public ICommand SelectCategoryCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand ResumeAllCommand { get; }
    public ICommand ClearCompletedCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SwitchToDownloadsCommand { get; }
    public ICommand SwitchToTelegramCommand { get; }

    // Telegram Workspace & Vault State
    public ObservableCollection<TelegramChannelInfo> TelegramChannels { get; } = [];
    public ObservableCollection<TelegramChannelInfo> FilteredTelegramChannels { get; } = [];
    public ObservableCollection<TelegramMediaItem> TelegramMediaItems { get; } = [];
    public ObservableCollection<TelegramMediaItem> DisplayedTelegramMedia { get; } = [];

    public ICommand ClearTelegramSearchCommand { get; }

    private string _telegramSearchQuery = string.Empty;
    public string TelegramSearchQuery
    {
        get => _telegramSearchQuery;
        set
        {
            if (SetField(ref _telegramSearchQuery, value))
            {
                FilterTelegramChannels();
                OnPropertyChanged(nameof(HasTelegramSearchQuery));
                OnPropertyChanged(nameof(IsTelegramSearchEmpty));
                OnPropertyChanged(nameof(HasNoFilteredTelegramChannels));
            }
        }
    }

    public bool HasTelegramSearchQuery => !string.IsNullOrWhiteSpace(TelegramSearchQuery);
    public bool IsTelegramSearchEmpty => string.IsNullOrWhiteSpace(TelegramSearchQuery);
    public bool HasNoFilteredTelegramChannels => FilteredTelegramChannels.Count == 0 && TelegramChannels.Count > 0;

    private string _telegramChatInput = string.Empty;
    public string TelegramChatInput
    {
        get => _telegramChatInput;
        set
        {
            if (SetField(ref _telegramChatInput, value))
            {
                OnPropertyChanged(nameof(EffectiveTelegramTargetDir));
            }
        }
    }

    private string _telegramMediaFilter = "all";
    public string TelegramMediaFilter
    {
        get => _telegramMediaFilter;
        set
        {
            if (SetField(ref _telegramMediaFilter, value))
            {
                RefreshTelegramDisplayedMedia();
                OnPropertyChanged(nameof(IsTgFilterAll));
                OnPropertyChanged(nameof(IsTgFilterVideo));
                OnPropertyChanged(nameof(IsTgFilterGif));
                OnPropertyChanged(nameof(IsTgFilterPhoto));
                OnPropertyChanged(nameof(IsTgFilterDoc));
                OnPropertyChanged(nameof(IsTgFilterAudio));
            }
        }
    }

    public bool IsTgFilterAll => TelegramMediaFilter == "all";
    public bool IsTgFilterVideo => TelegramMediaFilter == "video";
    public bool IsTgFilterGif => TelegramMediaFilter == "gif";
    public bool IsTgFilterPhoto => TelegramMediaFilter == "photo";
    public bool IsTgFilterDoc => TelegramMediaFilter == "document";
    public bool IsTgFilterAudio => TelegramMediaFilter == "audio";

    private string _telegramStatusFilter = "all";
    public string TelegramStatusFilter
    {
        get => _telegramStatusFilter;
        set
        {
            if (SetField(ref _telegramStatusFilter, value))
            {
                RefreshTelegramDisplayedMedia();
                OnPropertyChanged(nameof(IsTgStatusAll));
                OnPropertyChanged(nameof(IsTgStatusUncompleted));
                OnPropertyChanged(nameof(IsTgStatusCompleted));
            }
        }
    }

    public bool IsTgStatusAll => TelegramStatusFilter == "all";
    public bool IsTgStatusUncompleted => TelegramStatusFilter == "uncompleted";
    public bool IsTgStatusCompleted => TelegramStatusFilter == "completed";

    private bool _telegramIsScanning;
    public bool TelegramIsScanning
    {
        get => _telegramIsScanning;
        set => SetField(ref _telegramIsScanning, value);
    }

    private string _telegramTargetDir = string.Empty;
    public string TelegramTargetDir
    {
        get => string.IsNullOrEmpty(_telegramTargetDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : _telegramTargetDir;
        set
        {
            if (SetField(ref _telegramTargetDir, value))
            {
                OnPropertyChanged(nameof(EffectiveTelegramTargetDir));
            }
        }
    }

    private bool _telegramUseSubfolder = true;
    public bool TelegramUseSubfolder
    {
        get => _telegramUseSubfolder;
        set
        {
            if (SetField(ref _telegramUseSubfolder, value))
            {
                OnPropertyChanged(nameof(EffectiveTelegramTargetDir));
            }
        }
    }

    public string EffectiveTelegramTargetDir
    {
        get
        {
            string baseDir = TelegramTargetDir;
            if (!TelegramUseSubfolder) return baseDir;
            string cleanChat = string.IsNullOrWhiteSpace(TelegramChatInput)
                ? "General"
                : TelegramChatInput.Trim().TrimStart('@').Replace("https://t.me/", "");
            return Path.Combine(baseDir, "Telegram", cleanChat);
        }
    }

    public TelegramAuthStatus TelegramAuthStatus => _engine?.TelegramService?.AuthStatus ?? new TelegramAuthStatus();
    public bool IsTelegramAuthenticated => TelegramAuthStatus.IsAuthenticated;
    public bool IsTelegramNotAuthenticated => !TelegramAuthStatus.IsAuthenticated;
    public bool HasTelegramMedia => DisplayedTelegramMedia.Count > 0;
    public bool HasNoTelegramMedia => DisplayedTelegramMedia.Count == 0;
    public int TelegramMediaCount => TelegramMediaItems.Count;
    public int DisplayedTelegramMediaCount => DisplayedTelegramMedia.Count;

    public string TelegramAccountDisplay
    {
        get
        {
            if (!TelegramAuthStatus.IsAuthenticated)
                return I18n.Tr("telegram.noAccount", "Belum Ada Akun");

            string disp = TelegramAuthStatus.DisplayName ?? "";
            string user = TelegramAuthStatus.Username ?? "";
            string phone = TelegramAuthStatus.PhoneNumber ?? "";

            if (!string.IsNullOrWhiteSpace(disp) && !string.IsNullOrWhiteSpace(user))
                return $"{disp} (@{user.TrimStart('@')})";
            if (!string.IsNullOrWhiteSpace(disp))
                return disp;
            if (!string.IsNullOrWhiteSpace(user))
                return $"@{user.TrimStart('@')}";
            if (!string.IsNullOrWhiteSpace(phone))
                return phone;
            return "User";
        }
    }

    public int TelegramSelectedCount => TelegramMediaItems.Count(m => m.IsSelected);
    public int TelegramCompletedCount => TelegramMediaItems.Count(m => m.IsCompleted);
    public int TelegramUncompletedCount => TelegramMediaItems.Count - TelegramCompletedCount;
    public bool HasCompletedTelegramItems => TelegramCompletedCount > 0;

    public long TelegramSelectedSizeBytes => TelegramMediaItems.Where(m => m.IsSelected).Sum(m => m.FileSize);
    public string TelegramSelectedSizeText => DownloadItemViewModel.FormatBytes(TelegramSelectedSizeBytes);

    public string TelegramAccountInitials => TelegramAuthStatus.Initials;

    private TelegramChannelInfo? _selectedTelegramChannel;
    public TelegramChannelInfo? SelectedTelegramChannel
    {
        get => _selectedTelegramChannel;
        set
        {
            if (SetField(ref _selectedTelegramChannel, value))
            {
                foreach (var ch in TelegramChannels)
                {
                    ch.IsSelected = (ch == value);
                }
                OnPropertyChanged(nameof(ActiveTelegramChannelTitle));
                OnPropertyChanged(nameof(ActiveTelegramChannelUsername));
                OnPropertyChanged(nameof(ActiveTelegramChannelIconKind));
                OnPropertyChanged(nameof(HasSelectedTelegramChannel));
                if (value != null)
                {
                    TelegramChatInput = !string.IsNullOrEmpty(value.Username) ? $"@{value.Username.TrimStart('@')}" : value.Title;
                    _ = ScanTelegramAsync(TelegramChatInput);
                }
            }
        }
    }

    public bool HasSelectedTelegramChannel => SelectedTelegramChannel != null;

    public string ActiveTelegramChannelTitle => SelectedTelegramChannel?.FormattedTitle 
        ?? (!string.IsNullOrWhiteSpace(TelegramChatInput) ? TelegramChatInput : "Pilih Saluran Telegram");

    public string ActiveTelegramChannelUsername => SelectedTelegramChannel?.FormattedUsername 
        ?? (!string.IsNullOrWhiteSpace(TelegramChatInput) ? TelegramChatInput : "");

    public string ActiveTelegramChannelIconKind => SelectedTelegramChannel?.ChatIconKind ?? "radio";

    private string _telegramSortOption = "Terbaru";
    public string TelegramSortOption
    {
        get => _telegramSortOption;
        set
        {
            if (SetField(ref _telegramSortOption, value))
            {
                RefreshTelegramDisplayedMedia();
            }
        }
    }
    public IReadOnlyList<string> TelegramSortOptions { get; } = ["Terbaru", "Terlama", "Ukuran Terbesar", "Ukuran Terkecil"];

    public int TelegramUncompletedSelectedCount => TelegramMediaItems.Count(m => m.IsSelected && !m.IsCompleted);
    public string TelegramUncompletedSelectedText => $"{TelegramUncompletedSelectedCount} berkas belum diunduh dipilih";

    public bool IsAllUncompletedTelegramSelected => 
        DisplayedTelegramMedia.Any(m => !m.IsCompleted) && 
        DisplayedTelegramMedia.Where(m => !m.IsCompleted).All(m => m.IsSelected);

    public string TelegramDisplayTelemetryText => 
        $"Menampilkan {DisplayedTelegramMedia.Count} dari {TelegramMediaCount} berkas ({TelegramCompletedCount} diunduh)";

    public string TelegramSelectedSummaryText => string.Format(
        I18n.Tr("telegram.filesFound", "{0} Berkas Terpilih"),
        TelegramSelectedCount > 0 ? TelegramSelectedCount : DisplayedTelegramMedia.Count
    );

    public string TelegramDownloadButtonText => TelegramSelectedCount > 0
        ? string.Format(I18n.Tr("telegram.downloadSelected", "Unduh Berkas Terpilih ({0})"), TelegramSelectedCount)
        : string.Format(I18n.Tr("telegram.downloadSelected", "Unduh Berkas Terpilih ({0})"), DisplayedTelegramMedia.Count);

    // Telegram Commands
    public ICommand ScanTelegramCommand { get; }
    public ICommand SelectTelegramChannelCommand { get; }
    public ICommand DeleteTelegramChannelCommand { get; }
    public ICommand AddTelegramChannelCommand { get; }
    public ICommand SetTelegramFilterCommand { get; }
    public ICommand SetTelegramStatusFilterCommand { get; }
    public ICommand ToggleSelectAllTelegramCommand { get; }
    public ICommand ToggleSelectTelegramItemCommand { get; }
    public ICommand SelectOnlyUncompletedTelegramCommand { get; }
    public ICommand SyncCurrentTelegramChannelCommand { get; }
    public ICommand DownloadSelectedTelegramCommand { get; }
    public ICommand DownloadSingleTelegramItemCommand { get; }
    public ICommand OpenFileTelegramCommand { get; }
    public ICommand ChangeTelegramDirCommand { get; }

    public Database Database => _database;
    public DownloadEngine Engine => _engine;

    public Action<BrowserDownloadRequest>? OnBrowserDownloadRequested;
    public event Action<DownloadTask>? OnTaskFinished;
    public Action<IReadOnlyList<DownloadItemViewModel>>? RequestDeleteConfirmation;

    public MainViewModel()
    {
        bool isDesign = DesignerProperties.GetIsInDesignMode(new DependencyObject());

        if (isDesign)
        {
            _database = null!;
            _engine = null!;
            _ipcServer = null;

            FilteredTasks = CollectionViewSource.GetDefaultView(_allTasks);

            // Mock tasks for designer preview
            _allTasks.Add(new DownloadItemViewModel(new DownloadTask
            {
                Filename = "Ubuntu-24.04-Desktop-amd64.iso",
                TotalBytes = 5_242_880_000,
                DownloadedBytes = 2_621_440_000,
                Status = DownloadStatus.Downloading,
                Category = "Program"
            }));
            _allTasks.Add(new DownloadItemViewModel(new DownloadTask
            {
                Filename = "Annual_Report_2026.pdf",
                TotalBytes = 15_428_800,
                DownloadedBytes = 15_428_800,
                Status = DownloadStatus.Completed,
                Category = "Dokumen"
            }));

            SelectCategoryCommand = new RelayCommand(_ => { });
            PauseCommand = new RelayCommand(() => { });
            ResumeCommand = new RelayCommand(() => { });
            PauseAllCommand = new RelayCommand(() => { });
            ResumeAllCommand = new RelayCommand(() => { });
            ClearCompletedCommand = new RelayCommand(() => { });
            DeleteCommand = new RelayCommand(() => { });
            OpenFileCommand = new RelayCommand(() => { });
            OpenFolderCommand = new RelayCommand(() => { });
            SwitchToDownloadsCommand = new RelayCommand(() => { });
            SwitchToTelegramCommand = new RelayCommand(() => { });

            ClearTelegramSearchCommand = new RelayCommand(() => TelegramSearchQuery = string.Empty);
            ScanTelegramCommand = new RelayCommand(_ => { });
            SelectTelegramChannelCommand = new RelayCommand(_ => { });
            DeleteTelegramChannelCommand = new RelayCommand(_ => { });
            AddTelegramChannelCommand = new RelayCommand(() => { });
            SetTelegramFilterCommand = new RelayCommand(_ => { });
            SetTelegramStatusFilterCommand = new RelayCommand(_ => { });
            ToggleSelectAllTelegramCommand = new RelayCommand(() => { });
            ToggleSelectTelegramItemCommand = new RelayCommand(_ => { });
            SelectOnlyUncompletedTelegramCommand = new RelayCommand(() => { });
            SyncCurrentTelegramChannelCommand = new RelayCommand(() => { });
            DownloadSelectedTelegramCommand = new RelayCommand(() => { });
            DownloadSingleTelegramItemCommand = new RelayCommand(_ => { });
            OpenFileTelegramCommand = new RelayCommand(_ => { });
            ChangeTelegramDirCommand = new RelayCommand(() => { });
            return;
        }

        _database = new Database();
        _engine = new DownloadEngine(_database);
        _ipcServer = new IpcServer();

        // Listen for Telegram auth changes
        _engine.TelegramService.OnAuthStatusChanged += _ =>
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() => UpdateTelegramCounts());
            }
            catch { }
        };

        // Restore Telegram session in background
        _ = Task.Run(async () =>
        {
            try
            {
                await _engine.TelegramService.CheckAndRestoreAuthAsync();
                Application.Current?.Dispatcher.Invoke(() => UpdateTelegramCounts());
            }
            catch { }
        });

        // Apply saved rate limits
        if (_database.GetSetting("global_limit_enabled") == "1" &&
            long.TryParse(_database.GetSetting("global_limit_kbps"), out long kbps))
        {
            _engine.GlobalLimiter.SetRate(kbps * 1024);
        }

        FilteredTasks = CollectionViewSource.GetDefaultView(_allTasks);
        FilteredTasks.Filter = FilterTaskItem;

        SelectCategoryCommand = new RelayCommand(p =>
        {
            ActiveView = "downloads";
            ActiveCategory = p?.ToString() ?? "all";
        });

        SwitchToDownloadsCommand = new RelayCommand(() => ActiveView = "downloads");
        SwitchToTelegramCommand = new RelayCommand(() =>
        {
            ActiveView = "telegram";
            UpdateTelegramCounts();
            if (!_engine.TelegramService.AuthStatus.IsAuthenticated && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDM", "telegram", "mdm_telegram.session")))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _engine.TelegramService.CheckAndRestoreAuthAsync();
                        Application.Current?.Dispatcher.Invoke(() => UpdateTelegramCounts());
                    }
                    catch { }
                });
            }
        });

        PauseCommand = new RelayCommand(param =>
        {
            var targets = param is DownloadItemViewModel single 
                ? [single] 
                : (SelectedTasks.Count > 0 ? SelectedTasks.ToList() : (SelectedTask != null ? [SelectedTask] : []));
            foreach (var t in targets)
            {
                if (t.Status == DownloadStatus.Downloading)
                {
                    _engine.PauseDownload(t.Id);
                }
            }
        });

        ResumeCommand = new RelayCommand(param =>
        {
            var targets = param is DownloadItemViewModel single 
                ? [single] 
                : (SelectedTasks.Count > 0 ? SelectedTasks.ToList() : (SelectedTask != null ? [SelectedTask] : []));
            foreach (var t in targets)
            {
                if (t.Status != DownloadStatus.Downloading)
                {
                    _engine.StartDownload(t.Task);
                }
            }
        });

        PauseAllCommand = new RelayCommand(() =>
        {
            foreach (var t in _allTasks.Where(x => x.Status == DownloadStatus.Downloading))
            {
                _engine.PauseDownload(t.Id);
            }
        });

        ResumeAllCommand = new RelayCommand(() =>
        {
            foreach (var t in _allTasks.Where(x => x.Status is DownloadStatus.Paused or DownloadStatus.Failed))
            {
                _engine.StartDownload(t.Task);
            }
        });

        ClearCompletedCommand = new RelayCommand(() =>
        {
            var completed = _allTasks.Where(t => t.Status == DownloadStatus.Completed).ToList();
            foreach (var t in completed)
            {
                _database.DeleteTask(t.Id);
                _allTasks.Remove(t);
            }
            UpdateCounts();
        });

        DeleteCommand = new RelayCommand(param =>
        {
            var targets = param is DownloadItemViewModel single 
                ? [single] 
                : (SelectedTasks.Count > 0 ? SelectedTasks.ToList() : (SelectedTask != null ? [SelectedTask] : []));
            if (targets.Count == 0) return;

            if (RequestDeleteConfirmation != null)
            {
                RequestDeleteConfirmation.Invoke(targets);
            }
            else
            {
                DeleteTasks(targets, deleteFilesFromDisk: false);
            }
        });

        OpenFileCommand = new RelayCommand(param =>
        {
            var target = param is DownloadItemViewModel single ? single : SelectedTask;
            if (target != null && File.Exists(target.FilePath))
            {
                Process.Start(new ProcessStartInfo(target.FilePath) { UseShellExecute = true });
            }
        });

        OpenFolderCommand = new RelayCommand(() =>
        {
            if (SelectedTask != null)
            {
                string dir = Path.GetDirectoryName(SelectedTask.FilePath) ?? SelectedTask.Task.SaveDir;
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedTask.FilePath}\"") { UseShellExecute = true });
                }
            }
        });

        // Hook engine events safely
        _engine.ProgressUpdated += (s, e) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var item = _allTasks.FirstOrDefault(t => t.Id == e.TaskId);
                item?.UpdateMetrics(e.DownloadedBytes, e.TotalBytes, e.SpeedBps, e.EtaSeconds, e.Segments);

                if (item != null)
                {
                    var tgMedia = TelegramMediaItems.FirstOrDefault(m => m.TgUrl == item.Url || item.Url.EndsWith($"/msg/{m.MessageId}"));
                    if (tgMedia != null)
                    {
                        tgMedia.Status = "Sedang Mengunduh";
                        tgMedia.Progress = item.Progress;
                    }
                }

                UpdateSpeedMetrics(e.SpeedBps);
                UpdateCounts();
                UpdateTelegramCounts();
            });
        };

        _engine.TaskCompleted += (s, taskId) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var item = _allTasks.FirstOrDefault(t => t.Id == taskId);
                item?.MarkCompleted();

                if (item != null)
                {
                    var tgMedia = TelegramMediaItems.FirstOrDefault(m => m.TgUrl == item.Url || item.Url.EndsWith($"/msg/{m.MessageId}"));
                    if (tgMedia != null)
                    {
                        tgMedia.Status = "Terunduh";
                        tgMedia.Progress = 1.0;
                    }
                }

                UpdateSpeedMetrics(0);
                UpdateCounts();
                UpdateTelegramCounts();
                if (item != null)
                {
                    OnTaskFinished?.Invoke(item.Task);
                }
            });
        };

        _engine.TaskPaused += (s, taskId) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var item = _allTasks.FirstOrDefault(t => t.Id == taskId);
                item?.MarkPaused();

                if (item != null)
                {
                    var tgMedia = TelegramMediaItems.FirstOrDefault(m => m.TgUrl == item.Url || item.Url.EndsWith($"/msg/{m.MessageId}"));
                    if (tgMedia != null)
                    {
                        tgMedia.Status = "Jeda";
                    }
                }

                UpdateSpeedMetrics(0);
                UpdateCounts();
                UpdateTelegramCounts();
            });
        };

        _engine.TaskFailed += (s, err) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var item = _allTasks.FirstOrDefault(t => t.Id == err.TaskId);
                item?.MarkFailed(err.Error);

                if (item != null)
                {
                    var tgMedia = TelegramMediaItems.FirstOrDefault(m => m.TgUrl == item.Url || item.Url.EndsWith($"/msg/{m.MessageId}"));
                    if (tgMedia != null)
                    {
                        tgMedia.Status = "Gagal";
                    }
                }

                UpdateSpeedMetrics(0);
                UpdateCounts();
                UpdateTelegramCounts();
            });
        };

        // Hook IPC safely
        _ipcServer.DownloadRequested += (s, req) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OnBrowserDownloadRequested?.Invoke(req);
            });
        };

        I18n.Instance.LanguageChanged += (s, lang) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                UpdateCounts();
                UpdateTelegramCounts();
                foreach (var task in _allTasks)
                {
                    task.RefreshLocalization();
                }
            });
        };

        _ipcServer.Start();
        LoadInitialTasks();

        // Telegram Commands
        ClearTelegramSearchCommand = new RelayCommand(() => TelegramSearchQuery = string.Empty);

        ScanTelegramCommand = new RelayCommand(p =>
        {
            string? param = p?.ToString();
            bool loadMore = string.Equals(param, "loadMore", StringComparison.OrdinalIgnoreCase);
            string? input = (!loadMore && !string.IsNullOrWhiteSpace(param)) ? param : TelegramChatInput;
            _ = ScanTelegramAsync(input, TelegramMediaFilter, loadMore);
        });

        SelectTelegramChannelCommand = new RelayCommand(p =>
        {
            if (p is TelegramChannelInfo ch)
            {
                SelectTelegramChannel(ch);
            }
        });

        DeleteTelegramChannelCommand = new RelayCommand(p =>
        {
            if (p is TelegramChannelInfo ch)
            {
                DeleteTelegramChannel(ch);
            }
        });

        AddTelegramChannelCommand = new RelayCommand(() =>
        {
            ActiveView = "telegram";
            TelegramChatInput = "";
        });

        SetTelegramFilterCommand = new RelayCommand(p =>
        {
            if (p != null) TelegramMediaFilter = p.ToString()!;
        });

        SetTelegramStatusFilterCommand = new RelayCommand(p =>
        {
            if (p != null) TelegramStatusFilter = p.ToString()!;
        });

        ToggleSelectAllTelegramCommand = new RelayCommand(() =>
        {
            bool allSelected = DisplayedTelegramMedia.Count > 0 && DisplayedTelegramMedia.All(m => m.IsSelected);
            foreach (var m in DisplayedTelegramMedia)
            {
                m.IsSelected = !allSelected;
            }
            UpdateTelegramCounts();
        });

        ToggleSelectTelegramItemCommand = new RelayCommand(p =>
        {
            if (p is TelegramMediaItem item)
            {
                item.IsSelected = !item.IsSelected;
                UpdateTelegramCounts();
            }
        });

        SelectOnlyUncompletedTelegramCommand = new RelayCommand(() =>
        {
            var uncompleted = DisplayedTelegramMedia.Where(m => !m.IsCompleted).ToList();
            if (uncompleted.Count == 0) return;
            bool allUncompletedSelected = uncompleted.All(m => m.IsSelected);
            foreach (var m in DisplayedTelegramMedia)
            {
                if (!m.IsCompleted)
                {
                    m.IsSelected = !allUncompletedSelected;
                }
                else
                {
                    m.IsSelected = false;
                }
            }
            UpdateTelegramCounts();
        });

        SyncCurrentTelegramChannelCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrWhiteSpace(TelegramChatInput))
            {
                _ = ScanTelegramAsync(TelegramChatInput);
            }
            else if (SelectedTelegramChannel != null)
            {
                SelectTelegramChannel(SelectedTelegramChannel);
            }
        });

        DownloadSelectedTelegramCommand = new RelayCommand(() =>
        {
            DownloadSelectedTelegram();
        });

        DownloadSingleTelegramItemCommand = new RelayCommand(p =>
        {
            if (p is TelegramMediaItem item)
            {
                DownloadSingleTelegramItem(item);
            }
        });

        OpenFileTelegramCommand = new RelayCommand(p =>
        {
            if (p is TelegramMediaItem item)
            {
                string targetPath = Path.Combine(EffectiveTelegramTargetDir, item.Filename);
                if (File.Exists(targetPath))
                {
                    Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
                }
            }
        });

        ChangeTelegramDirCommand = new RelayCommand(() =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Pilih Direktori Penyimpanan Telegram",
                InitialDirectory = TelegramTargetDir
            };
            if (dlg.ShowDialog() == true)
            {
                TelegramTargetDir = dlg.FolderName;
            }
        });

        UpdateTelegramCounts();
        _ = FetchTelegramDialogsAsync();
    }

    private void UpdateSpeedMetrics(long speedBps)
    {
        GlobalSpeedText = $"{DownloadItemViewModel.FormatBytes(speedBps)}/s";
    }

    private void LoadInitialTasks()
    {
        var tasks = _database.LoadAllTasks();
        foreach (var t in tasks)
        {
            _allTasks.Add(new DownloadItemViewModel(t));
        }
        UpdateCounts();
    }

    public async Task StartNewDownloadAsync(
        string url,
        string saveDir,
        string? filename,
        int threads,
        string? referer = null,
        string? category = null,
        bool startImmediately = true)
    {
        var task = await _engine.ProbeAndCreateTaskAsync(url, saveDir, filename, threads, referer, category);
        if (!startImmediately)
        {
            task.Status = DownloadStatus.Paused;
            _database.UpsertTask(task);
        }

        var vm = new DownloadItemViewModel(task);
        _allTasks.Insert(0, vm);
        SelectedTask = vm;
        UpdateCounts();

        if (startImmediately)
        {
            _engine.StartDownload(task);
        }
    }

    public void DeleteTasks(IReadOnlyList<DownloadItemViewModel> targets, bool deleteFilesFromDisk)
    {
        foreach (var t in targets)
        {
            _engine.PauseDownload(t.Id);
            _database.DeleteTask(t.Id);

            if (deleteFilesFromDisk)
            {
                try
                {
                    if (File.Exists(t.FilePath))
                    {
                        File.Delete(t.FilePath);
                    }
                    string part = t.FilePath + ".part";
                    if (File.Exists(part))
                    {
                        File.Delete(part);
                    }
                }
                catch { /* file locked or already deleted */ }
            }

            _allTasks.Remove(t);
        }

        SelectedTasks = [];
        SelectedTask = null;
        UpdateCounts();
    }

    private bool FilterTaskItem(object obj)
    {
        if (obj is not DownloadItemViewModel item) return false;

        // Search filter
        if (!string.IsNullOrWhiteSpace(_searchQuery) &&
            !item.Filename.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Category filter
        return _activeCategory.ToLowerInvariant() switch
        {
            "downloading" => item.Status == DownloadStatus.Downloading,
            "completed" => item.Status == DownloadStatus.Completed,
            "paused" => item.Status == DownloadStatus.Paused,
            "failed" => item.Status == DownloadStatus.Failed,
            "kompresi" or "compressed" => 
                string.Equals(item.Category, "Kompresi", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Compressed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category, "Arsip", StringComparison.OrdinalIgnoreCase),
            "dokumen" or "documents" => 
                string.Equals(item.Category, "Dokumen", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Document", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Documents", StringComparison.OrdinalIgnoreCase),
            "video" or "videos" => 
                string.Equals(item.Category, "Video", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Videos", StringComparison.OrdinalIgnoreCase),
            "musik" or "music" or "audio" => 
                string.Equals(item.Category, "Musik", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Music", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Audio", StringComparison.OrdinalIgnoreCase),
            "program" or "aplikasi" => 
                string.Equals(item.Category, "Program", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Aplikasi", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Programs", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(item.Category, "Applications", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    public void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(PausedCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(KompresiCount));
        OnPropertyChanged(nameof(DokumenCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(MusikCount));
        OnPropertyChanged(nameof(ProgramCount));
        OnPropertyChanged(nameof(TotalVolumeBytes));
        OnPropertyChanged(nameof(TotalVolumeFormatted));
        OnPropertyChanged(nameof(TotalVolumeText));
        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(CompletedCountText));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(FilteredCountText));
    }

    public async Task ScanTelegramAsync(string? input = null, string? filter = null, bool loadMore = false)
    {
        if (!string.IsNullOrEmpty(input)) TelegramChatInput = input;
        if (!string.IsNullOrEmpty(filter)) TelegramMediaFilter = filter;
        if (string.IsNullOrWhiteSpace(TelegramChatInput))
        {
            if (!loadMore) TelegramMediaItems.Clear();
            RefreshTelegramDisplayedMedia();
            UpdateTelegramCounts();
            return;
        }
        TelegramIsScanning = true;

        try
        {
            if (!loadMore) TelegramMediaItems.Clear();
            var items = await _engine.TelegramService.ScanChatMediaAsync(TelegramChatInput, TelegramMediaFilter);

            foreach (var item in items)
            {
                // Sync with existing download task status
                var existing = _allTasks.FirstOrDefault(t => t.Url == item.TgUrl || t.Url.EndsWith($"/msg/{item.MessageId}"));
                if (existing != null)
                {
                    if (existing.Status == DownloadStatus.Completed)
                    {
                        item.Status = "Terunduh";
                        item.Progress = 1.0;
                    }
                    else if (existing.Status == DownloadStatus.Downloading)
                    {
                        item.Status = "Sedang Mengunduh";
                        item.Progress = existing.Progress;
                    }
                    else if (existing.Status == DownloadStatus.Paused)
                    {
                        item.Status = "Jeda";
                    }
                }

                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(TelegramMediaItem.IsSelected))
                    {
                        UpdateTelegramCounts();
                    }
                };

                TelegramMediaItems.Add(item);
            }

            RefreshTelegramDisplayedMedia();
            RefreshTelegramChannels();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Telegram scan error: {ex.Message}");
        }
        finally
        {
            TelegramIsScanning = false;
            UpdateTelegramCounts();
        }
    }

    public async Task FetchTelegramDialogsAsync()
    {
        try
        {
            if (_engine.TelegramService.AuthStatus.IsAuthenticated)
            {
                var dialogs = await _engine.TelegramService.ListDialogsAsync();
                if (dialogs.Count > 0)
                {
                    TelegramChannels.Clear();
                    foreach (var d in dialogs)
                    {
                        TelegramChannels.Add(d);
                    }
                    FilterTelegramChannels();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Fetch Telegram dialogs error: {ex.Message}");
        }

        RefreshTelegramChannels();
    }

    public void RefreshTelegramChannels()
    {
        var channels = _database.GetTelegramChannels();
        TelegramChannels.Clear();
        foreach (var ch in channels)
        {
            TelegramChannels.Add(ch);
        }
        FilterTelegramChannels();

        if (_selectedTelegramChannel == null && FilteredTelegramChannels.Count > 0)
        {
            SelectedTelegramChannel = FilteredTelegramChannels[0];
        }
        else if (_selectedTelegramChannel != null)
        {
            var match = FilteredTelegramChannels.FirstOrDefault(c => c.Id == _selectedTelegramChannel.Id);
            if (match != null)
            {
                match.IsSelected = true;
            }
        }
    }

    public void FilterTelegramChannels()
    {
        FilteredTelegramChannels.Clear();
        var q = _telegramSearchQuery?.Trim().ToLowerInvariant() ?? string.Empty;
        foreach (var ch in TelegramChannels)
        {
            if (string.IsNullOrEmpty(q) ||
                (!string.IsNullOrEmpty(ch.Title) && ch.Title.ToLowerInvariant().Contains(q)) ||
                (!string.IsNullOrEmpty(ch.Username) && ch.Username.ToLowerInvariant().Contains(q)))
            {
                FilteredTelegramChannels.Add(ch);
            }
        }
        OnPropertyChanged(nameof(HasNoFilteredTelegramChannels));
    }

    public void SelectTelegramChannel(TelegramChannelInfo channel)
    {
        ActiveView = "telegram";
        SelectedTelegramChannel = channel;
    }

    public void DeleteTelegramChannel(TelegramChannelInfo channel)
    {
        _database.DeleteTelegramChannel(channel.Id);
        TelegramChannels.Remove(channel);
        FilteredTelegramChannels.Remove(channel);
        if (_selectedTelegramChannel == channel)
        {
            SelectedTelegramChannel = FilteredTelegramChannels.FirstOrDefault();
        }
        OnPropertyChanged(nameof(HasNoFilteredTelegramChannels));
    }

    public void RefreshTelegramDisplayedMedia()
    {
        var filtered = TelegramMediaItems.Where(m =>
        {
            // Status filter
            if (TelegramStatusFilter == "completed" && !m.IsCompleted) return false;
            if (TelegramStatusFilter == "uncompleted" && m.IsCompleted) return false;

            // Type filter
            if (TelegramMediaFilter != "all" && !string.Equals(m.MediaType, TelegramMediaFilter, StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        });

        // Sorting
        IEnumerable<TelegramMediaItem> sorted = TelegramSortOption switch
        {
            "Terlama" => filtered.OrderBy(m => m.CreatedAt).ThenBy(m => m.MessageId),
            "Ukuran Terbesar" => filtered.OrderByDescending(m => m.FileSize),
            "Ukuran Terkecil" => filtered.OrderBy(m => m.FileSize),
            _ => filtered.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.MessageId) // "Terbaru"
        };

        DisplayedTelegramMedia.Clear();
        foreach (var item in sorted)
        {
            DisplayedTelegramMedia.Add(item);
        }
        UpdateTelegramCounts();
    }

    public void DownloadSelectedTelegram()
    {
        var targets = TelegramMediaItems.Where(m => m.IsSelected).ToList();
        if (targets.Count == 0)
        {
            targets = DisplayedTelegramMedia.ToList();
        }

        string saveDir = EffectiveTelegramTargetDir;
        if (!Directory.Exists(saveDir))
        {
            try { Directory.CreateDirectory(saveDir); } catch { }
        }

        foreach (var item in targets)
        {
            string filePath = Path.Combine(saveDir, item.Filename);
            var task = new DownloadTask
            {
                Url = item.TgUrl,
                Filename = item.Filename,
                SaveDir = saveDir,
                FilePath = filePath,
                TotalBytes = item.FileSize > 0 ? item.FileSize : null,
                DownloadedBytes = 0,
                Status = DownloadStatus.Downloading,
                Connections = 1,
                Category = CategoryHelper.DetectCategory(item.Filename),
                Referer = item.ChatTitle,
                Source = "telegram",
                TelegramChannelTitle = item.ChatTitle
            };

            _database.UpsertTask(task);
            var itemVm = new DownloadItemViewModel(task);
            _allTasks.Insert(0, itemVm);
            _engine.StartDownload(task);

            item.Status = "Sedang Mengunduh";
            item.IsSelected = false;
        }

        UpdateCounts();
        UpdateTelegramCounts();
    }

    public void DownloadSingleTelegramItem(TelegramMediaItem item)
    {
        string saveDir = EffectiveTelegramTargetDir;
        if (!Directory.Exists(saveDir))
        {
            try { Directory.CreateDirectory(saveDir); } catch { }
        }

        string filePath = Path.Combine(saveDir, item.Filename);
        var task = new DownloadTask
        {
            Url = item.TgUrl,
            Filename = item.Filename,
            SaveDir = saveDir,
            FilePath = filePath,
            TotalBytes = item.FileSize > 0 ? item.FileSize : null,
            DownloadedBytes = 0,
            Status = DownloadStatus.Downloading,
            Connections = 1,
            Category = CategoryHelper.DetectCategory(item.Filename),
            Referer = item.ChatTitle,
            Source = "telegram",
            TelegramChannelTitle = item.ChatTitle
        };

        _database.UpsertTask(task);
        var itemVm = new DownloadItemViewModel(task);
        _allTasks.Insert(0, itemVm);
        _engine.StartDownload(task);

        item.Status = "Sedang Mengunduh";
        UpdateCounts();
        UpdateTelegramCounts();
    }

    public void UpdateTelegramCounts()
    {
        OnPropertyChanged(nameof(TelegramSelectedCount));
        OnPropertyChanged(nameof(TelegramCompletedCount));
        OnPropertyChanged(nameof(TelegramUncompletedCount));
        OnPropertyChanged(nameof(HasCompletedTelegramItems));
        OnPropertyChanged(nameof(TelegramSelectedSizeBytes));
        OnPropertyChanged(nameof(TelegramSelectedSizeText));
        OnPropertyChanged(nameof(TelegramSelectedSummaryText));
        OnPropertyChanged(nameof(TelegramDownloadButtonText));
        OnPropertyChanged(nameof(TelegramAuthStatus));
        OnPropertyChanged(nameof(TelegramAccountInitials));
        OnPropertyChanged(nameof(ActiveTelegramChannelTitle));
        OnPropertyChanged(nameof(ActiveTelegramChannelUsername));
        OnPropertyChanged(nameof(ActiveTelegramChannelIconKind));
        OnPropertyChanged(nameof(HasSelectedTelegramChannel));
        OnPropertyChanged(nameof(IsTelegramAuthenticated));
        OnPropertyChanged(nameof(IsTelegramNotAuthenticated));
        OnPropertyChanged(nameof(HasTelegramMedia));
        OnPropertyChanged(nameof(HasNoTelegramMedia));
        OnPropertyChanged(nameof(TelegramMediaCount));
        OnPropertyChanged(nameof(DisplayedTelegramMediaCount));
        OnPropertyChanged(nameof(TelegramAccountDisplay));
        OnPropertyChanged(nameof(TelegramUncompletedSelectedCount));
        OnPropertyChanged(nameof(TelegramUncompletedSelectedText));
        OnPropertyChanged(nameof(TelegramDisplayTelemetryText));
        OnPropertyChanged(nameof(IsAllUncompletedTelegramSelected));
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
