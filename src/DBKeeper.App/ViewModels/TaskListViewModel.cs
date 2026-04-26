using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using DBKeeper.Scheduling;
using Serilog;

namespace DBKeeper.App.ViewModels;

public partial class TaskListViewModel : ObservableObject
{
    private readonly ITaskRepository _taskRepo;
    private readonly IConnectionRepository _connRepo;
    private readonly SchedulerService _scheduler;
    private List<TaskListItem> _allTasks = [];

    public ObservableCollection<TaskListItem> Tasks { get; } = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _filterType;    // null = 全部
    [ObservableProperty] private string? _filterStatus;

    public TaskListViewModel(ITaskRepository taskRepo, IConnectionRepository connRepo, SchedulerService scheduler)
    {
        _taskRepo = taskRepo;
        _connRepo = connRepo;
        _scheduler = scheduler;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var tasks = await _taskRepo.GetAllAsync();
        var connections = await _connRepo.GetAllAsync();
        var connMap = connections.ToDictionary(c => c.Id, c => c.Name);

        _allTasks.Clear();
        foreach (var t in tasks)
        {
            var connName = t.ConnectionId.HasValue && connMap.TryGetValue(t.ConnectionId.Value, out var name)
                ? name
                : "未绑定连接";
            _allTasks.Add(new TaskListItem(t, connName));
        }

        ApplyFilter();
    }

    /// <summary>根据当前过滤条件刷新 Tasks 显示列表</summary>
    public void ApplyFilter()
    {
        var filtered = _allTasks.AsEnumerable();

        // 按类型过滤
        if (!string.IsNullOrEmpty(FilterType))
            filtered = filtered.Where(t => t.Model.TaskType == FilterType);

        // 按搜索关键字过滤
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            filtered = filtered.Where(t =>
                t.Model.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                t.ConnectionName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        Tasks.Clear();
        foreach (var item in filtered)
            Tasks.Add(item);
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(TaskListItem item)
    {
        item.Model.IsEnabled = !item.Model.IsEnabled;
        await _taskRepo.UpdateAsync(item.Model);
        item.IsEnabled = item.Model.IsEnabled;

        // 同步调度
        if (item.Model.IsEnabled)
            await _scheduler.ScheduleTaskAsync(item.Model);
        else
            await _scheduler.UnscheduleTaskAsync(item.Model.Id);

        Log.Information("任务 {Name} 已{Action}", item.Model.Name, item.IsEnabled ? "启用" : "禁用");
    }

    [RelayCommand]
    private async Task ExecuteNowAsync(TaskListItem item)
    {
        if (item.IsRunning) return;

        item.IsRunning = true;
        item.LastRunStatus = "RUNNING";
        await _scheduler.TriggerNowAsync(item.Model.Id);
        Log.Information("手动触发任务: {Name}", item.Model.Name);
        await LoadAsync(); // 刷新状态
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(TaskListItem item)
    {
        await _scheduler.UnscheduleTaskAsync(item.Model.Id);
        await _taskRepo.DeleteAsync(item.Model.Id);
        _allTasks.Remove(item);
        Tasks.Remove(item);
        Log.Information("删除任务: {Name}", item.Model.Name);
    }

    public async Task SaveTaskAsync(TaskItem task, bool isNew)
    {
        if (isNew)
        {
            task.Id = await _taskRepo.InsertAsync(task);
            var connections = await _connRepo.GetAllAsync();
            var connName = task.ConnectionId.HasValue
                ? connections.FirstOrDefault(c => c.Id == task.ConnectionId.Value)?.Name ?? ""
                : "";
            var listItem = new TaskListItem(task, connName);
            _allTasks.Add(listItem);
            Tasks.Add(listItem);
        }
        else
        {
            await _taskRepo.UpdateAsync(task);
        }
        // 更新调度
        if (task.IsEnabled)
            await _scheduler.ScheduleTaskAsync(task);

        Log.Information("{Action}任务: {Name}", isNew ? "新建" : "更新", task.Name);
    }
}

public partial class TaskListItem : ObservableObject
{
    public TaskItem Model { get; }
    public string ConnectionName { get; }

    public TaskListItem(TaskItem model, string connectionName)
    {
        Model = model;
        ConnectionName = connectionName;
        _isEnabled = model.IsEnabled;
        _lastRunStatus = model.LastRunStatus;
    }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _lastRunStatus;

    public string StatusDisplay => IsRunning || LastRunStatus == "RUNNING"
        ? "执行中..."
        : LastRunStatus ?? "—";

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
    }

    partial void OnLastRunStatusChanged(string? value)
    {
        OnPropertyChanged(nameof(StatusDisplay));
    }

    /// <summary>任务类型中文显示</summary>
    public string TypeDisplay => Model.TaskType switch
    {
        "BACKUP" => "备份",
        "PROCEDURE" => "存储过程",
        "CUSTOM_SQL" => "自定义SQL",
        "BACKUP_CLEANUP" => "备份清理",
        _ => Model.TaskType
    };

    /// <summary>调度周期显示</summary>
    public string ScheduleDisplay
    {
        get
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<JsonElement>(Model.ScheduleConfig);
                return Model.ScheduleType switch
                {
                    "DAILY" => $"每天 {cfg.GetProperty("time").GetString()}",
                    "WEEKLY" => $"每周{DayName(cfg.GetProperty("day_of_week").GetInt32())} {cfg.GetProperty("time").GetString()}",
                    "MONTHLY" => $"每月{cfg.GetProperty("day_of_month").GetInt32()}日 {cfg.GetProperty("time").GetString()}",
                    "INTERVAL" => $"每 {cfg.GetProperty("interval_minutes").GetInt32()} 分钟",
                    "CRON" => cfg.GetProperty("cron_expression").GetString() ?? "Cron",
                    _ => Model.ScheduleType
                };
            }
            catch { return Model.ScheduleType; }
        }
    }

    private static string DayName(int day) => day switch
    {
        0 => "日", 1 => "一", 2 => "二", 3 => "三",
        4 => "四", 5 => "五", 6 => "六", _ => day.ToString()
    };
}
