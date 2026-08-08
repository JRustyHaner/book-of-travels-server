using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace BotMaster;

public record RoomEntry
{
    public required string Host { get; init; }        // IP the instance pinged from
    public required int Port { get; init; }           // room_port from Instance.Ping
    public int PlayerCount { get; set; }
    public DateTime LastPing { get; set; } = DateTime.UtcNow;
    public long ServerId { get; init; }
}

/// <summary>
/// Account persistence (SQLite) + the in-memory room registry.
/// Mirrors the client's local behavior: unknown credentials auto-create an account
/// (cf. Database.IsValidAccountLite in the decompiled client).
/// </summary>
public class Store
{
    private const string DefaultRegion = "eu-central-1";

    private readonly string _connString;
    private long _nextServerId = 1;
    private readonly ConcurrentDictionary<string, RoomEntry> _rooms = new();
    private readonly ConcurrentDictionary<string, (string email, string token, DateTime expiry)> _emailTokens = new();

    public Store(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connString = $"Data Source={dbPath}";
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS accounts (
                account_id    INTEGER PRIMARY KEY AUTOINCREMENT,
                email         TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                salt          TEXT NOT NULL,
                service_id    INTEGER NOT NULL DEFAULT 0,
                name          TEXT NOT NULL DEFAULT '',
                banned        INTEGER NOT NULL DEFAULT 0,
                banned_until  TEXT NOT NULL DEFAULT '',
                unlocks       TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connString);
        c.Open();
        return c;
    }

    /// <summary>Authenticate, auto-creating the account if missing. Returns (id, banned).</summary>
    public (long id, bool banned) Authenticate(string email, string password, ulong serviceId)
    {
        email = email.Trim().ToLowerInvariant();
        using var conn = Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT account_id, password_hash, salt, banned FROM accounts WHERE email=@e";
            cmd.Parameters.AddWithValue("@e", email);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var id = r.GetInt64(0);
                var hash = r.GetString(1);
                var salt = r.GetString(2);
                var banned = r.GetInt64(3) != 0;
                if (Verify(password, salt, hash)) return (id, banned);
                return (0, false);
            }
        }
        // auto-create (matches IsValidAccountLite)
        var (salt2, hash2) = Hash(password);
        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = "INSERT INTO accounts (email, password_hash, salt, service_id) VALUES (@e, @h, @s, @sid)";
            ins.Parameters.AddWithValue("@e", email);
            ins.Parameters.AddWithValue("@h", hash2);
            ins.Parameters.AddWithValue("@s", salt2);
            ins.Parameters.AddWithValue("@sid", (long)serviceId);
            ins.ExecuteNonQuery();
        }
        using (var sel = conn.CreateCommand())
        {
            sel.CommandText = "SELECT account_id FROM accounts WHERE email=@e";
            sel.Parameters.AddWithValue("@e", email);
            return ((long)sel.ExecuteScalar()!, false);
        }
    }

    public bool VerifyPassword(string email, string password)
    {
        email = email.Trim().ToLowerInvariant();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash, salt FROM accounts WHERE email=@e";
        cmd.Parameters.AddWithValue("@e", email);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        return Verify(password, r.GetString(1), r.GetString(0));
    }

    public bool ChangePassword(string email, string oldPassword, string newPassword)
    {
        if (!VerifyPassword(email, oldPassword)) return false;
        return ChangePasswordDirect(email, newPassword);
    }

    /// <summary>Change a password without checking the old one (email-token flow).</summary>
    public bool ChangePasswordDirect(string email, string newPassword)
    {
        var (salt, hash) = Hash(newPassword);
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET password_hash=@h, salt=@s WHERE email=@e";
        cmd.Parameters.AddWithValue("@h", hash);
        cmd.Parameters.AddWithValue("@s", salt);
        cmd.Parameters.AddWithValue("@e", email.Trim().ToLowerInvariant());
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool ChangeEmail(string oldEmail, string newEmail)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE accounts SET email=@n WHERE email=@o";
        cmd.Parameters.AddWithValue("@n", newEmail.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("@o", oldEmail.Trim().ToLowerInvariant());
        return cmd.ExecuteNonQuery() > 0;
    }

    // ----- email-verification token store (in-memory; private server never mails) -----

    public string CreateEmailToken(string email, string name)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _emailTokens[token] = (email.Trim().ToLowerInvariant(), token, DateTime.UtcNow.AddHours(24));
        return token;
    }

    public bool ConsumeEmailToken(string token, out string email)
    {
        email = "";
        if (!_emailTokens.TryRemove(token, out var e)) return false;
        if (e.expiry < DateTime.UtcNow) return false;
        email = e.email;
        return true;
    }

    // ----- room registry (fed by Instance.Ping heartbeats) -----

    public RoomEntry RegisterRoom(string ip, int port, int playerCount)
    {
        var key = $"{ip}:{port}";
        var room = _rooms.AddOrUpdate(key,
            _ => new RoomEntry { Host = ip, Port = port, PlayerCount = playerCount, ServerId = Interlocked.Increment(ref _nextServerId) },
            (_, existing) =>
            {
                existing.PlayerCount = playerCount;
                existing.LastPing = DateTime.UtcNow;
                return existing;
            });
        return room;
    }

    public List<RoomEntry> GetRooms(bool prune = true)
    {
        if (prune)
        {
            var cutoff = DateTime.UtcNow.AddSeconds(-30);
            foreach (var kv in _rooms)
                if (kv.Value.LastPing < cutoff) _rooms.TryRemove(kv.Key, out _);
        }
        return _rooms.Values.OrderBy(r => r.ServerId).ToList();
    }

    public string Region => DefaultRegion;

    private static (string salt, string hash) Hash(string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(salt + password));
        return (salt, Convert.ToHexString(hash));
    }

    private static bool Verify(string password, string salt, string hash)
    {
        var computed = SHA256.HashData(Encoding.UTF8.GetBytes(salt + password));
        return CryptographicOperations.FixedTimeEquals(computed, Convert.FromHexString(hash));
    }
}
