using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mdm.Services;
using Microsoft.Win32;

namespace Mdm.Views;

public partial class SettingsDialog : Window
{
    private readonly Database _database;
    private readonly TelegramService? _telegramService;
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MDM_ModernDownloadManager";
    private const string HostName = "com.myownidm.host";

    private const string DefaultAutoCaptureExt = "3GP 7Z AAC ACE AIF APK ARJ ASF AVI BIN BZ2 EXE GZ GZIP IMG ISO LZH M4A M4V MKV MOV MP3 MP4 MPA MPE MPEG MPG MSI MSU OGG OGV PDF PLJ PPS PPT PPTX QT R0* R1* RA RAR RM RMVB SEA SIT SITX TAR TIF TIFF TS WAV WMA WMV Z ZIP";
    private const string DefaultExcludedSites = "*.bank.co.id intranet.local";

    private bool _showPassword = false;
    private DispatcherTimer? _bannerTimer;

    public SettingsDialog(Database database, TelegramService? telegramService = null, string? initialTab = null)
    {
        InitializeComponent();
        _database = database;
        _telegramService = telegramService;

        LoadSettings();

        if (string.Equals(initialTab, "telegram", StringComparison.OrdinalIgnoreCase))
        {
            TabRadioTelegram.IsChecked = true;
            bool isAuthed = _telegramService?.AuthStatus.IsAuthenticated == true;
            if (!isAuthed)
            {
                SubTabTgLogin.IsChecked = true;
                TgSubTab_Checked(this, new RoutedEventArgs());
            }
            else
            {
                SubTabTgAccounts.IsChecked = true;
                TgSubTab_Checked(this, new RoutedEventArgs());
            }
        }
    }

    #region Window Drag & Navigation Controls

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
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
        DialogResult = false;
        Close();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelGeneral == null) return;

        PanelGeneral.Visibility = TabRadioGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelConnection.Visibility = TabRadioConnection.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelFileTypes.Visibility = TabRadioFileTypes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelSaveTo.Visibility = TabRadioSaveTo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelExtensions.Visibility = TabRadioExtensions.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTelegram.Visibility = TabRadioTelegram.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelProxy.Visibility = TabRadioProxy.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TgSubTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PanelTgAccountsList == null || PanelTgLoginOtp == null) return;

        bool isAccounts = SubTabTgAccounts.IsChecked == true;
        PanelTgAccountsList.Visibility = isAccounts ? Visibility.Visible : Visibility.Collapsed;
        PanelTgLoginOtp.Visibility = isAccounts ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    #region Loading & Binding Settings

    private void LoadSettings()
    {
        // --- 1. UMUM (GENERAL) ---
        string lang = _database.GetSetting("language", "id") ?? "id";
        LanguageComboBox.SelectedIndex = lang == "en" ? 1 : 0;

        // Startup Registry Check
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            AutoStartCheckBox.IsChecked = key?.GetValue(AppName) != null;
        }
        catch
        {
            AutoStartCheckBox.IsChecked = false;
        }

        MinimizeToTrayCheckBox.IsChecked = _database.GetSetting("minimize_to_tray", "1") == "1";
        MediaPanelCheckBox.IsChecked = _database.GetSetting("media_panel_overlay", "1") == "1";
        ClipboardCaptureCheckBox.IsChecked = _database.GetSetting("clipboard_auto_capture", "1") == "1";
        NotifyOnCompleteCheckBox.IsChecked = _database.GetSetting("notify_on_complete", "1") == "1";
        ShowCompletionDialogCheckBox.IsChecked = _database.GetSetting("show_completion_dialog", "1") == "1";

        // --- 2. SAMBUNGAN (CONNECTION) ---
        bool limitEnabled = _database.GetSetting("global_limit_enabled", "0") == "1";
        EnableLimitCheckBox.IsChecked = limitEnabled;
        SpeedLimitGrid.IsEnabled = limitEnabled;
        GlobalLimitValueTextBox.Text = _database.GetSetting("global_limit_value", "2048") ?? "2048";
        string limitUnit = _database.GetSetting("global_limit_unit", "KB/s") ?? "KB/s";
        GlobalLimitUnitComboBox.SelectedIndex = limitUnit == "MB/s" ? 1 : 0;

        ConnectionTimeoutTextBox.Text = _database.GetSetting("connection_timeout_sec", "30") ?? "30";
        MaxRetriesTextBox.Text = _database.GetSetting("max_retries", "10") ?? "10";

        // --- 3. JENIS BERKAS (FILE TYPES) ---
        AutoCaptureExtensionsTextBox.Text = _database.GetSetting("auto_capture_extensions", DefaultAutoCaptureExt) ?? DefaultAutoCaptureExt;
        ExcludedSitesTextBox.Text = _database.GetSetting("excluded_sites", DefaultExcludedSites) ?? DefaultExcludedSites;

        // --- 4. LOKASI SIMPAN (SAVE TO) ---
        string defaultDir = _database.GetSetting("default_dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        DefaultDownloadDirTextBox.Text = defaultDir;
        CategorySubfoldersCheckBox.IsChecked = _database.GetSetting("subfolder_by_category", "1") == "1";

        string defaultTemp = Path.Combine(Path.GetTempPath(), "MDM_Chunks");
        TempDirTextBox.Text = _database.GetSetting("temp_dir", defaultTemp) ?? defaultTemp;

        string dupAction = _database.GetSetting("duplicate_action", "ask") ?? "ask";
        DuplicateActionComboBox.SelectedIndex = dupAction switch
        {
            "numbered" => 1,
            "overwrite" => 2,
            "resume" => 3,
            _ => 0
        };

        // --- 5. EKSTENSI (EXTENSIONS) ---
        BrowserChromeCheckBox.IsChecked = _database.GetSetting("browser_chrome", "1") == "1";
        BrowserEdgeCheckBox.IsChecked = _database.GetSetting("browser_edge", "1") == "1";
        BrowserFirefoxCheckBox.IsChecked = _database.GetSetting("browser_firefox", "1") == "1";
        BrowserBraveCheckBox.IsChecked = _database.GetSetting("browser_brave", "1") == "1";

        // --- 6. TELEGRAM ---
        string defaultTgDir = Path.Combine(defaultDir, "Telegram");
        TelegramDirTextBox.Text = _database.GetSetting("telegram_download_dir", defaultTgDir) ?? defaultTgDir;
        TelegramSubfolderCheckBox.IsChecked = _database.GetSetting("telegram_subfolder", "1") == "1";

        string savedApiId = _database.GetSetting("tg_api_id", "") ?? "";
        if (savedApiId == "2040") savedApiId = "";
        TgApiIdTextBox.Text = savedApiId;

        string savedApiHash = _database.GetSetting("tg_api_hash", "") ?? "";
        if (savedApiHash == "b18441a1ff607e10a989891a5462e627") savedApiHash = "";
        TgApiHashTextBox.Text = savedApiHash;

        ResetAndClearTelegramLoginForm();
        UpdateTelegramSessionStatus();

        // --- 7. PROXY & SOCKS ---
        bool proxyEnabled = _database.GetSetting("proxy_enabled", "0") == "1";
        ProxyEnabledCheckBox.IsChecked = proxyEnabled;
        ProxyConfigFields.Opacity = proxyEnabled ? 1.0 : 0.5;
        ProxyConfigFields.IsEnabled = proxyEnabled;

        string proxyType = _database.GetSetting("proxy_type", "http") ?? "http";
        if (proxyType == "https") ProxyTypeHttpsRadio.IsChecked = true;
        else if (proxyType == "socks5") ProxyTypeSocks5Radio.IsChecked = true;
        else ProxyTypeHttpRadio.IsChecked = true;

        ProxyHostTextBox.Text = _database.GetSetting("proxy_host", "127.0.0.1") ?? "127.0.0.1";
        ProxyPortTextBox.Text = _database.GetSetting("proxy_port", "1080") ?? "1080";

        bool proxyAuth = _database.GetSetting("proxy_auth", "0") == "1";
        ProxyAuthCheckBox.IsChecked = proxyAuth;
        ProxyAuthFields.Visibility = proxyAuth ? Visibility.Visible : Visibility.Collapsed;

        ProxyUserTextBox.Text = _database.GetSetting("proxy_user", "") ?? "";
        string pass = _database.GetSetting("proxy_pass", "") ?? "";
        ProxyPasswordBox.Password = pass;
        ProxyPasswordVisibleTextBox.Text = pass;
    }

    private void EnableLimit_Checked(object sender, RoutedEventArgs e)
    {
        if (SpeedLimitGrid != null) SpeedLimitGrid.IsEnabled = true;
    }

    private void EnableLimit_Unchecked(object sender, RoutedEventArgs e)
    {
        if (SpeedLimitGrid != null) SpeedLimitGrid.IsEnabled = false;
    }

    private void ProxyEnabled_Checked(object sender, RoutedEventArgs e)
    {
        if (ProxyConfigFields != null)
        {
            ProxyConfigFields.Opacity = 1.0;
            ProxyConfigFields.IsEnabled = true;
        }
    }

    private void ProxyEnabled_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ProxyConfigFields != null)
        {
            ProxyConfigFields.Opacity = 0.5;
            ProxyConfigFields.IsEnabled = false;
        }
    }

    private void ProxyAuth_Checked(object sender, RoutedEventArgs e)
    {
        if (ProxyAuthFields != null) ProxyAuthFields.Visibility = Visibility.Visible;
    }

    private void ProxyAuth_Unchecked(object sender, RoutedEventArgs e)
    {
        if (ProxyAuthFields != null) ProxyAuthFields.Visibility = Visibility.Collapsed;
    }

    private void ToggleProxyPassword_Click(object sender, RoutedEventArgs e)
    {
        _showPassword = !_showPassword;
        if (_showPassword)
        {
            ProxyPasswordVisibleTextBox.Text = ProxyPasswordBox.Password;
            ProxyPasswordVisibleTextBox.Visibility = Visibility.Visible;
            ProxyPasswordBox.Visibility = Visibility.Collapsed;
            EyeIcon.Kind = "eye-off";
        }
        else
        {
            ProxyPasswordBox.Password = ProxyPasswordVisibleTextBox.Text;
            ProxyPasswordBox.Visibility = Visibility.Visible;
            ProxyPasswordVisibleTextBox.Visibility = Visibility.Collapsed;
            EyeIcon.Kind = "eye";
        }
    }

    #endregion

    #region Directory Browsing

    private void BrowseDefaultDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pilih Direktori Unduhan Utama",
            InitialDirectory = Directory.Exists(DefaultDownloadDirTextBox.Text) ? DefaultDownloadDirTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dlg.ShowDialog() == true)
        {
            DefaultDownloadDirTextBox.Text = dlg.FolderName;
        }
    }

    private void BrowseTempDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pilih Direktori Sparse Chunks Sementara",
            InitialDirectory = Directory.Exists(TempDirTextBox.Text) ? TempDirTextBox.Text : Path.GetTempPath()
        };

        if (dlg.ShowDialog() == true)
        {
            TempDirTextBox.Text = dlg.FolderName;
        }
    }

    private void BrowseTelegramDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Pilih Folder Media Telegram",
            InitialDirectory = Directory.Exists(TelegramDirTextBox.Text) ? TelegramDirTextBox.Text : DefaultDownloadDirTextBox.Text
        };

        if (dlg.ShowDialog() == true)
        {
            TelegramDirTextBox.Text = dlg.FolderName;
        }
    }

    #endregion

    #region Native Host Manifest Registration

    private void RegisterNativeHost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                NativeHostFeedbackText.Text = "Lokasi executable MDM tidak ditemukan";
                NativeHostFeedbackText.Foreground = (Brush)FindResource("AccentRoseBrush");
                return;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string manifestDir = Path.Combine(appData, "MDM", "NativeMessagingHosts");
            Directory.CreateDirectory(manifestDir);

            string chromeManifestPath = Path.Combine(manifestDir, $"{HostName}.json");
            var chromeManifest = new
            {
                name = HostName,
                description = "MDM Turbo Native Messaging Host for Chromium/Edge",
                path = exePath,
                type = "stdio",
                allowed_origins = new[] { "chrome-extension://*/" }
            };
            File.WriteAllText(chromeManifestPath, JsonSerializer.Serialize(chromeManifest, new JsonSerializerOptions { WriteIndented = true }));

            // Register into Registry for Chrome, Edge, and Firefox
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Google\Chrome\NativeMessagingHosts\{HostName}"))
            {
                k.SetValue("", chromeManifestPath);
            }

            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}"))
            {
                k.SetValue("", chromeManifestPath);
            }

            NativeHostFeedbackText.Text = "Host manifest berhasil didaftarkan ke registri Windows!";
            NativeHostFeedbackText.Foreground = (Brush)FindResource("AccentEmeraldBrush");
        }
        catch (Exception ex)
        {
            NativeHostFeedbackText.Text = $"Gagal mendaftarkan host: {ex.Message}";
            NativeHostFeedbackText.Foreground = (Brush)FindResource("AccentRoseBrush");
        }
    }

    private void OpenExtensionFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string extDir = Path.Combine(baseDir, "extension");

            if (!Directory.Exists(extDir))
            {
                // Fallback to source root directory during dev/debugging
                string devDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "extension"));
                if (Directory.Exists(devDir))
                {
                    extDir = devDir;
                }
            }

            if (Directory.Exists(extDir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = extDir,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show($"Folder ekstensi tidak ditemukan di:\n{extDir}", "MDM Ekstensi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Gagal membuka folder ekstensi: {ex.Message}", "MDM Ekstensi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Telegram Authentication & Management

    private void ResetAndClearTelegramLoginForm()
    {
        TgPhoneTextBox.Text = "+62";
        TgOtpTextBox.Text = string.Empty;
        TgPasswordBox.Password = string.Empty;
        TgPasswordVisibleTextBox.Text = string.Empty;
        TgPasswordVisibleTextBox.Visibility = Visibility.Collapsed;
        TgPasswordBox.Visibility = Visibility.Visible;
        TgEyeIcon.Kind = "eye";

        TgOtpBlock.Visibility = Visibility.Collapsed;
        TgPasswordBlock.Visibility = Visibility.Collapsed;
        TgErrorBorder.Visibility = Visibility.Collapsed;
        TgErrorText.Text = string.Empty;
    }

    private void UpdateTelegramSessionStatus()
    {
        string sessionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MDM",
            "telegram",
            "mdm_telegram.session"
        );

        bool hasSession = File.Exists(sessionPath);
        if (hasSession)
        {
            TelegramStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x10, 0xB9, 0x81));
            TelegramStatusBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0xB9, 0x81));
            TelegramStatusBadgeText.Text = I18n.Instance["telegram.connected"];
            TelegramStatusBadgeText.Foreground = (Brush)FindResource("AccentEmeraldBrush");

            string userDisplay = "";
            if (_telegramService?.AuthStatus.IsAuthenticated == true && !string.IsNullOrEmpty(_telegramService.AuthStatus.DisplayName))
            {
                userDisplay = !string.IsNullOrEmpty(_telegramService.AuthStatus.Username) ? $"@{_telegramService.AuthStatus.Username}" : _telegramService.AuthStatus.PhoneNumber ?? "";
                TgAccountNameText.Text = $"{_telegramService.AuthStatus.DisplayName} ({userDisplay})".Trim();
            }
            else
            {
                TgAccountNameText.Text = I18n.Instance["settings.tgActiveAccount"];
            }
            ActiveAccountCard.Visibility = Visibility.Visible;
            TgNoAccountCard.Visibility = Visibility.Collapsed;

            // Show informative banner on login tab that an account is active
            TgActiveNoticeBorder.Visibility = Visibility.Visible;
            string accName = !string.IsNullOrEmpty(userDisplay) ? $"{_telegramService?.AuthStatus.DisplayName} ({userDisplay})" : TgAccountNameText.Text;
            TgAlreadyLoggedInText.Text = I18n.Instance.CurrentLanguage == "en"
                ? $"Account '{accName}' is active. Logging in with a new number will replace this active session."
                : $"Akun '{accName}' sedang aktif. Login dengan nomor baru akan menggantikan sesi akun ini.";
        }
        else
        {
            TelegramStatusBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B));
            TelegramStatusBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xF5, 0x9E, 0x0B));
            TelegramStatusBadgeText.Text = I18n.Instance["telegram.notLoggedIn"];
            TelegramStatusBadgeText.Foreground = (Brush)FindResource("AccentAmberBrush");
            ActiveAccountCard.Visibility = Visibility.Collapsed;
            TgNoAccountCard.Visibility = Visibility.Visible;
            TgActiveNoticeBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async void TgSendOtp_Click(object sender, RoutedEventArgs e)
    {
        string phone = TgPhoneTextBox.Text.Trim();
        if (string.IsNullOrEmpty(phone) || phone.Length < 7)
        {
            ShowTgError("Masukkan nomor telepon valid dengan kode negara.");
            return;
        }

        TgErrorBorder.Visibility = Visibility.Collapsed;
        var btn = sender as Button;
        string origText = (btn?.Content as TextBlock)?.Text ?? I18n.Instance["settings.tgSendOtp"];
        if (btn != null)
        {
            btn.IsEnabled = false;
            if (btn.Content is TextBlock tb) tb.Text = I18n.Instance["settings.tgSendingOtp"];
        }

        try
        {
            if (_telegramService != null)
            {
                string? customApiId = string.IsNullOrWhiteSpace(TgApiIdTextBox.Text) ? null : TgApiIdTextBox.Text.Trim();
                string? customApiHash = string.IsNullOrWhiteSpace(TgApiHashTextBox.Text) ? null : TgApiHashTextBox.Text.Trim();
                var st = await _telegramService.RequestOtpAsync(phone, customApiId, customApiHash);
                if (!string.IsNullOrEmpty(st.ErrorMessage))
                {
                    string err = st.ErrorMessage;
                    if (err.Contains("PHONE_PASSWORD_FLOOD", StringComparison.OrdinalIgnoreCase))
                    {
                        err = I18n.Instance["settings.tgPasswordFlood"];
                    }
                    else if (err.Contains("PHONE_NUMBER_INVALID", StringComparison.OrdinalIgnoreCase))
                    {
                        err = "Nomor telepon tidak valid (PHONE_NUMBER_INVALID). Pastikan gunakan kode negara, e.g. +62812...";
                    }
                    else if (err.Contains("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
                    {
                        string seconds = System.Text.RegularExpressions.Regex.Match(err, @"\d+").Value;
                        if (string.IsNullOrEmpty(seconds) || seconds == "0") seconds = "beberapa";
                        err = $"Telegram membatasi permintaan sementara ({err}). Harap tunggu {seconds} detik sebelum mencoba lagi.";
                    }
                    ShowTgError(err);
                }
                else
                {
                    TgOtpBlock.Visibility = Visibility.Visible;
                    if (st.NeedsPassword)
                    {
                        TgPasswordBlock.Visibility = Visibility.Visible;
                    }
                    TgOtpTextBox.Focus();
                }
            }
        }
        catch (Exception ex)
        {
            ShowTgError($"Gagal meminta OTP: {ex.Message}");
        }
        finally
        {
            if (btn != null)
            {
                btn.IsEnabled = true;
                if (btn.Content is TextBlock tb) tb.Text = origText;
            }
        }
    }

    private async void TgVerifyOtp_Click(object sender, RoutedEventArgs e)
    {
        string code = TgOtpTextBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            ShowTgError("Masukkan kode OTP verifikasi.");
            return;
        }

        TgErrorBorder.Visibility = Visibility.Collapsed;
        var btn = sender as Button;
        string origText = (btn?.Content as TextBlock)?.Text ?? I18n.Instance["settings.tgVerifyLogin"];
        if (btn != null)
        {
            btn.IsEnabled = false;
            if (btn.Content is TextBlock tb) tb.Text = I18n.Instance["settings.tgVerifyingOtp"];
        }

        try
        {
            if (_telegramService != null)
            {
                var st = await _telegramService.VerifyOtpAsync(code);
                if (st.NeedsPassword)
                {
                    TgPasswordBlock.Visibility = Visibility.Visible;
                    TgPasswordBox.Focus();
                }
                else if (st.IsAuthenticated)
                {
                    UpdateTelegramSessionStatus();
                    ResetAndClearTelegramLoginForm();
                    SubTabTgAccounts.IsChecked = true;
                    TgSubTab_Checked(this, new RoutedEventArgs());
                }
                else
                {
                    string err = !string.IsNullOrEmpty(st.ErrorMessage) 
                        ? st.ErrorMessage 
                        : I18n.Instance["settings.tgInvalidCode"];

                    if (err.Contains("PHONE_CODE_INVALID", StringComparison.OrdinalIgnoreCase))
                    {
                        err = I18n.Instance["settings.tgInvalidCode"];
                    }
                    else if (err.Contains("PHONE_CODE_EXPIRED", StringComparison.OrdinalIgnoreCase))
                    {
                        err = I18n.Instance["settings.tgExpiredCode"];
                    }
                    ShowTgError(err);
                }
            }
        }
        catch (Exception ex)
        {
            ShowTgError($"Gagal verifikasi OTP: {ex.Message}");
        }
        finally
        {
            if (btn != null)
            {
                btn.IsEnabled = true;
                if (btn.Content is TextBlock tb) tb.Text = origText;
            }
        }
    }

    private void TgPhone_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TgSendOtp_Click(TgSendOtpBtn, new RoutedEventArgs());
        }
    }

    private void TgOtp_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TgVerifyOtp_Click(TgVerifyOtpBtn, new RoutedEventArgs());
        }
    }

    private void TgPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TgVerifyPassword_Click(TgVerifyPasswordBtn, new RoutedEventArgs());
        }
    }

    private void ToggleTgPassword_Click(object sender, RoutedEventArgs e)
    {
        if (TgPasswordBox.Visibility == Visibility.Visible)
        {
            TgPasswordVisibleTextBox.Text = TgPasswordBox.Password;
            TgPasswordBox.Visibility = Visibility.Collapsed;
            TgPasswordVisibleTextBox.Visibility = Visibility.Visible;
            TgPasswordVisibleTextBox.Focus();
            TgPasswordVisibleTextBox.CaretIndex = TgPasswordVisibleTextBox.Text.Length;
            TgEyeIcon.Kind = "eye-off";
        }
        else
        {
            TgPasswordBox.Password = TgPasswordVisibleTextBox.Text;
            TgPasswordVisibleTextBox.Visibility = Visibility.Collapsed;
            TgPasswordBox.Visibility = Visibility.Visible;
            TgPasswordBox.Focus();
            TgEyeIcon.Kind = "eye";
        }
    }

    private string GetEffectiveTgPassword()
    {
        return TgPasswordBox.Visibility == Visibility.Visible
            ? TgPasswordBox.Password
            : TgPasswordVisibleTextBox.Text;
    }

    private async void TgVerifyPassword_Click(object sender, RoutedEventArgs e)
    {
        string pass = GetEffectiveTgPassword();
        if (string.IsNullOrEmpty(pass))
        {
            ShowTgError("Masukkan password 2FA Anda.");
            return;
        }

        TgErrorBorder.Visibility = Visibility.Collapsed;
        var btn = TgVerifyPasswordBtn;
        string origText = TgVerifyPasswordBtnText.Text;
        btn.IsEnabled = false;
        TgVerifyPasswordBtnText.Text = I18n.Instance["settings.tgCheckingPass"];

        try
        {
            if (_telegramService != null)
            {
                var st = await _telegramService.VerifyPasswordAsync(pass);
                if (st.IsAuthenticated)
                {
                    UpdateTelegramSessionStatus();
                    ResetAndClearTelegramLoginForm();
                    SubTabTgAccounts.IsChecked = true;
                    TgSubTab_Checked(this, new RoutedEventArgs());
                }
                else
                {
                    string err = st.ErrorMessage ?? "PASSWORD_HASH_INVALID";
                    if (err.Contains("PHONE_PASSWORD_FLOOD", StringComparison.OrdinalIgnoreCase))
                    {
                        err = I18n.Instance["settings.tgPasswordFlood"];
                    }
                    else if (err.Contains("PASSWORD_HASH_INVALID", StringComparison.OrdinalIgnoreCase))
                    {
                        err = I18n.Instance["settings.tgInvalidPass"];
                    }
                    else if (err.StartsWith("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
                    {
                        string seconds = System.Text.RegularExpressions.Regex.Match(err, @"\d+").Value;
                        if (string.IsNullOrEmpty(seconds) || seconds == "0") seconds = "beberapa";
                        err = I18n.Instance.CurrentLanguage == "en"
                            ? $"Telegram rate limited this attempt (Flood Wait). Too many invalid attempts. Please wait {seconds} seconds before trying again."
                            : $"Telegram membatasi percobaan (Flood Wait). Terlalu banyak percobaan kata sandi salah. Harap tunggu {seconds} detik sebelum mencoba lagi.";
                    }
                    ShowTgError(err);
                }
            }
        }
        catch (Exception ex)
        {
            ShowTgError($"Gagal verifikasi 2FA: {ex.Message}");
        }
        finally
        {
            btn.IsEnabled = true;
            TgVerifyPasswordBtnText.Text = origText;
        }
    }

    private async void TgLogout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Apakah Anda yakin ingin menghapus sesi Telegram ini?", "Konfirmasi Hapus", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                if (_telegramService != null)
                {
                    await _telegramService.LogoutAsync();
                }
                else
                {
                    string sessionPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "MDM",
                        "telegram",
                        "mdm_telegram.session"
                    );
                    if (File.Exists(sessionPath))
                    {
                        File.Delete(sessionPath);
                    }
                }
                UpdateTelegramSessionStatus();
                ResetAndClearTelegramLoginForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Gagal menghapus session: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ShowTgError(string msg)
    {
        TgErrorText.Text = msg;
        TgErrorBorder.Visibility = Visibility.Visible;
        TgErrorBorder.BringIntoView();
    }

    #endregion

    #region Proxy Testing

    private async void TestProxy_Click(object sender, RoutedEventArgs e)
    {
        TestProxyBtn.IsEnabled = false;
        ProxyTestResultBadge.Visibility = Visibility.Collapsed;

        string host = ProxyHostTextBox.Text.Trim();
        if (string.IsNullOrEmpty(host) || !int.TryParse(ProxyPortTextBox.Text.Trim(), out int port))
        {
            ShowProxyTestResult(false, "Alamat host atau port tidak valid");
            TestProxyBtn.IsEnabled = true;
            return;
        }

        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(4000));

            if (completed == connectTask && tcp.Connected)
            {
                ShowProxyTestResult(true, $"Koneksi ke {host}:{port} berhasil! Respon aktif.");
            }
            else
            {
                ShowProxyTestResult(false, $"Gagal terhubung ke {host}:{port} (Timeout 4s)");
            }
        }
        catch (Exception ex)
        {
            ShowProxyTestResult(false, $"Error koneksi proxy: {ex.Message}");
        }
        finally
        {
            TestProxyBtn.IsEnabled = true;
        }
    }

    private void ShowProxyTestResult(bool success, string msg)
    {
        ProxyTestResultBadge.Visibility = Visibility.Visible;
        if (success)
        {
            ProxyTestResultBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, 0x10, 0xB9, 0x81));
            ProxyTestResultBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x10, 0xB9, 0x81));
            ProxyTestIcon.Kind = "check";
            ProxyTestIcon.Foreground = (Brush)FindResource("AccentEmeraldBrush");
            ProxyTestResultText.Foreground = (Brush)FindResource("AccentEmeraldBrush");
        }
        else
        {
            ProxyTestResultBadge.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0x52, 0x52));
            ProxyTestResultBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0x52, 0x52));
            ProxyTestIcon.Kind = "x";
            ProxyTestIcon.Foreground = (Brush)FindResource("AccentRoseBrush");
            ProxyTestResultText.Foreground = (Brush)FindResource("AccentRoseBrush");
        }
        ProxyTestResultText.Text = msg;
    }

    #endregion

    #region Save, Apply & Reset Defaults

    private void SaveAllSettingsToDatabase()
    {
        // 1. UMUM
        string newLang = LanguageComboBox.SelectedIndex == 1 ? "en" : "id";
        _database.SetSetting("language", newLang);
        I18n.Instance.SetLanguage(newLang);
        _database.SetSetting("minimize_to_tray", MinimizeToTrayCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("media_panel_overlay", MediaPanelCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("clipboard_auto_capture", ClipboardCaptureCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("notify_on_complete", NotifyOnCompleteCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("show_completion_dialog", ShowCompletionDialogCheckBox.IsChecked == true ? "1" : "0");

        // Windows Startup Registry
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key != null)
            {
                if (AutoStartCheckBox.IsChecked == true)
                {
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\"");
                    }
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
        }
        catch { }

        // 2. SAMBUNGAN
        bool limitEnabled = EnableLimitCheckBox.IsChecked == true;
        _database.SetSetting("global_limit_enabled", limitEnabled ? "1" : "0");
        _database.SetSetting("global_limit_value", GlobalLimitValueTextBox.Text.Trim());
        string limitUnit = GlobalLimitUnitComboBox.SelectedIndex == 1 ? "MB/s" : "KB/s";
        _database.SetSetting("global_limit_unit", limitUnit);

        // Convert to Kbps for engine compatibility
        if (long.TryParse(GlobalLimitValueTextBox.Text.Trim(), out long val))
        {
            long kbps = limitUnit == "MB/s" ? val * 1024 : val;
            _database.SetSetting("global_limit_kbps", kbps.ToString());
        }

        _database.SetSetting("connection_timeout_sec", ConnectionTimeoutTextBox.Text.Trim());
        _database.SetSetting("max_retries", MaxRetriesTextBox.Text.Trim());

        // 3. JENIS BERKAS
        _database.SetSetting("auto_capture_extensions", AutoCaptureExtensionsTextBox.Text.Trim());
        _database.SetSetting("excluded_sites", ExcludedSitesTextBox.Text.Trim());

        // 4. LOKASI SIMPAN
        _database.SetSetting("default_dir", DefaultDownloadDirTextBox.Text.Trim());
        _database.SetSetting("subfolder_by_category", CategorySubfoldersCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("temp_dir", TempDirTextBox.Text.Trim());

        string dupAction = DuplicateActionComboBox.SelectedIndex switch
        {
            1 => "numbered",
            2 => "overwrite",
            3 => "resume",
            _ => "ask"
        };
        _database.SetSetting("duplicate_action", dupAction);

        // 5. EKSTENSI
        _database.SetSetting("browser_chrome", BrowserChromeCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("browser_edge", BrowserEdgeCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("browser_firefox", BrowserFirefoxCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("browser_brave", BrowserBraveCheckBox.IsChecked == true ? "1" : "0");

        // 6. TELEGRAM
        _database.SetSetting("telegram_download_dir", TelegramDirTextBox.Text.Trim());
        _database.SetSetting("telegram_subfolder", TelegramSubfolderCheckBox.IsChecked == true ? "1" : "0");
        string customId = TgApiIdTextBox.Text.Trim();
        _database.SetSetting("tg_api_id", customId == "2040" ? "" : customId);

        string customHash = TgApiHashTextBox.Text.Trim();
        _database.SetSetting("tg_api_hash", customHash == "b18441a1ff607e10a989891a5462e627" ? "" : customHash);

        // 7. PROXY
        _database.SetSetting("proxy_enabled", ProxyEnabledCheckBox.IsChecked == true ? "1" : "0");
        string pType = "http";
        if (ProxyTypeHttpsRadio.IsChecked == true) pType = "https";
        else if (ProxyTypeSocks5Radio.IsChecked == true) pType = "socks5";
        _database.SetSetting("proxy_type", pType);

        _database.SetSetting("proxy_host", ProxyHostTextBox.Text.Trim());
        _database.SetSetting("proxy_port", ProxyPortTextBox.Text.Trim());
        _database.SetSetting("proxy_auth", ProxyAuthCheckBox.IsChecked == true ? "1" : "0");
        _database.SetSetting("proxy_user", ProxyUserTextBox.Text.Trim());
        _database.SetSetting("proxy_pass", _showPassword ? ProxyPasswordVisibleTextBox.Text : ProxyPasswordBox.Password);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SaveAllSettingsToDatabase();
        ShowSuccessBanner("Pengaturan berhasil disimpan dan diterapkan!");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SaveAllSettingsToDatabase();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Kembalikan semua konfigurasi pengaturan ke nilai bawaan pabrik (Default)?", "Konfirmasi Reset", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            LanguageComboBox.SelectedIndex = 0;
            AutoStartCheckBox.IsChecked = true;
            MinimizeToTrayCheckBox.IsChecked = true;
            MediaPanelCheckBox.IsChecked = true;
            ClipboardCaptureCheckBox.IsChecked = true;
            NotifyOnCompleteCheckBox.IsChecked = true;
            ShowCompletionDialogCheckBox.IsChecked = true;

            EnableLimitCheckBox.IsChecked = false;
            GlobalLimitValueTextBox.Text = "2048";
            GlobalLimitUnitComboBox.SelectedIndex = 0;
            ConnectionTimeoutTextBox.Text = "30";
            MaxRetriesTextBox.Text = "10";

            AutoCaptureExtensionsTextBox.Text = DefaultAutoCaptureExt;
            ExcludedSitesTextBox.Text = DefaultExcludedSites;

            DefaultDownloadDirTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            CategorySubfoldersCheckBox.IsChecked = true;
            TempDirTextBox.Text = Path.Combine(Path.GetTempPath(), "MDM_Chunks");
            DuplicateActionComboBox.SelectedIndex = 0;

            BrowserChromeCheckBox.IsChecked = true;
            BrowserEdgeCheckBox.IsChecked = true;
            BrowserFirefoxCheckBox.IsChecked = true;
            BrowserBraveCheckBox.IsChecked = true;

            ProxyEnabledCheckBox.IsChecked = false;
            ProxyTypeHttpRadio.IsChecked = true;
            ProxyHostTextBox.Text = "127.0.0.1";
            ProxyPortTextBox.Text = "1080";
            ProxyAuthCheckBox.IsChecked = false;
            ProxyUserTextBox.Text = "";
            ProxyPasswordBox.Password = "";
            ProxyPasswordVisibleTextBox.Text = "";

            SaveAllSettingsToDatabase();
            ShowSuccessBanner("Semua pengaturan telah dikembalikan ke nilai bawaan!");
        }
    }

    private void ShowSuccessBanner(string msg)
    {
        SuccessBannerText.Text = msg;
        SuccessBanner.Visibility = Visibility.Visible;

        _bannerTimer?.Stop();
        _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _bannerTimer.Tick += (s, ev) =>
        {
            SuccessBanner.Visibility = Visibility.Collapsed;
            _bannerTimer.Stop();
        };
        _bannerTimer.Start();
    }

    #endregion
}
