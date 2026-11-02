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

    public string SessionDir => _sessionDir;
    public string SessionPath => _sessionPath;

    private WTelegram.Client? _client;
    private string? _currentPhoneNumber;
    private string? _currentOtpCode;
    private string? _currentPassword;

    public event Action<TelegramAuthStatus>? OnAuthStatusChanged;

    public TelegramAuthStatus AuthStatus { get; private set; } = new();

    public TelegramService(Database database, string? customSessionDir = null)
    {
        _database = database;
        _sessionDir = !string.IsNullOrWhiteSpace(customSessionDir)
            ? customSessionDir
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MDM",
                "telegram"
            );

        if (!Directory.Exists(_sessionDir))
        {
            Directory.CreateDirectory(_sessionDir);
        }

        _sessionPath = Path.Combine(_sessionDir, "mdm_telegram.session");

        // Immediately populate cached auth status if session exists
        if (File.Exists(_sessionPath))
        {
            string? dispName = _database.GetSetting("tg_display_name");
            string? phone = _database.GetSetting("tg_phone_number");
            string? user = _database.GetSetting("tg_username");
            string? uidStr = _database.GetSetting("tg_user_id");

            AuthStatus = new TelegramAuthStatus
            {
                IsAuthenticated = true,
                PhoneNumber = phone,
                Username = user,
                DisplayName = !string.IsNullOrWhiteSpace(dispName) ? dispName : (!string.IsNullOrWhiteSpace(user) ? $"@{user.TrimStart('@')}" : (phone ?? "User")),
                UserId = long.TryParse(uidStr, out var uid) ? uid : null
            };
        }
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
                OnAuthStatusChanged?.Invoke(AuthStatus);
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

                _database.SetSetting("tg_user_id", user.id.ToString());
                if (!string.IsNullOrEmpty(user.phone)) _database.SetSetting("tg_phone_number", user.phone);
                if (!string.IsNullOrEmpty(user.username)) _database.SetSetting("tg_username", user.username);
                if (!string.IsNullOrEmpty(AuthStatus.DisplayName)) _database.SetSetting("tg_display_name", AuthStatus.DisplayName);

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

            // Only revoke auth if Telegram explicitly said the session is invalid / revoked
            bool isRevoked = ex.Message.Contains("AUTH_KEY_UNREGISTERED", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("SESSION_REVOKED", StringComparison.OrdinalIgnoreCase) ||
                             ex.Message.Contains("USER_DEACTIVATED", StringComparison.OrdinalIgnoreCase);

            if (isRevoked)
            {
                AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = ex.Message };
            }
            else
            {
                // Temporary network error, socket exception, file access contention, or offline:
                // Keep the cached authenticated status so the user remains logged in
                string? dispName = _database.GetSetting("tg_display_name");
                string? phone = _database.GetSetting("tg_phone_number");
                string? user = _database.GetSetting("tg_username");
                string? uidStr = _database.GetSetting("tg_user_id");

                AuthStatus = new TelegramAuthStatus
                {
                    IsAuthenticated = File.Exists(_sessionPath),
                    PhoneNumber = phone,
                    Username = user,
                    DisplayName = !string.IsNullOrWhiteSpace(dispName) ? dispName : (!string.IsNullOrWhiteSpace(user) ? $"@{user.TrimStart('@')}" : (phone ?? "User")),
                    UserId = long.TryParse(uidStr, out var uid) ? uid : null,
                    ErrorMessage = ex.Message
                };
            }
        }

        OnAuthStatusChanged?.Invoke(AuthStatus);
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

                if (user != null)
                {
                    _database.SetSetting("tg_user_id", user.id.ToString());
                    if (!string.IsNullOrEmpty(user.username)) _database.SetSetting("tg_username", user.username);
                    if (!string.IsNullOrEmpty(AuthStatus.DisplayName)) _database.SetSetting("tg_display_name", AuthStatus.DisplayName);
                }

                Logger.Info($"Login Telegram sukses: {AuthStatus.DisplayName}");
            }

            OnAuthStatusChanged?.Invoke(AuthStatus);
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

                if (user != null)
                {
                    _database.SetSetting("tg_user_id", user.id.ToString());
                    if (!string.IsNullOrEmpty(user.username)) _database.SetSetting("tg_username", user.username);
                    if (!string.IsNullOrEmpty(AuthStatus.DisplayName)) _database.SetSetting("tg_display_name", AuthStatus.DisplayName);
                }

                Logger.Info($"Verifikasi 2FA Telegram sukses: {AuthStatus.DisplayName}");
            }
            else
            {
                AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = "PASSWORD_HASH_INVALID" };
            }

            OnAuthStatusChanged?.Invoke(AuthStatus);
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
            OnAuthStatusChanged?.Invoke(AuthStatus);
            return AuthStatus;
        }
        catch (Exception ex)
        {
            Logger.Error("Gagal verifikasi password 2FA Telegram", ex);
            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false, ErrorMessage = ex.Message };
            OnAuthStatusChanged?.Invoke(AuthStatus);
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
            _database.SetSetting("tg_user_id", "");
            _database.SetSetting("tg_username", "");
            _database.SetSetting("tg_display_name", "");
            _currentPhoneNumber = null;
            _currentOtpCode = null;
            _currentPassword = null;

            AuthStatus = new TelegramAuthStatus { IsAuthenticated = false };
            OnAuthStatusChanged?.Invoke(AuthStatus);
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

            // 1. Channels & Supergroups / Groups
            foreach (var (peerId, chat) in dialogs.chats)
            {
                if (!chat.IsActive) continue;

                bool isChannel = chat is Channel ch && ch.IsChannel;
                string? username = chat is Channel ch2 ? ch2.username : null;

                var info = new TelegramChannelInfo
                {
                    Id = peerId,
                    Title = chat.Title ?? "Untitled Chat",
                    Username = username,
                    ChatType = isChannel ? "channel" : "group"
                };
                list.Add(info);
                _database.SaveTelegramChannel(info);
            }

            // 2. Personal Chats, Contacts & Saved Messages
            foreach (var (userId, user) in dialogs.users)
            {
                if (!user.IsActive) continue;

                string fullName = $"{user.first_name} {user.last_name}".Trim();
                if (string.IsNullOrEmpty(fullName)) fullName = user.username ?? $"User {userId}";

                var info = new TelegramChannelInfo
                {
                    Id = userId,
                    Title = user.id == _client.UserId ? "Pesan Tersimpan (Saved Messages)" : fullName,
                    Username = user.username,
                    ChatType = user.id == _client.UserId ? "saved" : "user"
                };
                list.Add(info);
                _database.SaveTelegramChannel(info);
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

            // 0. Saved Messages check
            if (clean.Equals("me", StringComparison.OrdinalIgnoreCase) || 
                clean.Equals("saved", StringComparison.OrdinalIgnoreCase) || 
                clean.Contains("Pesan Tersimpan", StringComparison.OrdinalIgnoreCase))
            {
                peer = _client.User;
                chatTitle = "Pesan Tersimpan (Saved Messages)";
            }
            // 1. Numeric ID lookup
            else if (long.TryParse(clean, out long numericId))
            {
                var dialogs = await _client.Messages_GetAllDialogs();
                if (dialogs.chats.TryGetValue(numericId, out var chat))
                {
                    peer = chat;
                    chatTitle = chat.Title;
                }
                else if (dialogs.users.TryGetValue(numericId, out var user))
                {
                    peer = user;
                    chatTitle = $"{user.first_name} {user.last_name}".Trim();
                }
            }
            else
            {
                // 2. Username or Link resolution via Contacts_ResolveUsername
                if (chatQuery.Contains("t.me/") || chatQuery.StartsWith("@"))
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
                    catch { }
                }

                // 3. Search in user's active dialogs (Channels, Groups, Users, Contacts)
                if (peer == null)
                {
                    var dialogs = await _client.Messages_GetAllDialogs();

                    // Search chats (Channels & Groups)
                    var chatMatch = dialogs.chats.Values.FirstOrDefault(c =>
                        string.Equals(c.Title, clean, StringComparison.OrdinalIgnoreCase) ||
                        (c is Channel ch && string.Equals(ch.username, clean, StringComparison.OrdinalIgnoreCase)) ||
                        c.Title.Contains(clean, StringComparison.OrdinalIgnoreCase));

                    if (chatMatch != null)
                    {
                        peer = chatMatch;
                        chatTitle = chatMatch.Title;
                    }
                    else
                    {
                        // Search users (Contacts & DMs)
                        var userMatch = dialogs.users.Values.FirstOrDefault(u =>
                            string.Equals(u.username, clean, StringComparison.OrdinalIgnoreCase) ||
                            $"{u.first_name} {u.last_name}".Contains(clean, StringComparison.OrdinalIgnoreCase) ||
                            (u.first_name != null && u.first_name.Contains(clean, StringComparison.OrdinalIgnoreCase)) ||
                            (u.last_name != null && u.last_name.Contains(clean, StringComparison.OrdinalIgnoreCase)));

                        if (userMatch != null)
                        {
                            peer = userMatch;
                            chatTitle = $"{userMatch.first_name} {userMatch.last_name}".Trim();
                            if (string.IsNullOrEmpty(chatTitle)) chatTitle = userMatch.username ?? "Contact";
                        }
                    }

                    // 4. Fallback to Contacts_ResolveUsername if still not found in dialogs
                    if (peer == null)
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
                        catch { }
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

            // Retrieve messages with server-side media filter if specific filter requested (parity with my-own-idm)
            Messages_MessagesBase messagesResult;
            string filterLower = (mediaFilter ?? "all").ToLowerInvariant();
            MessagesFilter? serverFilter = filterLower switch
            {
                "video" => new InputMessagesFilterVideo(),
                "photo" => new InputMessagesFilterPhotos(),
                "document" => new InputMessagesFilterDocument(),
                "audio" => new InputMessagesFilterMusic(),
                "gif" => new InputMessagesFilterGif(),
                _ => null
            };

            if (serverFilter != null)
            {
                try
                {
                    messagesResult = await _client.Messages_Search(peer, q: "", filter: serverFilter, limit: limit);
                }
                catch
                {
                    messagesResult = await _client.Messages_GetHistory(peer, limit: limit);
                }
            }
            else
            {
                messagesResult = await _client.Messages_GetHistory(peer, limit: limit);
            }

            foreach (var msgBase in messagesResult.Messages)
            {
                if (msgBase is not Message msg) continue;

                if (msg.media is MessageMediaDocument docMedia && docMedia.document is Document doc)
                {
                    string filename = doc.Filename;
                    string mime = doc.mime_type ?? "application/octet-stream";
                    string detectedType = "document";
                    string? resolution = null;
                    string? duration = null;
                    bool isAnimated = false;

                    if (doc.attributes != null)
                    {
                        foreach (var attr in doc.attributes)
                        {
                            if (attr is DocumentAttributeVideo vidAttr)
                            {
                                detectedType = "video";
                                resolution = $"{vidAttr.w}x{vidAttr.h}";
                                var durSec = (int)vidAttr.duration;
                                duration = durSec >= 3600
                                    ? TimeSpan.FromSeconds(durSec).ToString(@"hh\:mm\:ss")
                                    : TimeSpan.FromSeconds(durSec).ToString(@"mm\:ss");
                            }
                            else if (attr is DocumentAttributeAudio audAttr)
                            {
                                detectedType = "audio";
                                var durSec = (int)audAttr.duration;
                                duration = TimeSpan.FromSeconds(durSec).ToString(@"mm\:ss");
                            }
                            else if (attr is DocumentAttributeAnimated)
                            {
                                isAnimated = true;
                                detectedType = "gif";
                            }
                            else if (attr is DocumentAttributeFilename fnAttr && string.IsNullOrEmpty(filename))
                            {
                                filename = fnAttr.file_name;
                            }
                        }
                    }

                    if (isAnimated || mime.Contains("gif", StringComparison.OrdinalIgnoreCase))
                    {
                        detectedType = "gif";
                    }
                    else if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
                    {
                        detectedType = "video";
                    }
                    else if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    {
                        detectedType = "audio";
                    }
                    else if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        detectedType = "photo";
                    }

                    if (filterLower != "all" && !filterLower.Equals(detectedType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(filename))
                    {
                        string ext = detectedType switch
                        {
                            "video" => ".mp4",
                            "gif" => ".mp4",
                            "audio" => ".mp3",
                            "photo" => ".jpg",
                            _ => ".dat"
                        };
                        filename = $"telegram_{msg.id}{ext}";
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
                        MediaType = detectedType,
                        Resolution = resolution,
                        DurationText = duration,
                        CreatedAt = msg.date,
                        TgUrl = $"tg://chat/{peer.ID}/msg/{msg.id}"
                    });
                }
                else if (msg.media is MessageMediaPhoto photoMedia && photoMedia.photo is Photo photo)
                {
                    if (filterLower != "all" && filterLower != "photo") continue;

                    long photoSize = 0;
                    string? resolution = null;

                    if (photo.sizes != null)
                    {
                        foreach (var size in photo.sizes)
                        {
                            if (size is PhotoSize ps)
                            {
                                if (ps.size > photoSize)
                                {
                                    photoSize = ps.size;
                                    resolution = $"{ps.w}x{ps.h}";
                                }
                            }
                            else if (size is PhotoSizeProgressive psp)
                            {
                                if (psp.sizes.Length > 0 && psp.sizes[^1] > photoSize)
                                {
                                    photoSize = psp.sizes[^1];
                                    resolution = $"{psp.w}x{psp.h}";
                                }
                            }
                        }
                    }

                    results.Add(new TelegramMediaItem
                    {
                        MessageId = msg.id,
                        ChatId = peer.ID,
                        ChatTitle = chatTitle,
                        SenderName = chatTitle,
                        Filename = $"photo_{msg.id}.jpg",
                        FileSize = photoSize,
                        MimeType = "image/jpeg",
                        MediaType = "photo",
                        Resolution = resolution,
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
