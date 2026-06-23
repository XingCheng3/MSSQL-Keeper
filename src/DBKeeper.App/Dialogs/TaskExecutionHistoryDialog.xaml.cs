using System.Windows;
using System.IO;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DBKeeper.App.Dialogs;

public partial class TaskExecutionHistoryDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly TaskItem _task;

    public TaskExecutionHistoryDialog(TaskItem task)
    {
        InitializeComponent();
        _task = task;
        Owner = Application.Current.MainWindow;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        txtTitle.Text = $"执行历史 - {_task.Name}";
        txtSubTitle.Text = $"{DisplayTaskType(_task.TaskType)} / 任务 ID：{_task.Id}";
        txtError.Visibility = Visibility.Collapsed;
        txtCount.Text = "加载中...";

        try
        {
            var logRepo = App.Services.GetRequiredService<IExecutionLogRepository>();
            var backupRepo = App.Services.GetRequiredService<IBackupFileRepository>();
            var logs = await logRepo.GetByTaskAsync(_task.Id, _task.Name, _task.TaskType, 50);
            var files = await backupRepo.GetByTaskIdAsync(_task.Id);
            var fileMap = files
                .Where(f => f.ExecutionLogId.HasValue)
                .GroupBy(f => f.ExecutionLogId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.CreatedAt).First());

            var items = logs.Select(log =>
            {
                fileMap.TryGetValue(log.Id, out var file);
                return new TaskExecutionHistoryItem(log, file);
            }).ToList();

            historyGrid.ItemsSource = items;
            historyGrid.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            emptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtCount.Text = items.Count == 0 ? "0 条" : $"最近 {items.Count} 条";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载任务执行历史失败: {TaskId}", _task.Id);
            historyGrid.ItemsSource = null;
            historyGrid.Visibility = Visibility.Collapsed;
            emptyState.Visibility = Visibility.Visible;
            txtEmptyTitle.Text = "加载失败";
            txtEmptyDetail.Text = ex.Message;
            txtCount.Text = "加载失败";
            txtError.Text = $"加载失败：{ex.Message}";
            txtError.Visibility = Visibility.Visible;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DragBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch { /* 忽略拖拽过程中鼠标状态变化导致的异常 */ }
    }

    private static string DisplayTaskType(string taskType)
    {
        return taskType switch
        {
            "BACKUP" => "备份",
            "PROCEDURE" => "存储过程",
            "CUSTOM_SQL" => "自定义 SQL",
            "BACKUP_CLEANUP" => "备份清理",
            "DATA_ARCHIVE" => "数据归档",
            "DIRECTORY_SYNC" => "目录同步",
            _ => taskType
        };
    }

    private sealed class TaskExecutionHistoryItem
    {
        private readonly ExecutionLog _log;
        private readonly BackupFile? _file;

        public TaskExecutionHistoryItem(ExecutionLog log, BackupFile? file)
        {
            _log = log;
            _file = file;
        }

        public string StartedAtDisplay => FormatDateTime(_log.StartedAt);
        public string FinishedAtDisplay => FormatDateTime(_log.FinishedAt);

        public string TriggerDisplay => _log.TriggerType switch
        {
            "SCHEDULED" => "定时",
            "MANUAL" => "手动",
            "SYSTEM" => "系统",
            _ => _log.TriggerType
        };

        public string StatusDisplay => _log.Status switch
        {
            "RUNNING" => "执行中",
            "SUCCESS" => "成功",
            "WARNING" => "警告",
            "FAILED" => "失败",
            "CANCELLED" => "已取消",
            _ => _log.Status
        };

        public string DurationDisplay
        {
            get
            {
                if (!_log.DurationMs.HasValue)
                    return "-";

                var duration = TimeSpan.FromMilliseconds(_log.DurationMs.Value);
                return duration.TotalSeconds < 60
                    ? $"{duration.TotalSeconds:F1} 秒"
                    : $"{duration.TotalMinutes:F1} 分钟";
            }
        }

        public string SummaryDisplay => string.IsNullOrWhiteSpace(_log.Summary) ? "-" : _log.Summary!;
        public bool HasError => !string.IsNullOrWhiteSpace(_log.ErrorDetail);

        public string FileStatusDisplay
        {
            get
            {
                if (_file == null)
                    return "-";

                var exists = File.Exists(_file.FilePath) ? "存在" : "不存在";
                return $"{_file.FileName}，磁盘{exists}";
            }
        }

        public string ErrorDisplay => string.IsNullOrWhiteSpace(_log.ErrorDetail) ? "-" : _log.ErrorDetail!;

        private static string FormatDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "-";

            return DateTime.TryParse(value, out var dt)
                ? dt.ToString("yyyy-MM-dd HH:mm:ss")
                : value;
        }
    }
}
