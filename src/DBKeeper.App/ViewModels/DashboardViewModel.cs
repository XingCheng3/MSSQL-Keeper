using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using System.Collections.ObjectModel;

namespace DBKeeper.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepo;
    private readonly IExecutionLogRepository _logRepo;
    private readonly ISettingsRepository _settings;
    private readonly IConnectionRepository _connRepo;
    private readonly IBackupFileRepository _backupRepo;

    [ObservableProperty] private int _activeTaskCount;
    [ObservableProperty] private int _todayExecutionCount;
    [ObservableProperty] private int _todayFailedCount;

    // 磁盘信息拆分
    [ObservableProperty] private string _diskDrive = "—";
    [ObservableProperty] private string _diskFreeGb = "—";
    [ObservableProperty] private string _diskTotalGb = "—";
    [ObservableProperty] private double _diskUsedPercent;

    // 备份统计
    [ObservableProperty] private int _totalBackupCount;
    [ObservableProperty] private string _totalBackupSize = "—";
    [ObservableProperty] private string _lastBackupTime = "—";

    public ObservableCollection<ExecutionLog> RecentLogs { get; } = [];
    public ObservableCollection<TaskItem> UpcomingTasks { get; } = [];
    public ObservableCollection<Connection> Connections { get; } = [];

    public DashboardViewModel(
        ITaskRepository taskRepo,
        IExecutionLogRepository logRepo,
        ISettingsRepository settings,
        IConnectionRepository connRepo,
        IBackupFileRepository backupRepo)
    {
        _taskRepo = taskRepo;
        _logRepo = logRepo;
        _settings = settings;
        _connRepo = connRepo;
        _backupRepo = backupRepo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        // 统计卡片
        var tasks = await _taskRepo.GetAllAsync();
        ActiveTaskCount = tasks.Count(t => t.IsEnabled);
        TodayExecutionCount = await _logRepo.CountTodayAsync();
        TodayFailedCount = await _logRepo.CountRecent24hFailedAsync();

        // 磁盘空间
        try
        {
            var backupDir = await _settings.GetAsync("default_backup_dir");
            System.IO.DriveInfo? targetDrive = null;

            if (!string.IsNullOrEmpty(backupDir))
            {
                var root = System.IO.Path.GetPathRoot(backupDir);
                if (root != null)
                    targetDrive = new System.IO.DriveInfo(root);
            }

            targetDrive ??= System.IO.DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == System.IO.DriveType.Fixed)
                .OrderByDescending(d => d.TotalSize)
                .FirstOrDefault(d => d.Name != @"C:\")
                ?? System.IO.DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);

            if (targetDrive is { IsReady: true })
            {
                var freeGb = targetDrive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                var totalGb = targetDrive.TotalSize / (1024.0 * 1024 * 1024);
                DiskDrive = targetDrive.Name.TrimEnd('\\');
                DiskFreeGb = freeGb.ToString("F0");
                DiskTotalGb = totalGb.ToString("F0");
                DiskUsedPercent = totalGb > 0 ? ((totalGb - freeGb) / totalGb) * 100 : 0;
            }
        }
        catch
        {
            DiskDrive = "?";
            DiskFreeGb = "—";
            DiskTotalGb = "—";
        }

        // 最近日志
        var recent = await _logRepo.GetRecentAsync(15);
        RecentLogs.Clear();
        foreach (var l in recent) RecentLogs.Add(l);

        // 即将执行的任务
        UpcomingTasks.Clear();
        var upcoming = tasks
            .Where(t => t.IsEnabled && !string.IsNullOrEmpty(t.NextRunAt))
            .OrderBy(t => t.NextRunAt)
            .Take(5);
        foreach (var t in upcoming) UpcomingTasks.Add(t);

        // 连接列表
        var conns = await _connRepo.GetAllAsync();
        Connections.Clear();
        foreach (var c in conns) Connections.Add(c);

        // 备份统计
        var allBackups = await _backupRepo.GetAllAsync();
        var activeBackups = allBackups.Where(b => b.Status != "DELETED").ToList();
        TotalBackupCount = activeBackups.Count;
        var totalBytes = activeBackups.Sum(b => b.FileSizeBytes ?? 0);
        TotalBackupSize = totalBytes switch
        {
            < 1024 * 1024 => $"{totalBytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{totalBytes / (1024.0 * 1024):F1} MB",
            _ => $"{totalBytes / (1024.0 * 1024 * 1024):F2} GB"
        };
        if (activeBackups.Count > 0)
        {
            var latest = activeBackups.OrderByDescending(b => b.CreatedAt).First();
            LastBackupTime = DateTime.TryParse(latest.CreatedAt, out var dt)
                ? dt.ToString("MM-dd HH:mm")
                : latest.CreatedAt ?? "—";
        }
        else
        {
            LastBackupTime = "暂无";
        }
    }
}
