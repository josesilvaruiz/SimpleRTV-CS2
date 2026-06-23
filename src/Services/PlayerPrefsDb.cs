using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace SimpleRTV;

public class PlayerPrefsDb
{
    private readonly string _connectionString;

    public PlayerPrefsDb(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connectionString = $"Data Source={dbPath}";
        InitSchema();
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
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT chat_mode FROM rtv_player_prefs WHERE steam_id = $id";
        cmd.Parameters.AddWithValue("$id", steamId);
        var result = await cmd.ExecuteScalarAsync();
        return result is long val && val != 0;
    }

    public async Task SaveChatModeAsync(string steamId, bool chatMode)
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
}
