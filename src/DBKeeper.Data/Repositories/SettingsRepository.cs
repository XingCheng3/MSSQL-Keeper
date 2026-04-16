using Dapper;
using DBKeeper.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBKeeper.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly string _connStr;

    public SettingsRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    public async Task<string?> GetAsync(string key)
    {
        using var db = new SqliteConnection(_connStr);
        return await db.ExecuteScalarAsync<string?>("SELECT value FROM settings WHERE key = @key", new { key });
    }

    public async Task SetAsync(string key, string value)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("""
            INSERT INTO settings (key, value, updated_at) VALUES (@key, @value, @now)
            ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = @now
            """, new { key, value, now = DateTime.Now.ToString("O") });
    }

    public async Task<List<AppSetting>> GetAllAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<AppSetting>("SELECT * FROM settings");
        return result.ToList();
    }
}
