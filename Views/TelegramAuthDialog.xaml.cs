using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mdm.Models;
using Mdm.Services;

namespace Mdm.Views;

public partial class TelegramAuthDialog : Window
{
    private readonly TelegramService _telegramService;
    private readonly Database? _database;

    public bool IsSuccess { get; private set; }
    private string _currentPhone = string.Empty;

    public TelegramAuthDialog(TelegramService telegramService, Database? database = null)
    {
        InitializeComponent();
        _telegramService = telegramService;
        _database = database;

        if (!string.IsNullOrEmpty(_telegramService.AuthStatus.PhoneNumber))
        {
            PhoneTextBox.Text = _telegramService.AuthStatus.PhoneNumber;
        }

        Loaded += (_, _) =>
        {
            PhoneTextBox.Focus();
            PhoneTextBox.CaretIndex = PhoneTextBox.Text.Length;
        };
    }

    #region Window Drag & Controls

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = IsSuccess;
        Close();
    }

    #endregion

    #region Step Transitions & UI Updates

    private void SetStep(int step)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;

        PanelStep1Phone.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep2Otp.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep3Password.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelStep4Success.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        // Badge 1
        Step1Badge.Background = step >= 1 ? (Brush)FindResource("AccentCyanBrush") : new SolidColorBrush(Color.FromRgb(0x25, 0x2A, 0x33));
        Step1Number.Foreground = step >= 1 ? new SolidColorBrush(Color.FromRgb(0x09, 0x0E, 0x16)) : (Brush)FindResource("TextSecondaryBrush");
        Step1Label.Foreground = step >= 1 ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextSecondaryBrush");

        // Line 1
        StepLine1.Background = step >= 2 ? (Brush)FindResource("AccentCyanBrush") : (Brush)FindResource("BorderBrush");

        // Badge 2
        Step2Badge.Background = step >= 2 ? (Brush)FindResource("AccentEmeraldBrush") : new SolidColorBrush(Color.FromRgb(0x25, 0x2A, 0x33));
        Step2Number.Foreground = step >= 2 ? Brushes.White : (Brush)FindResource("TextSecondaryBrush");
        Step2Label.Foreground = step >= 2 ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextSecondaryBrush");

        // Line 2
        StepLine2.Background = step >= 3 ? (Brush)FindResource("AccentEmeraldBrush") : (Brush)FindResource("BorderBrush");

        // Badge 3
        Step3Badge.Background = step >= 4 ? (Brush)FindResource("AccentEmeraldBrush") : (step == 3 ? (Brush)FindResource("AccentRoseBrush") : new SolidColorBrush(Color.FromRgb(0x25, 0x2A, 0x33)));
        Step3Number.Foreground = step >= 3 ? Brushes.White : (Brush)FindResource("TextSecondaryBrush");
        Step3Label.Foreground = step >= 3 ? (Brush)FindResource("TextPrimaryBrush") : (Brush)FindResource("TextSecondaryBrush");
        Step3Label.Text = step == 3 ? "Verifikasi 2FA" : "Selesai";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    #endregion

    #region Step 1: Send OTP

    private async void SendOtp_Click(object sender, RoutedEventArgs e)
    {
        string phone = PhoneTextBox.Text.Trim();
        if (string.IsNullOrEmpty(phone) || phone.Length < 7)
        {
            ShowError("Masukkan nomor telepon valid dengan kode negara (contoh: +628123456789).");
            return;
        }

        _currentPhone = phone;
        SendOtpBtn.IsEnabled = false;
        SendOtpBtnText.Text = "Mengirim OTP...";
        ErrorBanner.Visibility = Visibility.Collapsed;

        string? customId = string.IsNullOrWhiteSpace(CustomApiIdTextBox.Text) ? null : CustomApiIdTextBox.Text.Trim();
        string? customHash = string.IsNullOrWhiteSpace(CustomApiHashTextBox.Text) ? null : CustomApiHashTextBox.Text.Trim();

        try
        {
            var res = await _telegramService.RequestOtpAsync(phone, customId, customHash);
            if (res.NeedsOtp)
            {
                PhoneSentDisplay.Text = phone;
                SetStep(2);
                OtpTextBox.Text = string.Empty;
                OtpTextBox.Focus();
            }
            else if (res.IsAuthenticated)
            {
                ShowSuccessView(res);
            }
            else if (!string.IsNullOrEmpty(res.ErrorMessage))
            {
                string err = res.ErrorMessage;
                if (err.Contains("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
                {
                    err = $"Telegram membatasi permintaan sementara ({err}). Harap tunggu sebelum mencoba lagi.";
                }
                else if (err.Contains("PHONE_NUMBER_INVALID", StringComparison.OrdinalIgnoreCase))
                {
                    err = "Nomor telepon tidak valid. Pastikan gunakan kode negara, contoh: +62812...";
                }
                ShowError(err);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Gagal mengirim OTP: {ex.Message}");
        }
        finally
        {
            SendOtpBtn.IsEnabled = true;
            SendOtpBtnText.Text = "Kirim Kode OTP Verifikasi";
        }
    }

    private void Phone_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendOtp_Click(sender, e);
        }
    }

    private void BackToPhone_Click(object sender, RoutedEventArgs e)
    {
        SetStep(1);
        PhoneTextBox.Focus();
        PhoneTextBox.CaretIndex = PhoneTextBox.Text.Length;
    }

    private void ResendOtp_Click(object sender, RoutedEventArgs e)
    {
        SetStep(1);
        SendOtp_Click(SendOtpBtn, e);
    }

    private void ToggleAdvancedApi_Click(object sender, RoutedEventArgs e)
    {
        bool isCollapsed = AdvancedApiPanel.Visibility == Visibility.Collapsed;
        AdvancedApiPanel.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
        AdvancedChevronIcon.Kind = isCollapsed ? "chevron-up" : "chevron-down";
    }

    #endregion

    #region Step 2: Verify OTP

    private async void VerifyOtp_Click(object sender, RoutedEventArgs e)
    {
        string otp = OtpTextBox.Text.Trim();
        if (string.IsNullOrEmpty(otp))
        {
            ShowError("Masukkan kode OTP verifikasi.");
            return;
        }

        VerifyOtpBtn.IsEnabled = false;
        VerifyOtpBtnText.Text = "Memverifikasi...";
        ErrorBanner.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _telegramService.VerifyOtpAsync(otp);

            if (res.NeedsPassword)
            {
                SetStep(3);
                PasswordBox.Password = string.Empty;
                PasswordVisibleTextBox.Text = string.Empty;
                PasswordBox.Focus();
            }
            else if (res.IsAuthenticated)
            {
                ShowSuccessView(res);
            }
            else
            {
                string err = res.ErrorMessage ?? "Kode OTP tidak valid.";
                if (err.Contains("PHONE_CODE_INVALID", StringComparison.OrdinalIgnoreCase))
                {
                    err = "Kode OTP tidak valid. Pastikan memasukkan 5 digit kode terbaru dari aplikasi Telegram.";
                }
                else if (err.Contains("PHONE_CODE_EXPIRED", StringComparison.OrdinalIgnoreCase))
                {
                    err = "Kode OTP telah kedaluwarsa. Silakan kirim ulang kode OTP.";
                }
                ShowError(err);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Gagal verifikasi OTP: {ex.Message}");
        }
        finally
        {
            VerifyOtpBtn.IsEnabled = true;
            VerifyOtpBtnText.Text = "Verifikasi & Masuk";
        }
    }

    private void Otp_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            VerifyOtp_Click(sender, e);
        }
    }

    #endregion

    #region Step 3: Verify 2FA Password

    private string GetEffectivePassword()
    {
        return PasswordBox.Visibility == Visibility.Visible
            ? PasswordBox.Password
            : PasswordVisibleTextBox.Text;
    }

    private async void VerifyPassword_Click(object sender, RoutedEventArgs e)
    {
        string pass = GetEffectivePassword();
        if (string.IsNullOrEmpty(pass))
        {
            ShowError("Masukkan kata sandi 2FA akun Anda.");
            return;
        }

        VerifyPasswordBtn.IsEnabled = false;
        VerifyPasswordBtnText.Text = "Memeriksa Kata Sandi...";
        ErrorBanner.Visibility = Visibility.Collapsed;

        try
        {
            var res = await _telegramService.VerifyPasswordAsync(pass);
            if (res.IsAuthenticated)
            {
                ShowSuccessView(res);
            }
            else
            {
                string err = res.ErrorMessage ?? "Kata sandi 2FA salah.";
                if (err.Contains("PASSWORD_HASH_INVALID", StringComparison.OrdinalIgnoreCase))
                {
                    err = "Kata sandi 2FA salah atau tidak valid.";
                }
                else if (err.Contains("PHONE_PASSWORD_FLOOD", StringComparison.OrdinalIgnoreCase))
                {
                    err = "Keamanan Telegram mengunci sementara akun karena terlalu banyak percobaan salah. Harap tunggu beberapa saat.";
                }
                ShowError(err);
            }
        }
        catch (Exception ex)
        {
            ShowError($"Gagal verifikasi 2FA: {ex.Message}");
        }
        finally
        {
            VerifyPasswordBtn.IsEnabled = true;
            VerifyPasswordBtnText.Text = "Konfirmasi Kata Sandi";
        }
    }

    private void Password_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            VerifyPassword_Click(sender, e);
        }
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        if (PasswordBox.Visibility == Visibility.Visible)
        {
            PasswordVisibleTextBox.Text = PasswordBox.Password;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordVisibleTextBox.Visibility = Visibility.Visible;
            PasswordVisibleTextBox.Focus();
            PasswordVisibleTextBox.CaretIndex = PasswordVisibleTextBox.Text.Length;
            EyeIcon.Kind = "eye-off";
        }
        else
        {
            PasswordBox.Password = PasswordVisibleTextBox.Text;
            PasswordVisibleTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordBox.Focus();
            EyeIcon.Kind = "eye";
        }
    }

    #endregion

    #region Step 4: Success View

    private void ShowSuccessView(TelegramAuthStatus status)
    {
        IsSuccess = true;
        SetStep(4);

        SuccessDisplayNameText.Text = !string.IsNullOrEmpty(status.DisplayName) ? status.DisplayName : "Pengguna Telegram";
        SuccessUsernameText.Text = !string.IsNullOrEmpty(status.Username) ? $"@{status.Username.TrimStart('@')}" : "MTProto Account";
        SuccessPhoneText.Text = !string.IsNullOrEmpty(status.PhoneNumber) ? status.PhoneNumber : _currentPhone;

        // Wipe sensitive credentials immediately
        PhoneTextBox.Text = "+62";
        OtpTextBox.Text = string.Empty;
        PasswordBox.Password = string.Empty;
        PasswordVisibleTextBox.Text = string.Empty;
    }

    private void FinishSuccess_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    #endregion
}
