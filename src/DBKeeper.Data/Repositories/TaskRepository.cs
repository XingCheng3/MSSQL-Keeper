using Dapper;
using DBKeeper.Core.Models;
using Microsoft.Data.Sqlite;

namespace DBKeeper.Data.Repositories;

public class TaskRepository : ITaskRepository
{
    private readonly string _connStr;

    public TaskRepository(string connectionString)
    {
        _connStr = connectionString;
    }

    public async Task<List<TaskItem>> GetAllAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<TaskItem>("SELECT * FROM tasks ORDER BY id");
        return result.ToList();
    }

    public async Task<List<TaskItem>> GetEnabledAsync()
    {
        using var db = new SqliteConnection(_connStr);
        var result = await db.QueryAsync<TaskItem>("SELECT * FROM tasks WHERE is_enabled = 1");
        return result.ToList();
    }

    public async Task<TaskItem?> GetByIdAsync(int id)
    {
        using var db = new SqliteConnection(_connStr);
        return await db.QueryFirstOrDefaultAsync<TaskItem>("SELECT * FROM tasks WHERE id = @id", new { id });
    }

    public async Task<int> InsertAsync(TaskItem task)
    {
        using var db = new SqliteConnection(_connStr);
        var now = DateTime.Now.ToString("O");
        return await db.ExecuteScalarAsync<int>("""
            INSERT INTO tasks (name, task_type, connection_id, is_enabled, schedule_type, schedule_config, task_config, next_run_at, created_at, updated_at)
            VALUES (@Name, @TaskType, @ConnectionId, @IsEnabled, @ScheduleType, @ScheduleConfig, @TaskConfig, @NextRunAt, @now, @now);
            SELECT last_insert_rowid();
            """, new { task.Name, task.TaskType, task.ConnectionId, task.IsEnabled, task.ScheduleType, task.ScheduleConfig, task.TaskConfig, task.NextRunAt, now });
    }

    public async Task UpdateAsync(TaskItem task)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("""
            UPDATE tasks SET name=@Name, task_type=@TaskType, connection_id=@ConnectionId, is_enabled=@IsEnabled,
                schedule_type=@ScheduleType, schedule_config=@ScheduleConfig, task_config=@TaskConfig, next_run_at=@NextRunAt, updated_at=@UpdatedAt
            WHERE id = @Id
            """, new { task.Name, task.TaskType, task.ConnectionId, task.IsEnabled, task.ScheduleType, task.ScheduleConfig, task.TaskConfig, task.NextRunAt, UpdatedAt = DateTime.Now.ToString("O"), task.Id });
    }

    public async Task DeleteAsync(int id)
    {
        using var db = new SqliteConnection(_connStr);
        await db.OpenAsync();
        using var tx = await db.BeginTransactionAsync();
        // 先解除子表的 FK 引用，保留备份记录和日志不丢失
        await db.ExecuteAsync("UPDATE backup_files SET task_id = 0 WHERE task_id = @id", new { id }, tx);
        await db.ExecuteAsync("UPDATE execution_logs SET task_id = 0 WHERE task_id = @id", new { id }, tx);
        await db.ExecuteAsync("DELETE FROM tasks WHERE id = @id", new { id }, tx);
        await tx.CommitAsync();
    }

    public async Task UpdateLastRunAsync(int id, string status, string? nextRunAt)
    {
        using var db = new SqliteConnection(_connStr);
        await db.ExecuteAsync("""
            UPDATE tasks SET last_run_at = @now, last_run_status = @status, next_run_at = @nextRunAt, updated_at = @now
            WHERE id = @id
            """, new { id, status, nextRunAt, now = DateTime.Now.ToString("O") });
    }

    public async Task<int> CountByConnectionIdAsync(int connectionId)
    {
        using var db = new SqliteConnection(_connStr);
        return await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tasks WHERE connection_id = @connectionId", new { connectionId });
    }
}
