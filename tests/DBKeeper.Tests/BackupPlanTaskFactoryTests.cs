using System.Text.Json;
using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;

namespace DBKeeper.Tests;

public class BackupPlanTaskFactoryTests
{
    [Fact]
    public void CreateTasks_ShouldCreateFullDiffAndCleanupTasks()
    {
        var tasks = BackupPlanTaskFactory.CreateTasks(new BackupPlanOptions
        {
            PlanName = "MES 数据库",
            ConnectionId = 7,
            DatabaseName = "MESDB",
            BackupDir = @"D:\Backup\MES",
            FullBackupDayOfWeek = 0,
            BackupTime = "02:00",
            RetentionDays = 30,
            MinKeepCount = 3,
            UseCompression = true,
            VerifyAfterBackup = true,
            CreateCleanupTask = true,
            CleanupTime = "03:00"
        });

        Assert.Equal(3, tasks.Count);

        var fullTask = tasks[0];
        Assert.Equal("MES 数据库 全量备份", fullTask.Name);
        Assert.Equal("BACKUP", fullTask.TaskType);
        Assert.Equal("WEEKLY", fullTask.ScheduleType);
        Assert.Equal(7, fullTask.ConnectionId);

        using var fullSchedule = JsonDocument.Parse(fullTask.ScheduleConfig);
        Assert.Equal("02:00", fullSchedule.RootElement.GetProperty("time").GetString());
        Assert.Equal(0, fullSchedule.RootElement.GetProperty("day_of_week").GetInt32());

        using var fullConfig = JsonDocument.Parse(fullTask.TaskConfig);
        Assert.Equal("FULL", fullConfig.RootElement.GetProperty("BackupType").GetString());
        Assert.Equal("{DB}_FULL_{DATE}_{TIME}.bak", fullConfig.RootElement.GetProperty("FileNameTemplate").GetString());
        Assert.True(fullConfig.RootElement.GetProperty("VerifyAfterBackup").GetBoolean());

        var diffTask = tasks[1];
        Assert.Equal("MES 数据库 差异备份", diffTask.Name);
        Assert.Equal("BACKUP", diffTask.TaskType);
        Assert.Equal("CRON", diffTask.ScheduleType);

        using var diffSchedule = JsonDocument.Parse(diffTask.ScheduleConfig);
        Assert.Equal("00 02 * * 1,2,3,4,5,6", diffSchedule.RootElement.GetProperty("cron_expression").GetString());

        using var diffConfig = JsonDocument.Parse(diffTask.TaskConfig);
        Assert.Equal("DIFF", diffConfig.RootElement.GetProperty("BackupType").GetString());

        var cleanupTask = tasks[2];
        Assert.Equal("MES 数据库 备份清理", cleanupTask.Name);
        Assert.Equal("BACKUP_CLEANUP", cleanupTask.TaskType);
        Assert.Equal("DAILY", cleanupTask.ScheduleType);

        using var cleanupConfig = JsonDocument.Parse(cleanupTask.TaskConfig);
        Assert.Equal(@"D:\Backup\MES", cleanupConfig.RootElement.GetProperty("TargetDir").GetString());
        Assert.Equal(30, cleanupConfig.RootElement.GetProperty("RetentionDays").GetInt32());
        Assert.Equal(3, cleanupConfig.RootElement.GetProperty("MinKeepCount").GetInt32());
    }

    [Fact]
    public void CreateTasks_ShouldSkipCleanupTask_WhenCleanupDisabled()
    {
        var tasks = BackupPlanTaskFactory.CreateTasks(new BackupPlanOptions
        {
            PlanName = "MES 数据库",
            ConnectionId = 7,
            DatabaseName = "MESDB",
            BackupDir = @"D:\Backup\MES",
            FullBackupDayOfWeek = 3,
            BackupTime = "23:30",
            RetentionDays = 14,
            MinKeepCount = 2,
            CreateCleanupTask = false
        });

        Assert.Equal(2, tasks.Count);

        using var diffSchedule = JsonDocument.Parse(tasks[1].ScheduleConfig);
        Assert.Equal("30 23 * * 0,1,2,4,5,6", diffSchedule.RootElement.GetProperty("cron_expression").GetString());
    }

    [Fact]
    public void CreateTasks_ShouldRejectInvalidTime()
    {
        var options = new BackupPlanOptions
        {
            PlanName = "MES 数据库",
            ConnectionId = 7,
            DatabaseName = "MESDB",
            BackupDir = @"D:\Backup\MES",
            FullBackupDayOfWeek = 0,
            BackupTime = "2点",
            RetentionDays = 30,
            MinKeepCount = 3
        };

        Assert.Throws<ArgumentException>(() => BackupPlanTaskFactory.CreateTasks(options));
    }
}
