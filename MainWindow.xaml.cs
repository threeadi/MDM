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
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private TrayIconService? _trayService;
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        StateChanged += (_, _) =>
        {
            if (MaximizeIcon != null)
            {
                MaximizeIcon.Kind = WindowState == WindowState.Maximized ? "copy" : "square";
            }
        };

        Loaded += (_, _) =>
        {
            // Enable native Windows 11 / 10 Dark Mode titlebar & DWM hardware composition
            var handle = new WindowInteropHelper(this).Handle;
            int useImmersiveDarkMode = 1;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

            // Enable native Windows 11 rounded corners matching OS DWM
            int cornerPreference = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // Initialize System Tray
            _trayService = new TrayIconService(this);
            _trayService.OnOpenRequested += () =>
            {
                Logger.Debug("MainWindow dipulihkan dari System Tray", "MAINWINDOW");
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
                Logger.Info("Aplikasi keluar melalui System Tray menu", "MAINWINDOW");
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
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Show();
                Activate();
                Topmost = true;
                Topmost = false;
                Focus();

                var dlg = new AddDownloadDialog(req.Url, req.Filename, req.Referer, req.Quality)
                {
                    Owner = this.IsVisible ? this : null
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
                        dlg.StartImmediately,
                        dlg.Quality,
                        req.RayId,
                        req.IsDrmProtected,
                        req.IsDecoyCanvas,
                        req.IsHoneypotDetected
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
            var vm = ViewModel;
            _ = Task.Run(async () =>
            {
                try
                {
                    await vm.Engine.TelegramService.CheckAndRestoreAuthAsync();
                    Dispatcher.Invoke(() => vm.UpdateTelegramCounts());
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Gagal restore Telegram session di MainWindow: {ex.Message}", "MAINWINDOW");
                }
            });
        };

        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && ViewModel.Database.GetSetting("minimize_to_tray", "1") == "1")
        {
            e.Cancel = true;
            Logger.Debug("MainWindow diminimalkan ke System Tray", "MAINWINDOW");
            Hide();
        }
        else
        {
            _trayService?.Dispose();
        }
    }

    private void TitleBar_Minimize_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void TitleBar_Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void TitleBar_Close_Click(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
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
                dlg.StartImmediately,
                dlg.Quality
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

    private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scv)
        {
            scv.ScrollToVerticalOffset(scv.VerticalOffset - (e.Delta / 3.0));
            e.Handled = true;
        }
    }

    private void TelegramMediaScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Infinite scroll: auto-paginate when user scrolls down near the bottom
        double scrollableHeight = e.ExtentHeight - e.ViewportHeight;
        if (e.VerticalChange > 0 && scrollableHeight > 0 && e.VerticalOffset >= scrollableHeight - 80)
        {
            if (DataContext is MainViewModel vm && !vm.TelegramIsScanning && !vm.IsAutoFetchingAllTelegram && !vm.IsBatchQueueingTelegram && vm.HasTelegramMedia && vm.HasMoreTelegramMedia)
            {
                vm.ScanTelegramCommand.Execute("loadMore");
            }
        }
    }

    private void TelegramMediaListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Virtualized ListBox infinite scroll: auto-paginate when user scrolls near the bottom
        double scrollableHeight = e.ExtentHeight - e.ViewportHeight;
        if (e.VerticalChange > 0 && scrollableHeight > 0 && e.VerticalOffset >= scrollableHeight - 80)
        {
            if (DataContext is MainViewModel vm && !vm.TelegramIsScanning && !vm.IsAutoFetchingAllTelegram && !vm.IsBatchQueueingTelegram && vm.HasTelegramMedia && vm.HasMoreTelegramMedia)
            {
                vm.ScanTelegramCommand.Execute("loadMore");
            }
        }
    }
}
