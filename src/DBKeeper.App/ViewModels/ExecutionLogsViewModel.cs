using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBKeeper.Core.Models;
using DBKeeper.Data.Repositories;
using Serilog;

namespace DBKeeper.App.ViewModels;

public partial class ExecutionLogsViewModel : ObservableObject
{
    private readonly IExecutionLogRepository _logRepo;

    public ObservableCollection<ExecutionLog> Logs { get; } = [];

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _filterTaskName;
    [ObservableProperty] private string? _filterStatus;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private DateTime? _endDate;

    private const int PageSize = 50;

    public ExecutionLogsViewModel(IExecutionLogRepository logRepo)
    {
        _logRepo = logRepo;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        var (items, total) = await _logRepo.GetPagedAsync(
            CurrentPage, PageSize, FilterTaskName, FilterStatus,
            StartDate?.ToString("O"),
            EndDate?.ToString("O"));

        TotalCount = total;
        TotalPages = Math.Max(1, (total + PageSize - 1) / PageSize);

        Logs.Clear();
        foreach (var item in items) Logs.Add(item);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages) { CurrentPage++; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (CurrentPage > 1) { CurrentPage--; await LoadAsync(); }
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        await _logRepo.ClearAllAsync();
        await LoadAsync();
    }

    /// <summary>导出符合当前过滤条件的所有日志为 CSV</summary>
    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        // 使用 SaveFileDialog 选择保存路径
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = $"执行日志_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            // 获取所有符合条件的日志（不分页）
            var (items, _) = await _logRepo.GetPagedAsync(
                1, int.MaxValue, FilterTaskName, FilterStatus,
                StartDate?.ToString("O"),
                EndDate?.ToString("O"));

            // 写入 CSV（UTF-8 BOM 兼容 Excel）
            var sb = new StringBuilder();
            // BOM header
            sb.AppendLine("时间,任务名,类型,触发方式,耗时(ms),状态,摘要,错误详情");

            foreach (var log in items)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsvField(log.StartedAt),
                    EscapeCsvField(log.TaskName),
                    EscapeCsvField(log.TaskType),
                    EscapeCsvField(log.TriggerType),
                    log.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? "",
                    EscapeCsvField(log.Status),
                    EscapeCsvField(log.Summary),
                    EscapeCsvField(log.ErrorDetail)));
            }

            var filePath = dialog.FileName;
            await File.WriteAllTextAsync(filePath, sb.ToString(), new UTF8Encoding(true));
            Log.Information("日志已导出到: {Path}, 共 {Count} 条", filePath, items.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "导出 CSV 失败");
        }
    }

    private static string EscapeCsvField(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        // 包含逗号、引号、换行时用引号包裹
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
