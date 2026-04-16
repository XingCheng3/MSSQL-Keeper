using Dapper;
using DBKeeper.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBKeeper.Data.Repositories;

public class BackupFileRepository : IBackupFileRepository
{
    private readonly string _connStr;

    public BackupFileRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    public async Task<List<BackupFile>> GetAllAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<BackupFile>("SELECT * FROM backup_files ORDER BY created_at DESC");
        return result.ToList();
    }

    public async Task<List<BackupFile>> GetByTaskIdAsync(int taskId)
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<BackupFile>(
            "SELECT * FROM backup_files WHERE task_id = @taskId ORDER BY created_at DESC", new { taskId });
        return result.ToList();
    }

    public async Task<int> InsertAsync(BackupFile file)
    {
        using var db = new SqliteConnection(_connStr);
        return await db.ExecuteScalarAsync<int>("""
            INSERT INTO backup_files (task_id, database_name, file_name, file_path, file_size_bytes, backup_type, created_at, expires_at, status)
            VALUES (@TaskId, @DatabaseName, @FileName, @FilePath, @FileSizeBytes, @BackupType, @CreatedAt, @ExpiresAt, @Status);
            SELECT last_insert_rowid();
            """, file);
    }

    public async Task UpdateStatusAsync(int id, string status, string? deletedAt = null)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync(
            "UPDATE backup_files SET status = @status, deleted_at = @deletedAt WHERE id = @id",
            new { id, status, deletedAt });
    }

    public async Task SetPinnedAsync(int id, bool pinned)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("UPDATE backup_files SET is_pinned = @pinned WHERE id = @id", new { id, pinned });
    }

    public async Task DeleteAsync(int id)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("DELETE FROM backup_files WHERE id = @id", new { id });
    }

    /// <summary>
    /// 获取指定目录下状态为 NORMAL/SIZE_ANOMALY 的备份文件（用于文件同步扫描）
    /// </summary>
    public async Task<List<BackupFile>> GetActiveByDirAsync(string directory)
    {
        // 确保目录以分隔符结尾，避免 "C:\Backup" 匹配到 "C:\BackupArchive\"
        var normalizedDir = directory.TrimEnd(System.IO.Path.DirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<BackupFile>("""
            SELECT * FROM backup_files 
            WHERE status IN ('NORMAL','SIZE_ANOMALY') AND file_path LIKE @prefix
            """, new { prefix = normalizedDir + "%" });
        return result.ToList();
    }

    /// <summary>
    /// 获取所有状态为 NORMAL/SIZE_ANOMALY 的备份文件
    /// </summary>
    public async Task<List<BackupFile>> GetAllActiveAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<BackupFile>("""
            SELECT * FROM backup_files 
            WHERE status IN ('NORMAL','SIZE_ANOMALY')
            ORDER BY created_at DESC
            """);
        return result.ToList();
    }
}
