using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Mdm.Views;

public enum DuplicateAction
{
    Cancel,
    AutoRename,
    Overwrite,
    OpenExisting
}

public partial class DuplicateDownloadDialog : Window
{
    public DuplicateAction Action { get; private set; } = DuplicateAction.Cancel;
    public string NewFilename { get; private set; } = string.Empty;

    public DuplicateDownloadDialog(string fullPath)
    {
        InitializeComponent();

        FilePathText.Text = fullPath;
        string dir = Path.GetDirectoryName(fullPath) ?? "";
        string fnWithoutExt = Path.GetFileNameWithoutExtension(fullPath);
        string ext = Path.GetExtension(fullPath);

        // Calculate unique auto-rename candidate
        int counter = 1;
        string candidate;
        do
        {
            candidate = $"{fnWithoutExt} ({counter}){ext}";
            counter++;
        } while (File.Exists(Path.Combine(dir, candidate)));

        NewFilename = candidate;
    }

    private void AutoRename_Click(object sender, RoutedEventArgs e)
    {
        Action = DuplicateAction.AutoRename;
        DialogResult = true;
        Close();
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e)
    {
        Action = DuplicateAction.Overwrite;
        DialogResult = true;
        Close();
    }

    private void OpenExisting_Click(object sender, RoutedEventArgs e)
    {
        Action = DuplicateAction.OpenExisting;
        if (File.Exists(FilePathText.Text))
        {
            Process.Start(new ProcessStartInfo(FilePathText.Text) { UseShellExecute = true });
        }
        DialogResult = false;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Action = DuplicateAction.Cancel;
        DialogResult = false;
        Close();
    }
}

