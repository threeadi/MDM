using System.Windows;
using System.Windows.Threading;
using Mdm.Services;

namespace Mdm;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        CrashReporter.Initialize();
        var db = new Database();
        I18n.Instance.Initialize(db);
        Logger.Info($"MDM Modern Download Manager (.NET 10 Native) dimulai. Bahasa: {I18n.Instance.CurrentLanguage}");

        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error("Unhandled Dispatcher Exception", e.Exception);
        CrashReporter.SendCrashReport(e.Exception, "DispatcherUnhandledException");
        MessageBox.Show($"Terjadi kesalahan aplikasi:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "MDM Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
