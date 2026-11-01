using System.Windows;
using System.Windows.Media;
using Mdm.Services;

namespace Mdm.Views;

public partial class TelegramAuthDialog : Window
{
    private readonly TelegramService _telegramService;

    public bool IsSuccess { get; private set; }

    public TelegramAuthDialog(TelegramService telegramService)
    {
        InitializeComponent();
        _telegramService = telegramService;

        if (!string.IsNullOrEmpty(_telegramService.AuthStatus.PhoneNumber))
        {
            PhoneTextBox.Text = _telegramService.AuthStatus.PhoneNumber;
        }

        Loaded += (_, _) =>
        {
            PhoneTextBox.Focus();
        };
    }

    private async void SendOtp_Click(object sender, RoutedEventArgs e)
    {
        string phone = PhoneTextBox.Text.Trim();
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
        {
            ShowStatus("Masukkan nomor telepon yang valid (contoh: +628123456789).", isError: true);
            return;
        }

        SendOtpBtn.IsEnabled = false;
        ShowStatus("Mengirim kode OTP ke Telegram...", isError: false);

        try
        {
            var res = await _telegramService.RequestOtpAsync(phone);
            if (res.NeedsOtp)
            {
                ShowStatus("Kode verifikasi telah dikirim ke aplikasi Telegram Anda. Masukkan kode di bawah.", isError: false);
                OtpTextBox.Focus();
            }
            else if (res.IsAuthenticated)
            {
                ShowStatus($"Berhasil login sebagai {res.DisplayName}!", isError: false);
                IsSuccess = true;
                DialogResult = true;
                Close();
            }
            else if (!string.IsNullOrEmpty(res.ErrorMessage))
            {
                ShowStatus($"Gagal: {res.ErrorMessage}", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", isError: true);
        }
        finally
        {
            SendOtpBtn.IsEnabled = true;
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        string otp = OtpTextBox.Text.Trim();
        string password = PasswordBox.Password;

        if (string.IsNullOrEmpty(otp))
        {
            ShowStatus("Masukkan kode OTP terlebih dahulu.", isError: true);
            return;
        }

        LoginBtn.IsEnabled = false;
        ShowStatus("Memverifikasi kredensial...", isError: false);

        try
        {
            var res = await _telegramService.VerifyOtpAsync(otp);

            if (res.NeedsPassword)
            {
                if (string.IsNullOrEmpty(password))
                {
                    ShowStatus("Akun Anda mengaktifkan 2FA. Silakan masukkan Password 2FA di atas.", isError: true);
                    PasswordBox.Focus();
                    return;
                }

                res = await _telegramService.VerifyPasswordAsync(password);
            }

            if (res.IsAuthenticated)
            {
                ShowStatus($"Login berhasil! Selamat datang, {res.DisplayName}.", isError: false);
                IsSuccess = true;
                DialogResult = true;
                Close();
            }
            else
            {
                ShowStatus(res.ErrorMessage ?? "Login gagal. Periksa kembali kode OTP atau Password Anda.", isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Kesalahan: {ex.Message}", isError: true);
        }
        finally
        {
            LoginBtn.IsEnabled = true;
        }
    }

    private void ShowStatus(string msg, bool isError)
    {
        StatusBadge.Visibility = Visibility.Visible;
        StatusText.Text = msg;
        StatusText.Foreground = isError 
            ? (Brush)FindResource("AccentRoseBrush") 
            : (Brush)FindResource("AccentCyanBrush");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

