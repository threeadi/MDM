using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mdm.Services;
using Mdm.ViewModels;
using Microsoft.Win32;

namespace Mdm.Views;

public partial class AddDownloadDialog : Window
{
    private CancellationTokenSource? _probeCts;

    public string DownloadUrl => UrlTextBox.Text.Trim();
    public string SaveDirectory => SaveDirTextBox.Text.Trim();
    public string CustomFilename => FilenameTextBox.Text.Trim();
    public int ConnectionThreads => int.TryParse(ThreadsTextBox.Text.Trim(), out int t) ? Math.Clamp(t, 1, 32) : 16;
    public string SelectedCategory => (CategoryComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Umum";
    public bool StartImmediately => StartNowCheckBox.IsChecked == true;
    public string? Referer { get; set; }
    public string? Quality { get; set; }

    public AddDownloadDialog(string? initialUrl = null, string? initialFilename = null, string? referer = null, string? quality = null)
    {
        InitializeComponent();

        string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(defaultDir))
        {
            defaultDir = "C:\\Downloads";
        }

        SaveDirTextBox.Text = defaultDir;
        Referer = referer;
        Quality = quality;

        if (!string.IsNullOrEmpty(initialUrl))
        {
            UrlTextBox.Text = initialUrl;
        }

        if (!string.IsNullOrEmpty(initialFilename))
        {
            FilenameTextBox.Text = initialFilename;
            SelectCategoryByExtension(initialFilename);
        }

        Loaded += (_, _) =>
        {
            if (string.IsNullOrEmpty(UrlTextBox.Text))
                UrlTextBox.Focus();
            else
                FilenameTextBox.Focus();
        };
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pilih Folder Penyimpanan",
            InitialDirectory = SaveDirTextBox.Text
        };

        if (dlg.ShowDialog() == true)
        {
            SaveDirTextBox.Text = dlg.FolderName;
        }
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string url = DownloadUrl;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            ProbeBadge.Visibility = Visibility.Collapsed;
            return;
        }

        _probeCts?.Cancel();
        _probeCts = new CancellationTokenSource();
        var token = _probeCts.Token;

        ProbeBadge.Visibility = Visibility.Visible;
        ProbeStatusText.Text = "Memeriksa informasi berkas...";
        ProbeStatusText.Foreground = (Brush)FindResource("TextMutedBrush");

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                var probe = await DownloadEngine.ProbeUrlAsync(url, Referer, token);

                Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;

                    if (string.IsNullOrEmpty(FilenameTextBox.Text) && !string.IsNullOrEmpty(probe.Filename))
                    {
                        FilenameTextBox.Text = probe.Filename;
                        SelectCategoryByExtension(probe.Filename);
                    }

                    string sizeStr = probe.TotalBytes.HasValue
                        ? DownloadItemViewModel.FormatBytes(probe.TotalBytes.Value)
                        : "Ukuran tidak diketahui";

                    string rangeStr = probe.SupportsRange ? "Multi-Thread (206 OK)" : "Single Connection";

                    ProbeStatusText.Text = $"Ukuran: {sizeStr}  |  Resume: {rangeStr}  |  Kategori: {probe.Category}";
                    ProbeStatusText.Foreground = (Brush)FindResource("AccentCyanBrush");
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (token.IsCancellationRequested) return;
                    ProbeStatusText.Text = $"Gagal probe: {ex.Message}";
                    ProbeStatusText.Foreground = (Brush)FindResource("AccentRoseBrush");
                });
            }
        }, token);
    }

    private void SelectCategoryByExtension(string filename)
    {
        string category = CategoryHelper.DetectCategory(filename);
        foreach (ComboBoxItem item in CategoryComboBox.Items)
        {
            if (item.Content?.ToString() == category)
            {
                CategoryComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            MessageBox.Show(this, "Silakan masukkan alamat URL unduhan yang valid.", "URL Kosong", MessageBoxButton.OK, MessageBoxImage.Warning);
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
