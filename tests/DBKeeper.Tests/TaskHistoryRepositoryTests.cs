using DBKeeper.Core.Models;
using DBKeeper.Tests.TestSupport;

namespace DBKeeper.Tests;

public class TaskHistoryRepositoryTests
{
    [Fact]
    public async Task GetByTaskIdAsync_ShouldReturnTaskLogsAndLinkedBackupFile()
    {
        using var workspace = new TestWorkspace();
        var taskId = await workspace.TaskRepository.InsertAsync(new TaskItem
        {
            Name = "归档测试",
            TaskType = "BACKUP",
            IsEnabled = true,
            ScheduleType = "DAILY",
            ScheduleConfig = """{"time":"02:00"}""",
            TaskConfig = """{}"""
        });
        var startedAt = DateTime.Now.ToString("O");
        var logId = await workspace.ExecutionLogRepository.InsertAsync(new ExecutionLog
        {
            TaskId = taskId,
            TaskName = "归档测试",
            TaskType = "BACKUP",
            TriggerType = "MANUAL",
            StartedAt = startedAt,
            Status = "RUNNING"
        });
        await workspace.ExecutionLogRepository.UpdateFinishAsync(logId, "SUCCESS", 1200, "完成", null);

        await workspace.BackupFileRepository.InsertAsync(new BackupFile
        {
            TaskId = taskId,
            ExecutionLogId = logId,
            SourceType = "DATABASE",
            SourceName = "MES",
            DatabaseName = "MES",
            FileName = "MES.bak",
            FilePath = Path.Combine(workspace.RootPath, "MES.bak"),
            BackupType = "FULL",
            CreatedAt = startedAt,
            Status = "NORMAL"
        });

        var logs = await workspace.ExecutionLogRepository.GetByTaskIdAsync(taskId);
        var files = await workspace.BackupFileRepository.GetByTaskIdAsync(taskId);

        Assert.Single(logs);
        Assert.Equal(logId, logs[0].Id);
        Assert.Single(files);
        Assert.Equal(logId, files[0].ExecutionLogId);
        Assert.Equal("MES", files[0].SourceName);
    }

    [Fact]
    public async Task GetByTaskAsync_ShouldReturnLegacyLogsByTaskNameAndType()
    {
        using var workspace = new TestWorkspace();
        var taskId = await workspace.TaskRepository.InsertAsync(new TaskItem
        {
            Name = "测试组合备份 差异备份",
            TaskType = "BACKUP",
            IsEnabled = true,
            ScheduleType = "DAILY",
            ScheduleConfig = """{"time":"02:00"}""",
            TaskConfig = """{}"""
        });

        var logId = await workspace.ExecutionLogRepository.InsertAsync(new ExecutionLog
        {
            TaskId = null,
            TaskName = "测试组合备份 差异备份",
            TaskType = "BACKUP",
            TriggerType = "SCHEDULED",
            StartedAt = DateTime.Now.ToString("O"),
            Status = "RUNNING"
        });
        await workspace.ExecutionLogRepository.UpdateFinishAsync(logId, "SUCCESS", 800, "差异备份完成", null);

        var logs = await workspace.ExecutionLogRepository.GetByTaskAsync(
            taskId,
            "测试组合备份 差异备份",
            "BACKUP");

        Assert.Single(logs);
        Assert.Equal(logId, logs[0].Id);
        Assert.Equal("差异备份完成", logs[0].Summary);
    }
}
