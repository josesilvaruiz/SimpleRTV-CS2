using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace SimpleRTV;

public class PlayerPrefsDb
{
    private readonly string _connectionString;
    private readonly ILogger _logger;

    public bool IsAvailable { get; private set; }

    public PlayerPrefsDb(string dbPath, ILogger logger)
    {
        _logger = logger;
        _connectionString = $"Data Source={dbPath}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            InitSchema();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            _logger.LogError("[SimpleRTV] SQLite unavailable — player chat-mode preferences will not be saved this session.");
            _logger.LogError("[SimpleRTV] Cause: {Err}", ex.Message);
            if (ex is DllNotFoundException || ex.InnerException is DllNotFoundException)
                _logger.LogError("[SimpleRTV] Native library e_sqlite3 not found. Make sure e_sqlite3.dll / libe_sqlite3.so is present in the plugin folder.");
        }
    }

    private void InitSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS rtv_player_prefs (
                steam_id TEXT PRIMARY KEY,
                chat_mode INTEGER NOT NULL DEFAULT 0
            )
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<bool> LoadChatModeAsync(string steamId)
    {
        if (!IsAvailable) return false;
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT chat_mode FROM rtv_player_prefs WHERE steam_id = $id";
            cmd.Parameters.AddWithValue("$id", steamId);
            var result = await cmd.ExecuteScalarAsync();
            return result is long val && val != 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[SimpleRTV] LoadChatMode failed: {Err}", ex.Message);
            return false;
        }
    }

    public async Task SaveChatModeAsync(string steamId, bool chatMode)
    {
        if (!IsAvailable) return;
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO rtv_player_prefs (steam_id, chat_mode) VALUES ($id, $mode)
                ON CONFLICT(steam_id) DO UPDATE SET chat_mode = excluded.chat_mode
                """;
            cmd.Parameters.AddWithValue("$id", steamId);
            cmd.Parameters.AddWithValue("$mode", chatMode ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[SimpleRTV] SaveChatMode failed: {Err}", ex.Message);
        }
    }
}
