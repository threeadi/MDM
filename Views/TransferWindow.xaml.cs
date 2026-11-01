using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Mdm.Models;
using Mdm.Services;
using Mdm.ViewModels;

namespace Mdm.Views;

public partial class TransferWindow : Window
{
    private readonly DownloadItemViewModel _item;
    private readonly DownloadEngine? _engine;
    private readonly Database? _database;

    public TransferWindow(DownloadItemViewModel item, DownloadEngine? engine = null, Database? database = null)
    {
        InitializeComponent();
        _item = item;
        _engine = engine;
        _database = database;
        DataContext = item;

        UpdatePauseResumeButton();
        InitializeLimiterUi();
        _item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DownloadItemViewModel.Status))
            {
                Dispatcher.Invoke(UpdatePauseResumeButton);
            }
        };

        I18n.Instance.LanguageChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePauseResumeButton();
                if (SegmentDetailsPanel != null && ToggleDetailsText != null)
                {
                    ToggleDetailsText.Text = SegmentDetailsPanel.Visibility == Visibility.Visible
                        ? I18n.Tr("transfer.hideThreadDetails", "Sembunyikan Rincian")
                        : I18n.Tr("transfer.showThreadDetails", "Tampilkan Rincian Thread");
                }
            });
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdatePauseResumeButton()
    {
        if (PauseResumeText == null || PauseResumeIcon == null) return;

        if (_item.Status == DownloadStatus.Downloading)
        {
            PauseResumeText.Text = I18n.Tr("transfer.pause", "Jeda");
            PauseResumeIcon.Kind = "pause";
            PauseResumeIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentAmberBrush");
        }
        else
        {
            PauseResumeText.Text = I18n.Tr("transfer.resume", "Lanjut");
            PauseResumeIcon.Kind = "play";
            PauseResumeIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentEmeraldBrush");
        }
    }

    private void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;

        if (_item.Status == DownloadStatus.Downloading)
        {
            _engine.PauseDownload(_item.Id);
        }
        else
        {
            _engine.StartDownload(_item.Task);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_engine != null && _item.Status == DownloadStatus.Downloading)
        {
            _engine.PauseDownload(_item.Id);
        }
        Close();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelStatus == null || PanelLimiter == null || PanelOptions == null) return;

        if (TabRadioStatus?.IsChecked == true)
        {
            PanelStatus.Visibility = Visibility.Visible;
            PanelLimiter.Visibility = Visibility.Collapsed;
            PanelOptions.Visibility = Visibility.Collapsed;
        }
        else if (TabRadioLimiter?.IsChecked == true)
        {
            PanelStatus.Visibility = Visibility.Collapsed;
            PanelLimiter.Visibility = Visibility.Visible;
            PanelOptions.Visibility = Visibility.Collapsed;
        }
        else if (TabRadioOptions?.IsChecked == true)
        {
            PanelStatus.Visibility = Visibility.Collapsed;
            PanelLimiter.Visibility = Visibility.Collapsed;
            PanelOptions.Visibility = Visibility.Visible;
        }
    }

    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SegmentDetailsPanel == null) return;

        if (SegmentDetailsPanel.Visibility == Visibility.Visible)
        {
            SegmentDetailsPanel.Visibility = Visibility.Collapsed;
            ToggleDetailsText.Text = I18n.Tr("transfer.showThreadDetails", "Tampilkan Rincian Thread");
            ToggleChevronIcon.Kind = "chevron-down";
        }
        else
        {
            SegmentDetailsPanel.Visibility = Visibility.Visible;
            ToggleDetailsText.Text = I18n.Tr("transfer.hideThreadDetails", "Sembunyikan Rincian");
            ToggleChevronIcon.Kind = "chevron-up";
        }
    }

    private bool _suppressLimiterEvents = true;

    private void InitializeLimiterUi()
    {
        if (TaskLimiterCheckBox == null || TaskLimitValueTextBox == null || TaskLimitUnitComboBox == null) return;

        _suppressLimiterEvents = true;
        try
        {
            long bps = _item.Task.SpeedLimitBps ?? 0;
            if (bps > 0)
            {
                TaskLimiterCheckBox.IsChecked = true;
                TaskLimitValueTextBox.IsEnabled = true;
                TaskLimitUnitComboBox.IsEnabled = true;

                if (bps >= 1024 * 1024 && bps % (1024 * 1024) == 0)
                {
                    TaskLimitValueTextBox.Text = (bps / (1024 * 1024)).ToString();
                    TaskLimitUnitComboBox.SelectedIndex = 1; // MB/s
                }
                else
                {
                    TaskLimitValueTextBox.Text = Math.Max(1, bps / 1024).ToString();
                    TaskLimitUnitComboBox.SelectedIndex = 0; // KB/s
                }
            }
            else
            {
                TaskLimiterCheckBox.IsChecked = false;
                TaskLimitValueTextBox.IsEnabled = false;
                TaskLimitUnitComboBox.IsEnabled = false;
                TaskLimitValueTextBox.Text = "1";
                TaskLimitUnitComboBox.SelectedIndex = 0; // KB/s
            }
        }
        finally
        {
            _suppressLimiterEvents = false;
        }
    }

    private void TaskLimiter_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressLimiterEvents) return;
        bool isEnabled = TaskLimiterCheckBox.IsChecked == true;
        TaskLimitValueTextBox.IsEnabled = isEnabled;
        TaskLimitUnitComboBox.IsEnabled = isEnabled;
        ApplySpeedLimit();
    }

    private void TaskLimiter_InputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressLimiterEvents) return;
        ApplySpeedLimit();
    }

    private void TaskLimiter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressLimiterEvents) return;
        ApplySpeedLimit();
    }

    private void ApplySpeedLimit()
    {
        if (_item == null) return;

        if (TaskLimiterCheckBox?.IsChecked == true)
        {
            if (double.TryParse(TaskLimitValueTextBox?.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) && val > 0)
            {
                bool isMb = TaskLimitUnitComboBox?.SelectedIndex == 1;
                long multiplier = isMb ? (1024 * 1024) : 1024;
                long bps = (long)(val * multiplier);
                _item.SpeedLimitBps = bps;
                _engine?.SetTaskSpeedLimit(_item.Id, bps);
                _database?.UpdateTaskSpeedLimit(_item.Id, bps);
            }
        }
        else
        {
            _item.SpeedLimitBps = null;
            _engine?.SetTaskSpeedLimit(_item.Id, 0);
            _database?.UpdateTaskSpeedLimit(_item.Id, null);
        }
    }

    private async void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_item.FilePath))
        {
            try
            {
                Clipboard.SetText(_item.FilePath);
                if (CopyIcon != null)
                {
                    CopyIcon.Kind = "check";
                    CopyIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentEmeraldBrush");
                    await System.Threading.Tasks.Task.Delay(1500);
                    CopyIcon.Kind = "copy";
                    CopyIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                }
            }
            catch
            {
                // Clipboard fallback
            }
        }
    }

    private void RefreshLink_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RefreshLinkDialog(_item)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.ValidatedNewUrl))
        {
            _item.Task.Url = dlg.ValidatedNewUrl;
            if (!string.IsNullOrEmpty(dlg.ValidatedReferer))
            {
                _item.Task.Referer = dlg.ValidatedReferer;
            }

            _database?.UpsertTask(_item.Task);
            _engine?.StartDownload(_item.Task);
        }
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        _engine?.StartDownload(_item.Task);
    }

    private async void ReportIssue_Click(object sender, RoutedEventArgs e)
    {
        if (ReportIssueBtn == null || ReportIssueText == null) return;

        ReportIssueBtn.IsEnabled = false;
        ReportIssueText.Text = I18n.Tr("transfer.reportSending", "Mengirim...");

        try
        {
            CrashReporter.SendCrashReport(new System.Exception($"Download failed: {_item.ErrorMessage ?? "Unknown"}"), "TransferWindow.UserReport");
            await System.Threading.Tasks.Task.Delay(500);
            ReportIssueText.Text = I18n.Tr("transfer.reportSent", "Terkirim ✓");
        }
        catch
        {
            ReportIssueText.Text = I18n.Tr("common.error", "Gagal kirim");
        }
        finally
        {
            await System.Threading.Tasks.Task.Delay(1500);
            ReportIssueText.Text = I18n.Tr("transfer.reportIssue", "Laporkan Masalah");
            ReportIssueBtn.IsEnabled = true;
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_item.FilePath))
        {
            Process.Start(new ProcessStartInfo(_item.FilePath) { UseShellExecute = true });
            Close();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = Path.GetDirectoryName(_item.FilePath) ?? _item.Task.SaveDir;
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_item.FilePath}\"") { UseShellExecute = true });
        }
    }
}