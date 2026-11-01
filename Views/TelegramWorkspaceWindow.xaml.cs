using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mdm.Models;
using Mdm.Services;
using Mdm.ViewModels;
using Microsoft.Win32;

namespace Mdm.Views;

public partial class TelegramWorkspaceWindow : Window
{
    private readonly TelegramService _telegramService;
    private readonly MainViewModel _mainViewModel;
    private readonly ObservableCollection<TelegramMediaItem> _allMediaItems = [];
    private string _activeFilter = "all";
    private string _baseDownloadDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public TelegramWorkspaceWindow(TelegramService telegramService, MainViewModel mainViewModel)
    {
        _telegramService = telegramService;
        _mainViewModel = mainViewModel;

        _baseDownloadDir = _mainViewModel.Database.GetSetting("default_dir") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        InitializeComponent();

        UpdateTargetDirDisplay();
        MediaGrid.ItemsSource = _allMediaItems;

        Loaded += async (_, _) =>
        {
            await RefreshAuthStatusAsync();
        };
    }

    private async Task RefreshAuthStatusAsync()
    {
        var status = await _telegramService.CheckAndRestoreAuthAsync();
        UpdateAuthUi(status);
    }

    private void UpdateAuthUi(TelegramAuthStatus status)
    {
        if (status.IsAuthenticated)
        {
            AuthBadge.Background = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
            AuthBadge.BorderBrush = (Brush)FindResource("AccentEmeraldBrush");
            AuthBadgeText.Text = "MTProto Connected";
            AuthBadgeText.Foreground = (Brush)FindResource("AccentEmeraldBrush");

            string userDisplay = !string.IsNullOrEmpty(status.Username) ? $"@{status.Username}" : status.PhoneNumber ?? "User";
            AccountNameText.Text = $"{status.DisplayName} ({userDisplay})";
            AccountNameText.Foreground = (Brush)FindResource("AccentEmeraldBrush");

            AuthActionBtn.Content = "Keluar (Logout)";
        }
        else
        {
            AuthBadge.Background = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
            AuthBadge.BorderBrush = (Brush)FindResource("AccentAmberBrush");
            AuthBadgeText.Text = "Belum Login";
            AuthBadgeText.Foreground = (Brush)FindResource("AccentAmberBrush");

            AccountNameText.Text = "Belum Ada Akun";
            AccountNameText.Foreground = (Brush)FindResource("AccentAmberBrush");

            AuthActionBtn.Content = "Login Akun";
        }
    }

    private async void AuthAction_Click(object sender, RoutedEventArgs e)
    {
        if (_telegramService.AuthStatus.IsAuthenticated)
        {
            var confirm = MessageBox.Show(this, "Apakah Anda yakin ingin logout dari sesi Telegram?", "Konfirmasi Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                await _telegramService.LogoutAsync();
                UpdateAuthUi(_telegramService.AuthStatus);
            }
        }
        else
        {
            var authDlg = new TelegramAuthDialog(_telegramService)
            {
                Owner = this
            };

            if (authDlg.ShowDialog() == true && authDlg.IsSuccess)
            {
                UpdateAuthUi(_telegramService.AuthStatus);
            }
        }
    }

    private void UpdateTargetDirDisplay()
    {
        if (string.IsNullOrEmpty(_baseDownloadDir)) return;

        string channelPart = ChatInputTextBox != null && !string.IsNullOrWhiteSpace(ChatInputTextBox.Text) 
            ? ChatInputTextBox.Text.Trim().TrimStart('@') 
            : "General";

        string target = SubfolderCheckBox?.IsChecked == true
            ? Path.Combine(_baseDownloadDir, "Telegram", channelPart)
            : _baseDownloadDir;

        if (TargetDirText != null)
        {
            TargetDirText.Text = target;
        }
    }

    private void ChangeDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pilih Direktori Penyimpanan Telegram",
            InitialDirectory = _baseDownloadDir
        };

        if (dlg.ShowDialog() == true)
        {
            _baseDownloadDir = dlg.FolderName;
            UpdateTargetDirDisplay();
        }
    }

    private void Subfolder_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTargetDirDisplay();
    }

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Scan_Click(sender, e);
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (!_telegramService.AuthStatus.IsAuthenticated)
        {
            MessageBox.Show(this, "Silakan login ke akun Telegram Anda terlebih dahulu.", "Belum Login", MessageBoxButton.OK, MessageBoxImage.Information);
            AuthAction_Click(sender, e);
            return;
        }

        string query = ChatInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(this, "Ketikkan nama obrolan, @username, link t.me/..., atau ID channel Telegram.", "Input Kosong", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateTargetDirDisplay();
        ScanBtn.IsEnabled = false;
        ScanBtnText.Text = "Memindai...";

        try
        {
            _allMediaItems.Clear();
            var items = await _telegramService.ScanChatMediaAsync(query, _activeFilter, limit: 100);

            foreach (var item in items)
            {
                // Hook selection change
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(TelegramMediaItem.IsSelected))
                    {
                        UpdateSummary();
                    }
                };
                _allMediaItems.Add(item);
            }

            UpdateSummary();
            if (items.Count == 0)
            {
                MessageBox.Show(this, "Tidak ada berkas media yang ditemukan pada channel/obrolan ini.", "Pindai Selesai", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Gagal memindai media chat: {ex.Message}", "Kesalahan Pindai", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ScanBtn.IsEnabled = true;
            ScanBtnText.Text = "Pindai Media Chat";
        }
    }

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag != null)
        {
            _activeFilter = rb.Tag.ToString() ?? "all";
            if (!string.IsNullOrWhiteSpace(ChatInputTextBox.Text) && _allMediaItems.Count > 0)
            {
                Scan_Click(sender, e);
            }
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _allMediaItems) item.IsSelected = true;
        UpdateSummary();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _allMediaItems) item.IsSelected = false;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        int total = _allMediaItems.Count;
        int selected = _allMediaItems.Count(i => i.IsSelected);
        long selectedBytes = _allMediaItems.Where(i => i.IsSelected).Sum(i => i.FileSize);

        SelectedSummaryText.Text = $"{selected} dari {total} berkas terpilih ({DownloadItemViewModel.FormatBytes(selectedBytes)})";
        DownloadSelectedBtn.Content = selected > 0 
            ? $"Unduh Berkas Terpilih ({selected})" 
            : "Unduh Berkas Terpilih";
    }

    private async void DownloadSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = _allMediaItems.Where(i => i.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            MessageBox.Show(this, "Pilih setidaknya satu berkas media yang ingin diunduh.", "Pilihan Kosong", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string saveDir = TargetDirText.Text;
        if (!Directory.Exists(saveDir))
        {
            try { Directory.CreateDirectory(saveDir); } catch { }
        }

        int queuedCount = 0;
        foreach (var item in selectedItems)
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
                Referer = item.ChatTitle
            };

            _mainViewModel.Database.UpsertTask(task);
            var itemVm = new DownloadItemViewModel(task);
            _mainViewModel.AllTasks.Insert(0, itemVm);
            _mainViewModel.Engine.StartDownload(task);

            item.Status = "Sedang Mengunduh";
            queuedCount++;
        }

        _mainViewModel.UpdateCounts();
        MessageBox.Show(this, $"{queuedCount} berkas media telah dimasukkan ke antrean unduhan MDM.", "Unduhan Dimulai", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

