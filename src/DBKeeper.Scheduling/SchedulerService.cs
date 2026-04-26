using System.Collections.Concurrent;
using System.Text.Json;
using Cronos;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using DBKeeper.Executors;
using Serilog;

namespace DBKeeper.Scheduling;

/// <summary>
/// 轻量级调度服务：Timer + Cronos 替代 Quartz.NET（节省 ~40MB 内存 + 20个线程）
/// </summary>
public class SchedulerService
{
    private readonly ITaskRepository _taskRepo;
    private readonly IConnectionRepository _connRepo;
    private readonly IExecutionLogRepository _logRepo;
    private readonly IBackupFileRepository _backupRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly Dictionary<string, ITaskExecutor> _executors;

    private Timer? _tickTimer;
    private readonly ConcurrentDictionary<int, ScheduledEntry> _entries = new();
    private readonly SemaphoreSlim _concurrencyLock;
    private volatile bool _running;

    public SchedulerService(
        ITaskRepository taskRepo,
        IConnectionRepository connRepo,
        IExecutionLogRepository logRepo,
        IBackupFileRepository backupRepo,
        ISettingsRepository settingsRepo)
    {
        _taskRepo = taskRepo;
        _connRepo = connRepo;
        _logRepo = logRepo;
        _backupRepo = backupRepo;
        _settingsRepo = settingsRepo;

        _executors = new ITaskExecutor[]
        {
            new BackupExecutor(),
            new ProcedureExecutor(),
            new SqlExecutor(),
            new CleanupExecutor(backupRepo)
        }.ToDictionary(e => e.TaskType);

        _concurrencyLock = new SemaphoreSlim(3, 3); // 默认最大并发3
    }

    /// <summary>异步初始化</summary>
    public async Task InitializeAsync()
    {
        var maxStr = await _settingsRepo.GetAsync("max_concurrent_tasks") ?? "3";
        var max = int.TryParse(maxStr, out var m) ? m : 3;
        // SemaphoreSlim 只能初始化一次，已在构造函数中设定
        Log.Information("调度引擎初始化，并发上限 {Max}", max);
    }

    /// <summary>启动调度器并从数据库恢复所有启用的任务</summary>
    public async Task StartAsync()
    {
        _running = true;
        var tasks = await _taskRepo.GetEnabledAsync();
        foreach (var task in tasks)
            await ScheduleTaskAsync(task);

        // 每 30 秒检查一次是否有任务到期
        _tickTimer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        Log.Information("调度引擎启动，已加载 {Count} 个任务", tasks.Count);
    }

    public Task StopAsync()
    {
        _running = false;
        _tickTimer?.Dispose();
        _tickTimer = null;
        _entries.Clear();
        Log.Information("调度引擎已停止");
        return Task.CompletedTask;
    }

    /// <summary>注册或更新任务调度</summary>
    public async Task ScheduleTaskAsync(TaskItem task)
    {
        _entries.TryRemove(task.Id, out _);

        if (!task.IsEnabled) return;

        var nextRun = CalculateNextRun(task);
        if (nextRun == null) return;

        _entries[task.Id] = new ScheduledEntry
        {
            TaskId = task.Id,
            NextRunUtc = nextRun.Value
        };

        task.NextRunAt = nextRun.Value.ToLocalTime().ToString("O");
        await _taskRepo.UpdateLastRunAsync(task.Id, task.LastRunStatus ?? "", task.NextRunAt);
    }

    /// <summary>移除任务调度</summary>
    public Task UnscheduleTaskAsync(int taskId)
    {
        _entries.TryRemove(taskId, out _);
        return Task.CompletedTask;
    }

    /// <summary>立即执行任务</summary>
    public async Task TriggerNowAsync(int taskId)
    {
        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null) return;
        await ExecuteTaskAsync(task, "MANUAL");
    }

    /// <summary>暂停所有</summary>
    public Task PauseAllAsync() { _running = false; return Task.CompletedTask; }

    /// <summary>恢复所有</summary>
    public Task ResumeAllAsync() { _running = true; return Task.CompletedTask; }

    /// <summary>定时 tick：检查到期任务并执行</summary>
    private async Task TickAsync()
    {
        if (!_running) return;

        var now = DateTimeOffset.UtcNow;
        var dueEntries = _entries.Values.Where(e => e.NextRunUtc <= now).ToList();

        foreach (var entry in dueEntries)
        {
            var task = await _taskRepo.GetByIdAsync(entry.TaskId);
            if (task == null || !task.IsEnabled)
            {
                _entries.TryRemove(entry.TaskId, out _);
                continue;
            }

            // 在后台执行，受信号量限制并发
            _ = Task.Run(async () =>
            {
                await _concurrencyLock.WaitAsync();
                try
                {
                    await ExecuteTaskAsync(task, "SCHEDULED");
                }
                finally
                {
                    _concurrencyLock.Release();
                }

                // 计算下次执行
                var next = CalculateNextRun(task);
                if (next.HasValue)
                {
                    _entries[task.Id] = new ScheduledEntry { TaskId = task.Id, NextRunUtc = next.Value };
                    task.NextRunAt = next.Value.ToLocalTime().ToString("O");
                    await _taskRepo.UpdateLastRunAsync(task.Id, task.LastRunStatus ?? "", task.NextRunAt);
                }
                else
                {
                    _entries.TryRemove(task.Id, out _);
                }
            });
        }
    }

    /// <summary>计算下次执行时间</summary>
    private DateTimeOffset? CalculateNextRun(TaskItem task)
    {
        try
        {
            var config = JsonSerializer.Deserialize<JsonElement>(task.ScheduleConfig);
            var now = DateTimeOffset.UtcNow;

            switch (task.ScheduleType)
            {
                case "DAILY":
                {
                    var time = config.GetProperty("time").GetString()!;
                    var cron = TimeToDailyCron(time);
                    return CronExpression.Parse(cron).GetNextOccurrence(now, TimeZoneInfo.Local);
                }
                case "WEEKLY":
                {
                    var dow = config.GetProperty("day_of_week").GetInt32();
                    var time = config.GetProperty("time").GetString()!;
                    var parts = time.Split(':');
                    var cronDow = dow == 0 ? 0 : dow; // Cronos: 0=Sun
                    var cron = $"{parts[1]} {parts[0]} * * {cronDow}";
                    return CronExpression.Parse(cron).GetNextOccurrence(now, TimeZoneInfo.Local);
                }
                case "MONTHLY":
                {
                    var dom = config.GetProperty("day_of_month").GetInt32();
                    var time = config.GetProperty("time").GetString()!;
                    var parts = time.Split(':');
                    var cron = $"{parts[1]} {parts[0]} {dom} * *";
                    return CronExpression.Parse(cron).GetNextOccurrence(now, TimeZoneInfo.Local);
                }
                case "INTERVAL":
                {
                    var minutes = config.GetProperty("interval_minutes").GetInt32();
                    return now.AddMinutes(minutes);
                }
                case "CRON":
                {
                    var cronExpr = config.GetProperty("cron_expression").GetString()!;
                    // Cronos 支持 5 段式 cron（分 时 日 月 周），如需秒级用 CronFormat.IncludeSeconds
                    var format = cronExpr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5
                        ? CronFormat.IncludeSeconds
                        : CronFormat.Standard;
                    return CronExpression.Parse(cronExpr, format).GetNextOccurrence(now, TimeZoneInfo.Local);
                }
                default:
                    Log.Warning("未知调度类型: {Type}", task.ScheduleType);
                    return null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "计算任务 {TaskName} 的下次执行时间失败", task.Name);
            return null;
        }
    }

    private static string TimeToDailyCron(string time)
    {
        var parts = time.Split(':');
        return $"{parts[1]} {parts[0]} * * *";
    }

    /// <summary>核心执行逻辑</summary>
    public async Task ExecuteTaskAsync(TaskItem task, string triggerType)
    {
        var conn = await _connRepo.GetByIdAsync(task.ConnectionId);
        if (conn == null)
        {
            Log.Error("任务 {TaskName} 的连接不存在 (ID={ConnId})", task.Name, task.ConnectionId);
            return;
        }

        var log = new ExecutionLog
        {
            TaskId = task.Id,
            TaskName = task.Name,
            TaskType = task.TaskType,
            TriggerType = triggerType,
            StartedAt = DateTime.Now.ToString("O"),
            Status = "RUNNING"
        };
        log.Id = await _logRepo.InsertAsync(log);
        await _taskRepo.UpdateLastRunAsync(task.Id, "RUNNING", task.NextRunAt);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!_executors.TryGetValue(task.TaskType, out var executor))
                throw new InvalidOperationException($"未知任务类型: {task.TaskType}");

            var result = await executor.ExecuteAsync(task, conn).WaitAsync(TimeSpan.FromMinutes(30));
            sw.Stop();

            string status;
            if (result.Success)
                status = "SUCCESS";
            else if (result.IsWarning)
                status = "WARNING";
            else
                status = "FAILED";

            await _logRepo.UpdateFinishAsync(log.Id, status, (int)sw.ElapsedMilliseconds, result.Summary, result.ErrorDetail);
            await _taskRepo.UpdateLastRunAsync(task.Id, status, task.NextRunAt);

            if (task.TaskType == "BACKUP" && result.Success)
            {
                var actualFilePath = result.Metadata?.GetValueOrDefault("FilePath") as string;
                await RecordBackupFileAsync(task, actualFilePath);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "任务 {TaskName} 执行失败", task.Name);
            await _logRepo.UpdateFinishAsync(log.Id, "FAILED", (int)sw.ElapsedMilliseconds, null, ex.ToString());
            await _taskRepo.UpdateLastRunAsync(task.Id, "FAILED", task.NextRunAt);
        }
    }

    /// <summary>定时调度触发执行</summary>
    public async Task ExecuteScheduledAsync(int taskId)
    {
        var task = await _taskRepo.GetByIdAsync(taskId);
        if (task == null || !task.IsEnabled) return;
        await ExecuteTaskAsync(task, "SCHEDULED");
    }

    private async Task RecordBackupFileAsync(TaskItem task, string? actualFilePath)
    {
        var config = JsonSerializer.Deserialize<BackupConfig>(task.TaskConfig);
        if (config == null) return;

        string filePath;
        string fileName;
        if (!string.IsNullOrEmpty(actualFilePath))
        {
            filePath = actualFilePath;
            fileName = Path.GetFileName(filePath);
        }
        else
        {
            fileName = (config.FileNameTemplate ?? "{DB}_{DATE}.bak")
                .Replace("{DB}", config.DatabaseName)
                .Replace("{DATE}", DateTime.Now.ToString("yyyyMMdd_HHmmss"))
                .Replace("{TIME}", DateTime.Now.ToString("HHmmss"));
            filePath = Path.Combine(config.BackupDir, fileName);
        }

        if (!File.Exists(filePath)) return;

        var fi = new FileInfo(filePath);
        var bf = new BackupFile
        {
            TaskId = task.Id,
            DatabaseName = config.DatabaseName,
            FileName = fileName,
            FilePath = filePath,
            FileSizeBytes = fi.Length,
            BackupType = config.BackupType,
            CreatedAt = DateTime.Now.ToString("O"),
            ExpiresAt = config.RetentionDays > 0 ? DateTime.Now.AddDays(config.RetentionDays).ToString("O") : null,
            Status = "NORMAL"
        };
        await _backupRepo.InsertAsync(bf);
    }

    /// <summary>内部调度条目</summary>
    private class ScheduledEntry
    {
        public int TaskId { get; init; }
        public DateTimeOffset NextRunUtc { get; init; }
    }
}
