using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mdm.Models;
using Mdm.Services;
using Mdm.ViewModels;

namespace Mdm.Views;

public partial class RefreshLinkDialog : Window
{
    private readonly DownloadItemViewModel _itemViewModel;
    private CancellationTokenSource? _probeCts;

    public string? ValidatedNewUrl { get; private set; }
    public string? ValidatedReferer { get; private set; }

    public RefreshLinkDialog(DownloadItemViewModel itemViewModel)
    {
        InitializeComponent();
        _itemViewModel = itemViewModel;
        DataContext = _itemViewModel;

        Loaded += (_, _) =>
        {
            NewUrlTextBox.Focus();
        };
    }

    public void SetCapturedUrl(string newUrl, string? referer = null)
    {
        Dispatcher.Invoke(() =>
        {
            NewUrlTextBox.Text = newUrl;
            if (!string.IsNullOrEmpty(referer))
            {
                ValidatedReferer = referer;
            }
        });
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        string target = !string.IsNullOrEmpty(_itemViewModel.Task.Referer) 
            ? _itemViewModel.Task.Referer 
            : _itemViewModel.Task.Url;

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Gagal membuka browser: {ex.Message}", "Kesalahan", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void NewUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string url = NewUrlTextBox.Text.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ProbeBadge.Visibility = Visibility.Collapsed;
            ApplyButton.IsEnabled = false;
            return;
        }

        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var token = _probeCts.Token;

        ProbeBadge.Visibility = Visibility.Visible;
        ProbeStatusText.Text = "Memverifikasi tautan baru & kecocokan ukuran...";
        ProbeStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
        ApplyButton.IsEnabled = false;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                var probe = await DownloadEngine.ProbeUrlAsync(url, ValidatedReferer ?? _itemViewModel.Task.Referer, token);

                Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;

                    // Verify range support
                    if (!probe.SupportsRange && _itemViewModel.Task.SupportsRange)
                    {
                        ProbeStatusText.Text = "Peringatan: Server tautan baru tidak mendukung Range resume (Single stream).";
                        ProbeStatusText.Foreground = (Brush)FindResource("AccentAmberBrush");
                        ApplyButton.IsEnabled = true;
                        ValidatedNewUrl = url;
                        return;
                    }

                    // Verify size match if known
                    if (_itemViewModel.Task.TotalBytes.HasValue && probe.TotalBytes.HasValue)
                    {
                        if (_itemViewModel.Task.TotalBytes.Value != probe.TotalBytes.Value)
                        {
                            ProbeStatusText.Text = $"Perhatian: Ukuran berkas berbeda! Lama: {DownloadItemViewModel.FormatBytes(_itemViewModel.Task.TotalBytes.Value)}, Baru: {DownloadItemViewModel.FormatBytes(probe.TotalBytes.Value)}";
                            ProbeStatusText.Foreground = (Brush)FindResource("AccentRoseBrush");
                            ApplyButton.IsEnabled = true;
                            ValidatedNewUrl = url;
                            return;
                        }
                    }

                    ProbeStatusText.Text = $"Tautan valid! Ukuran: {(probe.TotalBytes.HasValue ? DownloadItemViewModel.FormatBytes(probe.TotalBytes.Value) : "Sesuai")}. Multi-thread Range didukung.";
                    ProbeStatusText.Foreground = (Brush)FindResource("AccentEmeraldBrush");
                    ApplyButton.IsEnabled = true;
                    ValidatedNewUrl = url;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    ProbeStatusText.Text = $"Gagal memverifikasi: {ex.Message}";
                    ProbeStatusText.Foreground = (Brush)FindResource("AccentRoseBrush");
                    ApplyButton.IsEnabled = false;
                });
            }
        }, token);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValidatedNewUrl))
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _probeCts?.Cancel();
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _probeCts?.Cancel();
        base.OnClosed(e);
    }
}

