using Dapper;
using DBKeeper.Core.Models;
using DBKeeper.Scheduling;
using DBKeeper.Tests.TestSupport;

namespace DBKeeper.Tests;

public class SchedulerServiceTests
{
    [Fact]
    public async Task ScheduleTaskAsync_ShouldOnlyUpdateNextRun()
    {
        using var workspace = new TestWorkspace();
        var taskId = await workspace.TaskRepository.InsertAsync(new TaskItem
        {
            Name = "每日任务",
            TaskType = "BACKUP",
            ConnectionId = null,
            IsEnabled = true,
            ScheduleType = "DAILY",
            ScheduleConfig = """{"time":"02:00"}""",
            TaskConfig = """{"DatabaseName":"MES_DB","BackupType":"FULL","BackupDir":"D:\\Backup","RetentionDays":30,"MinKeepCount":3,"UseCompression":true,"VerifyAfterBackup":false,"TimeoutSec":600}""",
            NextRunAt = null,
            CreatedAt = DateTime.Now.ToString("O"),
            UpdatedAt = DateTime.Now.ToString("O")
        });

        using var db = new Microsoft.Data.Sqlite.SqliteConnection(workspace.DbInitializer.ConnectionString);
        await db.OpenAsync();
        await db.ExecuteAsync("""
            UPDATE tasks SET last_run_at = @LastRunAt, last_run_status = @LastRunStatus WHERE id = @Id
            """, new
        {
            Id = taskId,
            LastRunAt = "2026-01-01T00:00:00.0000000+08:00",
            LastRunStatus = "SUCCESS"
        });

        var scheduler = new SchedulerService(
            workspace.TaskRepository,
            workspace.ConnectionRepository,
            workspace.ExecutionLogRepository,
            workspace.BackupFileRepository,
            workspace.SettingsRepository);

        var task = await workspace.TaskRepository.GetByIdAsync(taskId);
        Assert.NotNull(task);

        await scheduler.ScheduleTaskAsync(task!);

        var updatedTask = await workspace.TaskRepository.GetByIdAsync(taskId);
        Assert.NotNull(updatedTask);
        Assert.Equal("2026-01-01T00:00:00.0000000+08:00", updatedTask!.LastRunAt);
        Assert.Equal("SUCCESS", updatedTask.LastRunStatus);
        Assert.False(string.IsNullOrWhiteSpace(updatedTask.NextRunAt));
    }
}
