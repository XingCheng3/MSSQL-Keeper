using System.Text.Json;
using DBKeeper.Core.Models;

namespace DBKeeper.Core.Helpers;

/// <summary>将组合备份策略转换为现有定时任务</summary>
public static class BackupPlanTaskFactory
{
    public static IReadOnlyList<TaskItem> CreateTasks(BackupPlanOptions options)
    {
        Validate(options);

        var now = DateTime.Now.ToString("O");
        var tasks = new List<TaskItem>
        {
            CreateBackupTask(
                name: $"{options.PlanName} 全量备份",
                backupType: "FULL",
                scheduleType: "WEEKLY",
                scheduleConfig: JsonSerializer.Serialize(new
                {
                    time = options.BackupTime.Trim(),
                    day_of_week = options.FullBackupDayOfWeek
                }),
                options,
                now)
        };

        tasks.Add(CreateBackupTask(
            name: $"{options.PlanName} 差异备份",
            backupType: "DIFF",
            scheduleType: "CRON",
            scheduleConfig: JsonSerializer.Serialize(new
            {
                cron_expression = BuildDiffCron(options.BackupTime.Trim(), options.FullBackupDayOfWeek)
            }),
            options,
            now));

        if (options.CreateCleanupTask)
        {
            tasks.Add(new TaskItem
            {
                Name = $"{options.PlanName} 备份清理",
                TaskType = "BACKUP_CLEANUP",
                ConnectionId = options.ConnectionId,
                IsEnabled = true,
                ScheduleType = "DAILY",
                ScheduleConfig = JsonSerializer.Serialize(new { time = options.CleanupTime.Trim() }),
                TaskConfig = JsonSerializer.Serialize(new
                {
                    TargetDir = options.BackupDir.Trim(),
                    RetentionDays = options.RetentionDays,
                    MinKeepCount = options.MinKeepCount
                }),
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        return tasks;
    }

    private static TaskItem CreateBackupTask(
        string name,
        string backupType,
        string scheduleType,
        string scheduleConfig,
        BackupPlanOptions options,
        string now)
    {
        return new TaskItem
        {
            Name = name,
            TaskType = "BACKUP",
            ConnectionId = options.ConnectionId,
            IsEnabled = true,
            ScheduleType = scheduleType,
            ScheduleConfig = scheduleConfig,
            TaskConfig = JsonSerializer.Serialize(new
            {
                DatabaseName = options.DatabaseName.Trim(),
                BackupType = backupType,
                BackupDir = options.BackupDir.Trim(),
                FileNameTemplate = $"{{DB}}_{backupType}_{{DATE}}_{{TIME}}.bak",
                RetentionDays = options.RetentionDays,
                MinKeepCount = options.MinKeepCount,
                UseCompression = options.UseCompression,
                VerifyAfterBackup = options.VerifyAfterBackup,
                TimeoutSec = 600
            }),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private static string BuildDiffCron(string time, int fullBackupDayOfWeek)
    {
        var parts = time.Split(':', StringSplitOptions.TrimEntries);
        var diffDays = Enumerable.Range(0, 7)
            .Where(day => day != fullBackupDayOfWeek)
            .Select(day => day.ToString());

        return $"{parts[1]} {parts[0]} * * {string.Join(",", diffDays)}";
    }

    private static void Validate(BackupPlanOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PlanName))
            throw new ArgumentException("策略名称不能为空", nameof(options));
        if (options.ConnectionId <= 0)
            throw new ArgumentException("连接不能为空", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DatabaseName))
            throw new ArgumentException("数据库名不能为空", nameof(options));
        if (string.IsNullOrWhiteSpace(options.BackupDir))
            throw new ArgumentException("备份目录不能为空", nameof(options));
        if (options.FullBackupDayOfWeek is < 0 or > 6)
            throw new ArgumentException("全量备份星期配置无效", nameof(options));
        if (!IsValidTime(options.BackupTime))
            throw new ArgumentException("备份时间格式必须为 HH:mm", nameof(options));
        if (options.CreateCleanupTask && !IsValidTime(options.CleanupTime))
            throw new ArgumentException("清理时间格式必须为 HH:mm", nameof(options));
        if (options.RetentionDays <= 0)
            throw new ArgumentException("保留天数必须大于 0", nameof(options));
        if (options.MinKeepCount < 0)
            throw new ArgumentException("最少保留份数不能小于 0", nameof(options));
    }

    private static bool IsValidTime(string value)
    {
        return TimeOnly.TryParseExact(value.Trim(), "HH:mm", out _);
    }
}
