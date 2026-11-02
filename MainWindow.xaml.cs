using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Mdm.Models;
using Mdm.Services;
using Mdm.ViewModels;
using Mdm.Views;

namespace Mdm;

public partial class MainWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private TrayIconService? _trayService;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // Enable native Windows 11 / 10 Dark Mode titlebar
            var handle = new WindowInteropHelper(this).Handle;
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

            // Initialize System Tray
            _trayService = new TrayIconService(this);
            _trayService.OnOpenRequested += () =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Focus();
            };
            _trayService.OnAddDownloadRequested += () => AddDownload_Click(this, new RoutedEventArgs());
            _trayService.OnPauseAllRequested += () => ViewModel.PauseAllCommand.Execute(null);
            _trayService.OnResumeAllRequested += () => ViewModel.ResumeAllCommand.Execute(null);
            _trayService.OnExitRequested += () =>
            {
                _isExplicitExit = true;
                Close();
            };
            _trayService.Initialize();

            // Task completion notification
            ViewModel.OnTaskFinished += (task) =>
            {
                _trayService.ShowNotification("Unduhan Selesai", $"Berkas '{task.Filename}' telah selesai diunduh.");
            };

            // Browser extension download requested listener
            ViewModel.OnBrowserDownloadRequested = (req) =>
            {
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();

                var dlg = new AddDownloadDialog(req.Url, req.Filename, req.Referer)
                {
                    Owner = this
                };

                if (dlg.ShowDialog() == true)
                {
                    string finalFn = dlg.CustomFilename;
                    if (!HandleDuplicateCheck(ref finalFn, dlg.SaveDirectory))
                    {
                        return;
                    }

                    _ = ViewModel.StartNewDownloadAsync(
                        dlg.DownloadUrl,
                        dlg.SaveDirectory,
                        finalFn,
                        dlg.ConnectionThreads,
                        dlg.Referer,
                        dlg.SelectedCategory,
                        dlg.StartImmediately
                    );
                }
            };

            // Delete confirmation dialog listener (1:1 with my-own-idm)
            ViewModel.RequestDeleteConfirmation = (targets) =>
            {
                var dlg = new DeleteConfirmDialog(targets)
                {
                    Owner = this
                };
                if (dlg.ShowDialog() == true)
                {
                    ViewModel.DeleteTasks(targets, dlg.DeleteFilesFromDisk);
                }
            };

            // Restore Telegram auth session if session file exists
            _ = Task.Run(async () =>
            {
                try
                {
                    await ViewModel.Engine.TelegramService.CheckAndRestoreAuthAsync();
                    Dispatcher.Invoke(() => ViewModel.UpdateTelegramCounts());
                }
                catch { }
            });
        };

        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && ViewModel.Database.GetSetting("minimize_to_tray", "1") == "1")
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            _trayService?.Dispose();
        }
    }

    private void AddDownload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddDownloadDialog
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            string finalFn = dlg.CustomFilename;
            if (!HandleDuplicateCheck(ref finalFn, dlg.SaveDirectory))
            {
                return;
            }

            _ = ViewModel.StartNewDownloadAsync(
                dlg.DownloadUrl,
                dlg.SaveDirectory,
                finalFn,
                dlg.ConnectionThreads,
                dlg.Referer,
                dlg.SelectedCategory,
                dlg.StartImmediately
            );
        }
    }

    private async void TelegramGrabber_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.TelegramAuthStatus.IsAuthenticated)
        {
            var authDlg = new TelegramAuthDialog(ViewModel.Engine.TelegramService, ViewModel.Database)
            {
                Owner = this
            };
            if (authDlg.ShowDialog() == true || authDlg.IsSuccess)
            {
                await ViewModel.FetchTelegramDialogsAsync();
                ViewModel.UpdateTelegramCounts();
            }
        }
        else
        {
            var dlg = new SettingsDialog(ViewModel.Database, ViewModel.Engine.TelegramService, initialTab: "telegram")
            {
                Owner = this
            };
            dlg.ShowDialog();
            await ViewModel.FetchTelegramDialogsAsync();
            ViewModel.UpdateTelegramCounts();
        }
    }

    private bool HandleDuplicateCheck(ref string filename, string saveDir)
    {
        if (string.IsNullOrWhiteSpace(filename)) return true;

        string targetPath = Path.Combine(saveDir, filename);
        if (File.Exists(targetPath))
        {
            var dupDlg = new DuplicateDownloadDialog(targetPath)
            {
                Owner = this
            };

            if (dupDlg.ShowDialog() == true)
            {
                if (dupDlg.Action == DuplicateAction.AutoRename)
                {
                    filename = dupDlg.NewFilename;
                    return true;
                }
                if (dupDlg.Action == DuplicateAction.Overwrite)
                {
                    return true;
                }
            }
            return false;
        }

        return true;
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(ViewModel.Database, ViewModel.Engine.TelegramService)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true)
        {
            // Update global rate limit if configured
            if (ViewModel.Database.GetSetting("global_limit_enabled") == "1" &&
                long.TryParse(ViewModel.Database.GetSetting("global_limit_kbps"), out long kbps))
            {
                ViewModel.Engine.GlobalLimiter.SetRate(kbps * 1024);
            }
            else
            {
                ViewModel.Engine.GlobalLimiter.SetRate(0); // Unlimited
            }
        }
    }

    private void ContextMenu_RefreshLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task) return;

        var dlg = new RefreshLinkDialog(task)
        {
            Owner = this
        };

        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.ValidatedNewUrl))
        {
            task.Task.Url = dlg.ValidatedNewUrl;
            if (!string.IsNullOrEmpty(dlg.ValidatedReferer))
            {
                task.Task.Referer = dlg.ValidatedReferer;
            }

            ViewModel.Database.UpsertTask(task.Task);
            ViewModel.Engine.StartDownload(task.Task);
        }
    }

    private void ContextMenu_Properties_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task) return;

        var dlg = new PropertiesDialog(task)
        {
            Owner = this
        };
        dlg.ShowDialog();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedTask is not { } task) return;

        if (task.Status == DownloadStatus.Completed && File.Exists(task.FilePath))
        {
            Process.Start(new ProcessStartInfo(task.FilePath) { UseShellExecute = true });
        }
        else
        {
            var transferWin = new TransferWindow(task, ViewModel.Engine, ViewModel.Database)
            {
                Owner = this
            };
            transferWin.ShowDialog();
        }
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dg)
        {
            ViewModel.SelectedTasks = dg.SelectedItems.OfType<DownloadItemViewModel>().ToList();
        }
    }

    private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            if (ViewModel.DeleteCommand.CanExecute(null))
            {
                ViewModel.DeleteCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (sender is DataGrid dg)
            {
                dg.SelectAll();
                e.Handled = true;
            }
        }
    }

    private void ContextMenu_SelectAll_Click(object sender, RoutedEventArgs e)
    {
        DownloadsDataGrid?.SelectAll();
    }
}
