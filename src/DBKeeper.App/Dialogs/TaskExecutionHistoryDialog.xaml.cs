using System.Windows;
using System.IO;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace DBKeeper.App.Dialogs;

public partial class TaskExecutionHistoryDialog : Window
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
        txtSubTitle.Text = "最近 50 次执行记录，备份任务会显示对应文件当前是否还存在。";

        var logRepo = App.Services.GetRequiredService<IExecutionLogRepository>();
        var backupRepo = App.Services.GetRequiredService<IBackupFileRepository>();
        var logs = await logRepo.GetByTaskIdAsync(_task.Id, 50);
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
        txtEmpty.Text = items.Count == 0 ? "暂无执行记录" : $"共 {items.Count} 条记录";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
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

        private static string FormatDateTime(string value)
        {
            return DateTime.TryParse(value, out var dt)
                ? dt.ToString("yyyy-MM-dd HH:mm:ss")
                : value;
        }
    }
}
