using System.Text.Json;
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

    [Theory]
    [InlineData("DIFF")]
    [InlineData("FULL")]
    public async Task ExecuteTaskAsync_DirectorySyncTarget_ShouldNotCreateBackupFileRecord(string syncMode)
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(workspace.RootPath, "target");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");

        var taskId = await InsertDirectoryTaskAsync(workspace, sourceDir, targetDir, syncMode, retentionDays: 30);
        var scheduler = CreateScheduler(workspace);
        var task = await workspace.TaskRepository.GetByIdAsync(taskId);
        Assert.NotNull(task);

        await scheduler.ExecuteTaskAsync(task!, "MANUAL", new SchedulerService.RunningTaskState());

        var files = await workspace.BackupFileRepository.GetByTaskIdAsync(taskId);
        Assert.Empty(files);
    }

    [Fact]
    public async Task ExecuteTaskAsync_DirectoryArchive_ShouldSetExpiresAt()
    {
        using var workspace = new TestWorkspace();
        var sourceDir = Path.Combine(workspace.RootPath, "source");
        var targetDir = Path.Combine(workspace.RootPath, "target");
        Directory.CreateDirectory(sourceDir);
        await File.WriteAllTextAsync(Path.Combine(sourceDir, "a.txt"), "alpha");

        var taskId = await InsertDirectoryTaskAsync(workspace, sourceDir, targetDir, "ARCHIVE", retentionDays: 7);
        var scheduler = CreateScheduler(workspace);
        var task = await workspace.TaskRepository.GetByIdAsync(taskId);
        Assert.NotNull(task);

        await scheduler.ExecuteTaskAsync(task!, "MANUAL", new SchedulerService.RunningTaskState());

        var files = await workspace.BackupFileRepository.GetByTaskIdAsync(taskId);
        var backupFile = Assert.Single(files);
        Assert.Equal("DIR_ZIP", backupFile.BackupType);
        Assert.False(string.IsNullOrWhiteSpace(backupFile.ExpiresAt));
        Assert.True(File.Exists(backupFile.FilePath));
    }

    private static SchedulerService CreateScheduler(TestWorkspace workspace)
    {
        return new SchedulerService(
            workspace.TaskRepository,
            workspace.ConnectionRepository,
            workspace.ExecutionLogRepository,
            workspace.BackupFileRepository,
            workspace.SettingsRepository);
    }

    private static Task<int> InsertDirectoryTaskAsync(
        TestWorkspace workspace,
        string sourceDir,
        string targetDir,
        string syncMode,
        int retentionDays)
    {
        var config = new DirectorySyncConfig
        {
            SourceDir = sourceDir,
            TargetDir = targetDir,
            SyncMode = syncMode,
            ArchiveFormat = "ZIP",
            CompressionLevel = "BALANCED",
            FileNameTemplate = "{NAME}_{DATE}_{TIME}.{EXT}",
            RetentionDays = retentionDays,
            MinKeepCount = 3,
            IncludeSubdirectories = true,
            OverwriteChangedFiles = true,
            TimeoutSec = 60
        };

        return workspace.TaskRepository.InsertAsync(new TaskItem
        {
            Name = $"目录同步 {syncMode}",
            TaskType = "DIRECTORY_SYNC",
            IsEnabled = true,
            ScheduleType = "DAILY",
            ScheduleConfig = """{"time":"02:00"}""",
            TaskConfig = JsonSerializer.Serialize(config),
            CreatedAt = DateTime.Now.ToString("O"),
            UpdatedAt = DateTime.Now.ToString("O")
        });
    }
}
