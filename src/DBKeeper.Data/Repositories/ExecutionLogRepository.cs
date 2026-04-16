using Dapper;
using DBKeeper.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBKeeper.Data.Repositories;

public class ExecutionLogRepository : IExecutionLogRepository
{
    private readonly string _connStr;

    public ExecutionLogRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    public async Task<int> InsertAsync(ExecutionLog log)
    {
        using var db = new SqliteConnection(_connStr);
        return await db.ExecuteScalarAsync<int>("""
            INSERT INTO execution_logs (task_id, task_name, task_type, trigger_type, started_at, status)
            VALUES (@TaskId, @TaskName, @TaskType, @TriggerType, @StartedAt, @Status);
            SELECT last_insert_rowid();
            """, log);
    }

    public async Task UpdateFinishAsync(int id, string status, int durationMs, string? summary, string? errorDetail)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("""
            UPDATE execution_logs SET finished_at = @now, status = @status, duration_ms = @durationMs,
                summary = @summary, error_detail = @errorDetail
            WHERE id = @id
            """, new { id, status, durationMs, summary, errorDetail, now = DateTime.Now.ToString("O") });
    }

    /// <summary>
    /// 分页查询，支持按任务名、状态、时间范围过滤
    /// </summary>
    public async Task<(List<ExecutionLog> Items, int Total)> GetPagedAsync(
        int page, int pageSize, string? taskName = null, string? status = null, string? startFrom = null, string? startTo = null)
    {
        using var db = new SqliteConnection(_connStr);

        var where = "WHERE 1=1";
        if (!string.IsNullOrEmpty(taskName)) where += " AND task_name LIKE @taskName";
        if (!string.IsNullOrEmpty(status)) where += " AND status = @status";
        if (!string.IsNullOrEmpty(startFrom)) where += " AND started_at >= @startFrom";
        if (!string.IsNullOrEmpty(startTo)) where += " AND started_at <= @startTo";

        var param = new { taskName = $"%{taskName}%", status, startFrom, startTo };

        var total = await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM execution_logs {where}", param);
        var items = await db.QueryAsync<ExecutionLog>(
            $"SELECT * FROM execution_logs {where} ORDER BY started_at DESC LIMIT @pageSize OFFSET @offset",
            new { taskName = $"%{taskName}%", status, startFrom, startTo, pageSize, offset = (page - 1) * pageSize });

        return (items.ToList(), total);
    }

    public async Task<List<ExecutionLog>> GetRecentAsync(int count)
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<ExecutionLog>(
            "SELECT * FROM execution_logs ORDER BY started_at DESC LIMIT @count", new { count });
        return result.ToList();
    }

    public async Task CleanupAsync(int retentionDays)
    {
        using var db = new SqliteConnection(_connStr);
        var cutoff = DateTime.Now.AddDays(-retentionDays).ToString("O");
        await db.ExecuteAsync("DELETE FROM execution_logs WHERE started_at < @cutoff", new { cutoff });
    }

    public async Task ClearAllAsync()
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("DELETE FROM execution_logs");
    }

    public async Task<int> CountTodayAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var today = DateTime.Today.ToString("O");
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM execution_logs WHERE started_at >= @today", new { today });
    }

    public async Task<int> CountRecent24hFailedAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var cutoff = DateTime.Now.AddHours(-24).ToString("O");
        return await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM execution_logs WHERE started_at >= @cutoff AND status = 'FAILED'", new { cutoff });
    }
}
