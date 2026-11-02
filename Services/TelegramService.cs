using System.IO;
using Mdm.Models;
using TL;
using WTelegram;

namespace Mdm.Services;

public class TelegramService
{
    private const string DefaultApiId = "2040";
    private const string DefaultApiHash = "b18441a1ff607e10a989891a5462e627";

    private readonly string _sessionDir;
    private readonly string _sessionPath;
    private readonly Database _database;

    private WTelegram.Client? _client;
    private string? _currentPhoneNumber;
    private string? _currentOtpCode;
    private string? _currentPassword;

    public TelegramAuthStatus AuthStatus { get; private set; } = new();

    public TelegramService(Database database)
    {
        _database = database;
        _sessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MDM",
            "telegram"
        );

        if (!Directory.Exists(_sessionDir))
        {
            Directory.CreateDirectory(_sessionDir);
        }

        _sessionPath = Path.Combine(_sessionDir, "mdm_telegram.session");
    }

    private string? Config(string what)
    {
        return what switch
        {
            "api_id" => !string.IsNullOrWhiteSpace(_database.GetSetting("tg_api_id")) ? _database.GetSetting("tg_api_id") : DefaultApiId,
            "api_hash" => !string.IsNullOrWhiteSpace(_database.GetSetting("tg_api_hash")) ? _database.GetSetting("tg_api_hash") : DefaultApiHash,
            "phone_number" => _currentPhoneNumber ?? _database.GetSetting("tg_phone_number"),
            "verification_code" => _currentOtpCode,
            "password" => _currentPassword,
            "session_pathname" => _sessionPath,
            _ => null
        };
    }

    public async Task<TelegramAuthStatus> CheckAndRestoreAuthAsync()
    {
        try
        {
            if (!File.Exists(_sessionPath))
            {
                AuthStatus = new TelegramAuthStatus { IsAuthenticated = false };
                return AuthStatus;
            }

            _client ??= new WTelegram.Client(Config);
            var user = await _client.LoginUserIfNeeded();

            if (user != null)
            {
                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = true,
                    UserId = user.id,
                    PhoneNumber = user.phone,
                    Username = user.username,
                    DisplayName = $"{user.first_name} {user.last_name}".Trim()
                };
                Logger.Info($"Telegram session dipulihkan untuk akun: {AuthStatus.DisplayName} (@{AuthStatus.Username})");
            }
            else
            {
                AuthStatus = new TelegramAuthStatus { IsAuthenticated = false };
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Gagal memulihkan session Telegram: {ex.Message}");
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = ex.Message };
        }

        return AuthStatus;
    }

    public async Task<TelegramAuthStatus> RequestOtpAsync(string phoneNumber, string? customApiId = null, string? customApiHash = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(customApiId) && !string.IsNullOrEmpty(customApiHash))
            {
                _database.SetSetting("tg_api_id", customApiId);
                _database.SetSetting("tg_api_hash", customApiHash);
            }

            _currentPhoneNumber = phoneNumber.Trim();
            _database.SetSetting("tg_phone_number", _currentPhoneNumber);

            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            // If not authenticated, ensure previous incomplete session is deleted so WTelegram doesn't confuse phone number with OTP/2FA code
            if (!AuthStatus.IsAuthenticated && File.Exists(_sessionPath))
            {
                try { File.Delete(_sessionPath); } catch { }
            }

            _client = new WTelegram.Client(Config);

            string loginResult = await _client.Login(_currentPhoneNumber);

            if (loginResult == "verification_code")
            {
                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = false,
                    PhoneNumber = _currentPhoneNumber,
                    NeedsOtp = true
                };
            }
            else if (string.IsNullOrEmpty(loginResult))
            {
                var user = _client.User;
                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = true,
                    UserId = user?.id,
                    PhoneNumber = user?.phone ?? _currentPhoneNumber,
                    Username = user?.username,
                    DisplayName = $"{user?.first_name} {user?.last_name}".Trim()
                };
                Logger.Info($"Login Telegram langsung sukses: {AuthStatus.DisplayName}");
            }

            return AuthStatus;
        }
        catch (RpcException rpcEx)
        {
            int waitSec = rpcEx.X;
            string msg = rpcEx.Message;
            if (msg.StartsWith("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
            {
                msg = $"FLOOD_WAIT_{waitSec}";
            }
            Logger.Error($"Gagal meminta OTP Telegram | RpcException: {msg} (Code: {rpcEx.Code}, X: {waitSec})", rpcEx);
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = msg };
            return AuthStatus;
        }
        catch (Exception ex)
        {
            Logger.Error("Gagal meminta OTP Telegram", ex);
            AuthStatus = new TelegramAuthStatus
            {
                IsAuthenticated = false,
                ErrorMessage = ex.Message
            };
            return AuthStatus;
        }
    }

    public async Task<TelegramAuthStatus> VerifyOtpAsync(string code)
    {
        if (_client == null)
        {
            if (!string.IsNullOrEmpty(_currentPhoneNumber) || !string.IsNullOrEmpty(_database.GetSetting("tg_phone_number")))
            {
                _client = new WTelegram.Client(Config);
            }
            else
            {
                return new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = "Klien Telegram belum dimulai. Silakan kirim kode OTP terlebih dahulu." };
            }
        }

        try
        {
            _currentOtpCode = code.Trim();
            string loginResult = await _client.Login(_currentOtpCode);

            if (loginResult == "password")
            {
                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = false,
                    PhoneNumber = _currentPhoneNumber ?? _database.GetSetting("tg_phone_number"),
                    NeedsPassword = true
                };
            }
            else if (string.IsNullOrEmpty(loginResult))
            {
                var user = _client.User;
                string? phone = user?.phone ?? _currentPhoneNumber ?? _database.GetSetting("tg_phone_number");
                if (!string.IsNullOrEmpty(phone))
                {
                    _database.SetSetting("tg_phone_number", phone);
                }

                _currentOtpCode = null;
                _currentPassword = null;
                _currentPhoneNumber = null;

                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = true,
                    UserId = user?.id,
                    PhoneNumber = phone,
                    Username = user?.username,
                    DisplayName = $"{user?.first_name} {user?.last_name}".Trim()
                };
                Logger.Info($"Login Telegram sukses: {AuthStatus.DisplayName}");
            }

            return AuthStatus;
        }
        catch (RpcException rpcEx)
        {
            int waitSec = rpcEx.X;
            string msg = rpcEx.Message;
            if (msg.StartsWith("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
            {
                msg = $"FLOOD_WAIT_{waitSec}";
            }
            Logger.Error($"Gagal verifikasi OTP Telegram | RpcException: {msg} (Code: {rpcEx.Code}, X: {waitSec})", rpcEx);
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = msg };
            return AuthStatus;
        }
        catch (Exception ex)
        {
            Logger.Error("Gagal verifikasi OTP Telegram", ex);
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = ex.Message };
            return AuthStatus;
        }
    }

    public async Task<TelegramAuthStatus> VerifyPasswordAsync(string password)
    {
        if (_client == null)
        {
            return new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = "Klien Telegram belum dimulai." };
        }

        try
        {
            _currentPassword = password;
            string loginResult = await _client.Login(_currentPassword);

            if (string.IsNullOrEmpty(loginResult))
            {
                var user = _client.User;
                string? phone = user?.phone ?? _currentPhoneNumber ?? _database.GetSetting("tg_phone_number");
                if (!string.IsNullOrEmpty(phone))
                {
                    _database.SetSetting("tg_phone_number", phone);
                }

                _currentOtpCode = null;
                _currentPassword = null;
                _currentPhoneNumber = null;

                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = true,
                    UserId = user?.id,
                    PhoneNumber = phone,
                    Username = user?.username,
                    DisplayName = $"{user?.first_name} {user?.last_name}".Trim()
                };
                Logger.Info($"Verifikasi 2FA Telegram sukses: {AuthStatus.DisplayName}");
            }
            else
            {
                AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = "PASSWORD_HASH_INVALID" };
            }

            return AuthStatus;
        }
        catch (RpcException rpcEx)
        {
            int waitSec = rpcEx.X;
            string msg = rpcEx.Message;
            if (msg.StartsWith("FLOOD_WAIT", StringComparison.OrdinalIgnoreCase))
            {
                msg = $"FLOOD_WAIT_{waitSec}";
            }
            Logger.Error($"Gagal verifikasi password 2FA Telegram | RpcException: {msg} (Code: {rpcEx.Code}, X: {waitSec})", rpcEx);
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = msg };
            return AuthStatus;
        }
        catch (Exception ex)
        {
            Logger.Error("Gagal verifikasi password 2FA Telegram", ex);
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = ex.Message };
            return AuthStatus;
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            if (File.Exists(_sessionPath))
            {
                File.Delete(_sessionPath);
            }

            _database.SetSetting("tg_phone_number", "");
            _currentPhoneNumber = null;
            _currentOtpCode = null;
            _currentPassword = null;

            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false };
            Logger.Info("Akun Telegram telah logout.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Gagal logout Telegram: {ex.Message}");
        }
    }

    public async Task<List<TelegramChannelInfo>> ListDialogsAsync()
    {
        var list = new List<TelegramChannelInfo>();
        if (_client == null || !AuthStatus.IsAuthenticated) return list;

        try
        {
            var dialogs = await _client.Messages_GetAllDialogs();
            foreach (var (peerId, chat) in dialogs.chats)
            {
                list.Add(new TelegramChannelInfo
                {
                    Id = peerId,
                    Title = chat.Title ?? "Untitled Chat",
                    Username = chat is Channel ch ? ch.username : null,
                    ChatType = chat is Channel ? "channel" : "group"
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Gagal memuat daftar dialog Telegram: {ex.Message}");
        }

        return list;
    }

    public async Task<List<TelegramMediaItem>> ScanChatMediaAsync(string chatQuery, string mediaFilter = "all", int limit = 100)
    {
        var results = new List<TelegramMediaItem>();
        string clean = chatQuery.Trim().Replace("https://t.me/", "").Replace("t.me/", "").TrimStart('@');
        string chatTitle = string.IsNullOrWhiteSpace(clean) ? "Telegram Vault" : clean;

        if (_client == null || !AuthStatus.IsAuthenticated || string.IsNullOrWhiteSpace(clean))
        {
            return results;
        }

        try
        {
            InputPeer? peer = null;

            if (long.TryParse(clean, out long numericId))
            {
                var dialogs = await _client.Messages_GetAllDialogs();
                if (dialogs.chats.TryGetValue(numericId, out var chat))
                {
                    peer = chat;
                    chatTitle = chat.Title;
                }
            }
            else
            {
                try
                {
                    var resolved = await _client.Contacts_ResolveUsername(clean);
                    if (resolved.Chat != null)
                    {
                        peer = resolved.Chat;
                        chatTitle = resolved.Chat.Title;
                    }
                    else if (resolved.User != null)
                    {
                        peer = resolved.User;
                        chatTitle = $"{resolved.User.first_name} {resolved.User.last_name}".Trim();
                    }
                }
                catch
                {
                    // Search in dialogs by title match
                    var dialogs = await _client.Messages_GetAllDialogs();
                    var match = dialogs.chats.Values.FirstOrDefault(c => c.Title.Contains(clean, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        peer = match;
                        chatTitle = match.Title;
                    }
                }
            }

            if (peer == null)
            {
                throw new Exception($"Chat atau channel '{chatQuery}' tidak ditemukan.");
            }

            // Save channel to database
            _database.SaveTelegramChannel(new TelegramChannelInfo
            {
                Id = peer.ID,
                Title = chatTitle,
                Username = clean.StartsWith("@") ? clean : null,
                ChatType = peer is InputPeerUser ? "user" : "channel"
            });

            var history = await _client.Messages_GetHistory(peer, limit: limit);

            foreach (var msgBase in history.Messages)
            {
                if (msgBase is not Message msg) continue;

                if (msg.media is MessageMediaDocument docMedia && docMedia.document is Document doc)
                {
                    string filename = doc.Filename ?? $"telegram_{msg.id}.dat";
                    string mime = doc.mime_type ?? "application/octet-stream";
                    string mediaType = "document";

                    if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) mediaType = "video";
                    else if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) mediaType = "audio";
                    else if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) mediaType = "photo";

                    if (mediaFilter != "all" && !mediaFilter.Equals(mediaType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(new TelegramMediaItem
                    {
                        MessageId = msg.id,
                        ChatId = peer.ID,
                        ChatTitle = chatTitle,
                        SenderName = chatTitle,
                        Filename = filename,
                        FileSize = doc.size,
                        MimeType = mime,
                        MediaType = mediaType,
                        CreatedAt = msg.date,
                        TgUrl = $"tg://chat/{peer.ID}/msg/{msg.id}"
                    });
                }
                else if (msg.media is MessageMediaPhoto photoMedia && photoMedia.photo is Photo photo)
                {
                    if (mediaFilter != "all" && mediaFilter != "photo") continue;

                    results.Add(new TelegramMediaItem
                    {
                        MessageId = msg.id,
                        ChatId = peer.ID,
                        ChatTitle = chatTitle,
                        SenderName = chatTitle,
                        Filename = $"photo_{msg.id}.jpg",
                        FileSize = 0,
                        MimeType = "image/jpeg",
                        MediaType = "photo",
                        CreatedAt = msg.date,
                        TgUrl = $"tg://chat/{peer.ID}/msg/{msg.id}"
                    });
                }
            }

            Logger.Info($"Pindai media Telegram '{chatTitle}' selesai: {results.Count} berkas ditemukan.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Gagal memindai media Telegram untuk '{chatQuery}'", ex);
            throw;
        }

        return results;
    }

    public async Task DownloadMediaMessageAsync(
        long chatId,
        int messageId,
        string targetFilePath,
        Action<long, long> onProgress,
        CancellationToken ct)
    {
        if (_client == null || !AuthStatus.IsAuthenticated)
        {
            throw new InvalidOperationException("Klien Telegram belum login atau tidak terhubung.");
        }

        // Get peer
        var dialogs = await _client.Messages_GetAllDialogs();
        dialogs.chats.TryGetValue(chatId, out var chat);
        dialogs.users.TryGetValue(chatId, out var user);

        if (chat == null && user == null)
        {
            throw new Exception($"Chat ID {chatId} tidak ditemukan dalam daftar dialog.");
        }

        Messages_MessagesBase messagesBase;
        if (chat is Channel ch)
        {
            messagesBase = await _client.Channels_GetMessages(ch, new InputMessage[] { messageId });
        }
        else
        {
            messagesBase = await _client.Messages_GetMessages(new InputMessage[] { messageId });
        }

        if (messagesBase.Messages.Length == 0 || messagesBase.Messages[0] is not Message msg)
        {
            throw new Exception($"Pesan Telegram ID {messageId} tidak ditemukan.");
        }

        string? dir = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 65536, useAsync: true);

        if (msg.media is MessageMediaDocument docMedia && docMedia.document is Document doc)
        {
            await _client.DownloadFileAsync(doc, fileStream, progress: (done, total) =>
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                onProgress(done, total);
            });
        }
        else if (msg.media is MessageMediaPhoto photoMedia && photoMedia.photo is Photo photo)
        {
            await _client.DownloadFileAsync(photo, fileStream, progress: (done, total) =>
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException();
                onProgress(done, total);
            });
        }
        else
        {
            throw new Exception("Pesan tidak memiliki berkas media yang dapat diunduh.");
        }
    }
}
