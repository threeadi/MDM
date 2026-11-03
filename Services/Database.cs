using System.IO;
using System.Text.Json;
using Mdm.Models;
using Microsoft.Data.Sqlite;

namespace Mdm.Services;

public class Database
{
    private readonly string _connectionString;

    public Database(string? customDbPath = null)
    {
        string dbPath = customDbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MDM",
            "mdm.db"
        );

        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        Logger.Info($"Inisialisasi basis data SQLite di: '{dbPath}'", "DATABASE");
        InitSchema();
    }

    private void InitSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS downloads (
                id TEXT PRIMARY KEY,
                url TEXT NOT NULL,
                filename TEXT NOT NULL,
                save_dir TEXT NOT NULL,
                file_path TEXT NOT NULL,
                total_bytes INTEGER,
                downloaded_bytes INTEGER NOT NULL,
                status TEXT NOT NULL,
                connections INTEGER NOT NULL,
                supports_range INTEGER NOT NULL,
                created_at TEXT NOT NULL,
                completed_at TEXT,
                error_message TEXT,
                referer TEXT,
                speed_limit_bps INTEGER,
                segments_json TEXT,
                category TEXT,
                source TEXT,
                telegram_channel_title TEXT,
                quality TEXT,
                is_hls INTEGER DEFAULT 0,
                ray_id TEXT
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS telegram_channels (
                id INTEGER PRIMARY KEY,
                title TEXT NOT NULL,
                username TEXT,
                chat_type TEXT NOT NULL,
                unread_count INTEGER DEFAULT 0
            );

            -- Clean up any legacy dummy / mock channels
            DELETE FROM telegram_channels WHERE title LIKE '%Cinema%' OR title LIKE '%Master Audio%' OR title LIKE '%OpenSource Software%';
        ";
        cmd.ExecuteNonQuery();

        // Safe migrations for existing databases
        string[] columnsToAdd = ["category TEXT", "source TEXT", "telegram_channel_title TEXT", "quality TEXT", "is_hls INTEGER DEFAULT 0", "ray_id TEXT"];
        foreach (var col in columnsToAdd)
        {
            try
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE downloads ADD COLUMN {col};";
                alterCmd.ExecuteNonQuery();
            }
            catch
            {
                // Column already exists, safe to ignore
            }
        }

        try
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE telegram_channels ADD COLUMN media_count INTEGER;";
            alterCmd.ExecuteNonQuery();
        }
        catch
        {
            // Column already exists, safe to ignore
        }
    }

    public List<DownloadTask> LoadAllTasks()
    {
        var list = new List<DownloadTask>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, url, filename, save_dir, file_path, total_bytes, downloaded_bytes, status, connections, supports_range, created_at, completed_at, error_message, referer, speed_limit_bps, segments_json, category, source, telegram_channel_title, quality, is_hls, ray_id FROM downloads ORDER BY created_at DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var task = new DownloadTask
            {
                Id = reader.GetString(0),
                Url = reader.GetString(1),
                Filename = reader.GetString(2),
                SaveDir = reader.GetString(3),
                FilePath = reader.GetString(4),
                TotalBytes = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                DownloadedBytes = reader.GetInt64(6),
                Status = Enum.TryParse<DownloadStatus>(reader.GetString(7), out var st) ? st : DownloadStatus.Paused,
                Connections = reader.GetInt32(8),
                SupportsRange = reader.GetInt32(9) == 1,
                CreatedAt = reader.GetString(10),
                CompletedAt = reader.IsDBNull(11) ? null : reader.GetString(11),
                ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
                Referer = reader.IsDBNull(13) ? null : reader.GetString(13),
                SpeedLimitBps = reader.IsDBNull(14) ? null : reader.GetInt64(14)
            };

            // If downloading upon start, set to paused
            if (task.Status == DownloadStatus.Downloading)
            {
                task.Status = DownloadStatus.Paused;
            }

            if (!reader.IsDBNull(15))
            {
                var json = reader.GetString(15);
                try
                {
                    task.Segments = JsonSerializer.Deserialize<List<DownloadSegment>>(json) ?? [];
                }
                catch { }
            }

            // Category, Source, TelegramChannelTitle
            if (reader.FieldCount > 16 && !reader.IsDBNull(16))
            {
                var cat = reader.GetString(16);
                if (!string.IsNullOrWhiteSpace(cat) && !cat.Equals("Umum", StringComparison.OrdinalIgnoreCase))
                {
                    task.Category = cat;
                }
                else
                {
                    task.Category = CategoryHelper.DetectCategory(task.Filename);
                }
            }
            else
            {
                task.Category = CategoryHelper.DetectCategory(task.Filename);
            }

            if (reader.FieldCount > 17 && !reader.IsDBNull(17))
            {
                task.Source = reader.GetString(17);
            }

            if (reader.FieldCount > 18 && !reader.IsDBNull(18))
            {
                task.TelegramChannelTitle = reader.GetString(18);
            }

            if (reader.FieldCount > 19 && !reader.IsDBNull(19))
            {
                task.Quality = reader.GetString(19);
            }

            if (reader.FieldCount > 20 && !reader.IsDBNull(20))
            {
                task.IsHls = reader.GetInt32(20) == 1;
            }

            if (reader.FieldCount > 21 && !reader.IsDBNull(21))
            {
                task.RayId = reader.GetString(21);
            }

            list.Add(task);
        }

        return list;
    }

    public void UpsertTask(DownloadTask task)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO downloads (id, url, filename, save_dir, file_path, total_bytes, downloaded_bytes, status, connections, supports_range, created_at, completed_at, error_message, referer, speed_limit_bps, segments_json, category, source, telegram_channel_title, quality, is_hls, ray_id)
            VALUES (@id, @url, @filename, @save_dir, @file_path, @total_bytes, @downloaded_bytes, @status, @connections, @supports_range, @created_at, @completed_at, @error_message, @referer, @speed_limit_bps, @segments_json, @category, @source, @telegram_channel_title, @quality, @is_hls, @ray_id)
            ON CONFLICT(id) DO UPDATE SET
                url = excluded.url,
                filename = excluded.filename,
                save_dir = excluded.save_dir,
                file_path = excluded.file_path,
                total_bytes = excluded.total_bytes,
                downloaded_bytes = excluded.downloaded_bytes,
                status = excluded.status,
                connections = excluded.connections,
                supports_range = excluded.supports_range,
                completed_at = excluded.completed_at,
                error_message = excluded.error_message,
                referer = excluded.referer,
                speed_limit_bps = excluded.speed_limit_bps,
                segments_json = excluded.segments_json,
                category = excluded.category,
                source = excluded.source,
                telegram_channel_title = excluded.telegram_channel_title,
                quality = excluded.quality,
                is_hls = excluded.is_hls,
                ray_id = excluded.ray_id;
        ";

        cmd.Parameters.AddWithValue("@id", task.Id);
        cmd.Parameters.AddWithValue("@url", task.Url);
        cmd.Parameters.AddWithValue("@filename", task.Filename);
        cmd.Parameters.AddWithValue("@save_dir", task.SaveDir);
        cmd.Parameters.AddWithValue("@file_path", task.FilePath);
        cmd.Parameters.AddWithValue("@total_bytes", (object?)task.TotalBytes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@downloaded_bytes", task.DownloadedBytes);
        cmd.Parameters.AddWithValue("@status", task.Status.ToString());
        cmd.Parameters.AddWithValue("@connections", task.Connections);
        cmd.Parameters.AddWithValue("@supports_range", task.SupportsRange ? 1 : 0);
        cmd.Parameters.AddWithValue("@created_at", task.CreatedAt);
        cmd.Parameters.AddWithValue("@completed_at", (object?)task.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_message", (object?)task.ErrorMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@referer", (object?)task.Referer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@speed_limit_bps", (object?)task.SpeedLimitBps ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@segments_json", JsonSerializer.Serialize(task.Segments));
        cmd.Parameters.AddWithValue("@category", (object?)task.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@source", (object?)task.Source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telegram_channel_title", (object?)task.TelegramChannelTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@quality", (object?)task.Quality ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@is_hls", task.IsHls ? 1 : 0);
        cmd.Parameters.AddWithValue("@ray_id", (object?)task.RayId ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        Logger.Debug($"Upsert task '{task.Id}' ({task.Filename}, status={task.Status})", "DATABASE");
    }

    public DownloadTask? GetTask(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, url, filename, save_dir, file_path, total_bytes, downloaded_bytes, status, connections, supports_range, created_at, completed_at, error_message, referer, speed_limit_bps, segments_json, category, source, telegram_channel_title, quality, is_hls, ray_id FROM downloads WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var task = new DownloadTask
        {
            Id = reader.GetString(0),
            Url = reader.GetString(1),
            Filename = reader.GetString(2),
            SaveDir = reader.GetString(3),
            FilePath = reader.GetString(4),
            TotalBytes = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            DownloadedBytes = reader.GetInt64(6),
            Status = Enum.TryParse<DownloadStatus>(reader.GetString(7), out var st) ? st : DownloadStatus.Paused,
            Connections = reader.GetInt32(8),
            SupportsRange = reader.GetInt32(9) == 1,
            CreatedAt = reader.GetString(10),
            CompletedAt = reader.IsDBNull(11) ? null : reader.GetString(11),
            ErrorMessage = reader.IsDBNull(12) ? null : reader.GetString(12),
            Referer = reader.IsDBNull(13) ? null : reader.GetString(13),
            SpeedLimitBps = reader.IsDBNull(14) ? null : reader.GetInt64(14)
        };

        if (!reader.IsDBNull(15))
        {
            var json = reader.GetString(15);
            try
            {
                task.Segments = JsonSerializer.Deserialize<List<DownloadSegment>>(json) ?? [];
            }
            catch { }
        }

        if (reader.FieldCount > 16 && !reader.IsDBNull(16))
        {
            var cat = reader.GetString(16);
            if (!string.IsNullOrWhiteSpace(cat) && !cat.Equals("Umum", StringComparison.OrdinalIgnoreCase))
            {
                task.Category = cat;
            }
            else
            {
                task.Category = CategoryHelper.DetectCategory(task.Filename);
            }
        }
        else
        {
            task.Category = CategoryHelper.DetectCategory(task.Filename);
        }

        if (reader.FieldCount > 17 && !reader.IsDBNull(17))
        {
            task.Source = reader.GetString(17);
        }

        if (reader.FieldCount > 18 && !reader.IsDBNull(18))
        {
            task.TelegramChannelTitle = reader.GetString(18);
        }

        if (reader.FieldCount > 19 && !reader.IsDBNull(19))
        {
            task.Quality = reader.GetString(19);
        }

        if (reader.FieldCount > 20 && !reader.IsDBNull(20))
        {
            task.IsHls = reader.GetInt32(20) == 1;
        }

        if (reader.FieldCount > 21 && !reader.IsDBNull(21))
        {
            task.RayId = reader.GetString(21);
        }

        return task;
    }

    public void UpdateTaskSpeedLimit(string id, long? speedLimitBps)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE downloads SET speed_limit_bps = @speed_limit_bps WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@speed_limit_bps", (object?)speedLimitBps ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public HashSet<string> GetAllDownloadUrls()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT url FROM downloads";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    set.Add(reader.GetString(0));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"GetAllDownloadUrls error: {ex.Message}");
        }
        return set;
    }

    public string? GetSetting(string key, string? defaultValue = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);

        var val = cmd.ExecuteScalar();
        return val != null && val != DBNull.Value ? val.ToString() : defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
        ";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
        Logger.Debug($"Simpan setting '{key}' = '{value}'", "DATABASE");
    }

    public void DeleteTask(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM downloads WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        Logger.Info($"Menghapus task '{id}' dari basis data", "DATABASE");
    }

    public List<TelegramChannelInfo> GetTelegramChannels()
    {
        var list = new List<TelegramChannelInfo>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, username, chat_type, unread_count, media_count FROM telegram_channels ORDER BY title ASC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TelegramChannelInfo
            {
                Id = reader.GetInt64(0),
                Title = reader.GetString(1),
                Username = reader.IsDBNull(2) ? null : reader.GetString(2),
                ChatType = reader.GetString(3),
                UnreadCount = reader.GetInt32(4),
                MediaCount = reader.IsDBNull(5) ? null : reader.GetInt32(5)
            });
        }

        return list;
    }

    public void SaveTelegramChannel(TelegramChannelInfo channel)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO telegram_channels (id, title, username, chat_type, unread_count, media_count)
            VALUES (@id, @title, @username, @chat_type, @unread_count, @media_count)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                username = excluded.username,
                chat_type = excluded.chat_type,
                unread_count = excluded.unread_count,
                media_count = COALESCE(excluded.media_count, telegram_channels.media_count);
        ";
        cmd.Parameters.AddWithValue("@id", channel.Id);
        cmd.Parameters.AddWithValue("@title", channel.Title);
        cmd.Parameters.AddWithValue("@username", (object?)channel.Username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@chat_type", channel.ChatType);
        cmd.Parameters.AddWithValue("@unread_count", channel.UnreadCount);
        cmd.Parameters.AddWithValue("@media_count", (object?)channel.MediaCount ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void UpdateTelegramChannelMediaCount(long id, int count)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE telegram_channels SET media_count = @count WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@count", count);
        cmd.ExecuteNonQuery();
    }

    public void DeleteTelegramChannel(long id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM telegram_channels WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }
}

