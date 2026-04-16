using Dapper;
using DBKeeper.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBKeeper.Data.Repositories;

public class ConnectionRepository : IConnectionRepository
{
    private readonly string _connStr;

    public ConnectionRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    public async Task<List<Connection>> GetAllAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<Connection>("SELECT * FROM connections ORDER BY id");
        return result.ToList();
    }

    public async Task<Connection?> GetByIdAsync(int id)
    {
        using var db = new SqliteConnection(_connStr);
        return await db.QueryFirstOrDefaultAsync<Connection>("SELECT * FROM connections WHERE id = @id", new { id });
    }

    public async Task<int> InsertAsync(Connection conn)
    {
        using var db = new SqliteConnection(_connStr);
        var now = DateTime.Now.ToString("O");
        return await db.ExecuteScalarAsync<int>("""
            INSERT INTO connections (name, host, username, password, default_db, timeout_sec, trust_server_certificate, is_default, remark, created_at, updated_at)
            VALUES (@Name, @Host, @Username, @Password, @DefaultDb, @TimeoutSec, @TrustServerCertificate, @IsDefault, @Remark, @now, @now);
            SELECT last_insert_rowid();
            """, new { conn.Name, conn.Host, conn.Username, conn.Password, conn.DefaultDb, conn.TimeoutSec, conn.TrustServerCertificate, conn.IsDefault, conn.Remark, now });
    }

    public async Task UpdateAsync(Connection conn)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("""
            UPDATE connections SET name=@Name, host=@Host, username=@Username, password=@Password,
                default_db=@DefaultDb, timeout_sec=@TimeoutSec, trust_server_certificate=@TrustServerCertificate,
                remark=@Remark, updated_at=@UpdatedAt
            WHERE id = @Id
            """, new { conn.Name, conn.Host, conn.Username, conn.Password, conn.DefaultDb, conn.TimeoutSec, conn.TrustServerCertificate, conn.Remark, UpdatedAt = DateTime.Now.ToString("O"), conn.Id });
    }

    public async Task DeleteAsync(int id)
    {
        using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        using var tx = await db.BeginTransactionAsync();
        // 先解除 tasks 表的 FK 引用（保留任务但标记为无连接）
        await db.ExecuteAsync("UPDATE tasks SET connection_id = 0 WHERE connection_id = @id", new { id }, tx);
        await db.ExecuteAsync("DELETE FROM connections WHERE id = @id", new { id }, tx);
        await tx.CommitAsync();
    }

    /// <summary>
    /// 设为默认连接（先清除其他默认，再设置当前）
    /// </summary>
    public async Task SetDefaultAsync(int id)
    {
        using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        using var tx = await db.BeginTransactionAsync();
        await db.ExecuteAsync("UPDATE connections SET is_default = 0", transaction: tx);
        await db.ExecuteAsync("UPDATE connections SET is_default = 1 WHERE id = @id", new { id }, tx);
        await tx.CommitAsync();
    }
}
