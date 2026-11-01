using System.Windows;
using System.Windows.Input;
using Mdm.Models;
using Mdm.Services;
using Mdm.ViewModels;

namespace Mdm.Views;

public partial class DeleteConfirmDialog : Window
{
    private readonly IReadOnlyList<DownloadItemViewModel> _items;

    public bool DeleteFilesFromDisk => DeleteFromDiskCheckBox.IsChecked == true;

    public DeleteConfirmDialog(DownloadItemViewModel singleItem)
        : this([singleItem])
    {
    }

    public DeleteConfirmDialog(IReadOnlyList<DownloadItemViewModel> items)
    {
        InitializeComponent();
        _items = items;
        PopulateUi();
    }

    private void PopulateUi()
    {
        if (_items.Count == 0) return;

        if (_items.Count == 1)
        {
            var item = _items[0];
            PromptTitleText.Text = I18n.Tr("deleteConfirm.descCompleted", "Hapus berkas ini dari antrean unduhan?");
            PromptSubtitleText.Text = I18n.Tr("deleteConfirm.promptSubtitle", "Tugas unduhan ini akan dihapus dari daftar MDM Turbo Desktop.");
            FilenameText.Text = item.Filename;
            SizeSummaryText.Text = $"{item.DownloadedFormatted} / {item.SizeFormatted}";
            StatusBadgeText.Text = item.StatusText;
            StatusBadgeText.Foreground = item.StatusBrush;
            StatusBadgeBorder.Background = item.StatusBgBrush;
            StatusBadgeBorder.BorderBrush = item.StatusBrush;
            FilePathText.Text = item.FilePath;
            CategoryIcon.Kind = item.LucideCategoryIcon;
            CategoryIcon.Foreground = item.LucideCategoryColor;

            ActiveDownloadWarning.Visibility = item.Status == DownloadStatus.Downloading
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Default to checked if file is finished downloading (1:1 with my-own-idm)
            DeleteFromDiskCheckBox.IsChecked = item.Status == DownloadStatus.Completed;
        }
        else
        {
            PromptTitleText.Text = string.Format(I18n.Tr("deleteConfirm.promptMultipleTitle", "Hapus {0} berkas ini dari antrean unduhan?"), _items.Count);
            PromptSubtitleText.Text = I18n.Tr("deleteConfirm.promptMultipleSubtitle", "Seluruh tugas unduhan terpilih akan dihapus dari daftar MDM Turbo Desktop.");

            var names = _items.Take(3).Select(i => i.Filename);
            FilenameText.Text = string.Join(", ", names) + (_items.Count > 3 ? $" (+{_items.Count - 3} lainnya)" : "");

            long totalBytes = _items.Sum(i => i.TotalBytes);
            SizeSummaryText.Text = $"Total: {DownloadItemViewModel.FormatBytes(totalBytes)} ({_items.Count} berkas)";

            StatusBadgeText.Text = string.Format(I18n.Tr("deleteConfirm.selectedBadge", "{0} Terpilih"), _items.Count);
            StatusBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("AccentCyanBrush");
            StatusBadgeBorder.Background = (System.Windows.Media.Brush)FindResource("SurfaceMediumBrush");
            StatusBadgeBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");

            PathContainer.Visibility = Visibility.Collapsed;
            CategoryIcon.Kind = "files";

            bool anyActive = _items.Any(i => i.Status == DownloadStatus.Downloading);
            ActiveDownloadWarning.Visibility = anyActive ? Visibility.Visible : Visibility.Collapsed;

            DeleteFromDiskCheckBox.IsChecked = _items.All(i => i.Status == DownloadStatus.Completed);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            DialogResult = true;
            Close();
        }
    }
}

