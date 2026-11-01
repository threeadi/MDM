using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mdm.ViewModels;

namespace Mdm.Views;

public partial class PropertiesDialog : Window
{
    private readonly DownloadItemViewModel _itemViewModel;

    public PropertiesDialog(DownloadItemViewModel itemViewModel)
    {
        InitializeComponent();
        _itemViewModel = itemViewModel;
        DataContext = _itemViewModel;
    }

    private async void CalculateAll_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_itemViewModel.FilePath))
        {
            MessageBox.Show(this, "Berkas fisik belum ada atau telah dipindahkan.", "Berkas Tidak Ditemukan", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Md5TextBox.Text = "Menghitung...";
        Sha1TextBox.Text = "Menghitung...";
        Sha256TextBox.Text = "Menghitung...";

        string filePath = _itemViewModel.FilePath;

        await Task.Run(() =>
        {
            try
            {
                using var streamMd5 = File.OpenRead(filePath);
                using var md5 = MD5.Create();
                string md5Hash = Convert.ToHexStringLower(md5.ComputeHash(streamMd5));

                using var streamSha1 = File.OpenRead(filePath);
                using var sha1 = SHA1.Create();
                string sha1Hash = Convert.ToHexStringLower(sha1.ComputeHash(streamSha1));

                using var streamSha256 = File.OpenRead(filePath);
                using var sha256 = SHA256.Create();
                string sha256Hash = Convert.ToHexStringLower(sha256.ComputeHash(streamSha256));

                Dispatcher.Invoke(() =>
                {
                    Md5TextBox.Text = md5Hash;
                    Sha1TextBox.Text = sha1Hash;
                    Sha256TextBox.Text = sha256Hash;
                    UpdateComparison();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    Md5TextBox.Text = $"Gagal: {ex.Message}";
                    Sha1TextBox.Text = $"Gagal: {ex.Message}";
                    Sha256TextBox.Text = $"Gagal: {ex.Message}";
                });
            }
        });
    }

    private void CopyMd5_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Md5TextBox.Text))
        {
            Clipboard.SetText(Md5TextBox.Text);
        }
    }

    private void CopySha1_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Sha1TextBox.Text))
        {
            Clipboard.SetText(Sha1TextBox.Text);
        }
    }

    private void CopySha256_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(Sha256TextBox.Text))
        {
            Clipboard.SetText(Sha256TextBox.Text);
        }
    }

    private void CompareTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateComparison();
    }

    private void UpdateComparison()
    {
        string target = CompareTextBox.Text.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(target))
        {
            CompareResultText.Text = "";
            return;
        }

        string md5 = Md5TextBox.Text.Trim().ToLowerInvariant();
        string sha1 = Sha1TextBox.Text.Trim().ToLowerInvariant();
        string sha256 = Sha256TextBox.Text.Trim().ToLowerInvariant();

        if (target == md5 || target == sha1 || target == sha256)
        {
            CompareResultText.Text = "COCOK (MATCH)";
            CompareResultText.Foreground = (Brush)FindResource("AccentEmeraldBrush");
        }
        else
        {
            CompareResultText.Text = "TIDAK COCOK (MISMATCH)";
            CompareResultText.Foreground = (Brush)FindResource("AccentRoseBrush");
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_itemViewModel.FilePath))
        {
            Process.Start(new ProcessStartInfo(_itemViewModel.FilePath) { UseShellExecute = true });
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string dir = Path.GetDirectoryName(_itemViewModel.FilePath) ?? _itemViewModel.Task.SaveDir;
        if (Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_itemViewModel.FilePath}\"") { UseShellExecute = true });
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

