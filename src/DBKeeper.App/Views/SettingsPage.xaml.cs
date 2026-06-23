using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using DBKeeper.App.Converters;
using DBKeeper.App.Services;
using DBKeeper.Scheduling;
using Serilog;

namespace DBKeeper.App.Views;

public partial class SettingsPage : Page
{
    private ISettingsRepository _settings = null!;
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = App.Services.GetRequiredService<ISettingsRepository>();
        await LoadPageDataAsync();
    }

    private async Task LoadPageDataAsync()
    {
        toggleTray.IsChecked = (await _settings.GetAsync("minimize_to_tray_on_close")) == "true";
        toggleAutoStart.IsChecked = (await _settings.GetAsync("auto_start")) == "true";

        var retention = await _settings.GetAsync("log_retention_days") ?? "90";
        ComboBoxHelper.SelectByTag(cmbLogRetention, retention);

        var concurrency = await _settings.GetAsync("max_concurrent_tasks") ?? "3";
        ComboBoxHelper.SelectByTag(cmbConcurrency, concurrency);

        var scanInterval = await _settings.GetAsync("backup_scan_interval_min") ?? "30";
        ComboBoxHelper.SelectByTag(cmbScanInterval, scanInterval);

        // 默认备份目录
        txtDefaultBackupDir.Text = await _settings.GetAsync("default_backup_dir") ?? "";

        // 数据库路径
        var dbInit = App.Services.GetRequiredService<DbInitializer>();
        txtDbPath.Text = dbInit.ConnectionString.Replace("Data Source=", "");

        _loaded = true;
    }

    private async void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var val = toggleTray.IsChecked == true ? "true" : "false";
        await _settings.SetAsync("minimize_to_tray_on_close", val);
        Log.Information("设置变更: minimize_to_tray_on_close = {Value}", val);
    }

    private async void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        var enabled = toggleAutoStart.IsChecked == true;
        await _settings.SetAsync("auto_start", enabled ? "true" : "false");

        try
        {
            var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (enabled)
                key?.SetValue("DBKeeper", $"\"{Environment.ProcessPath}\"");
            else
                key?.DeleteValue("DBKeeper", false);
            Log.Information("开机自启已{Action}", enabled ? "启用" : "禁用");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "设置开机自启失败");
        }
    }

    private async void LogRetention_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || cmbLogRetention.SelectedItem is not ComboBoxItem item) return;
        var val = item.Tag?.ToString() ?? "90";
        await _settings.SetAsync("log_retention_days", val);
        Log.Information("设置变更: log_retention_days = {Value}", val);
    }

    private async void Concurrency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || cmbConcurrency.SelectedItem is not ComboBoxItem item) return;
        var val = item.Tag?.ToString() ?? "3";
        await _settings.SetAsync("max_concurrent_tasks", val);
        if (int.TryParse(val, out var max))
            App.Services.GetRequiredService<SchedulerService>().UpdateConcurrencyLimit(max);
        Log.Information("设置变更: max_concurrent_tasks = {Value}", val);
    }

    private async void ScanInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || cmbScanInterval.SelectedItem is not ComboBoxItem item) return;
        var val = item.Tag?.ToString() ?? "30";
        await _settings.SetAsync("backup_scan_interval_min", val);
        if (int.TryParse(val, out var minutes))
            App.Services.GetRequiredService<BackupFileSyncService>().Restart(minutes);
        Log.Information("设置变更: backup_scan_interval_min = {Value}", val);
    }

    private async void BrowseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        var path = Helpers.FolderPicker.Show("选择默认备份目录");
        if (path != null)
        {
            txtDefaultBackupDir.Text = path;
            await SaveDefaultBackupDirAsync(path);
        }
    }

    private async void DefaultBackupDir_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        await SaveDefaultBackupDirAsync(txtDefaultBackupDir.Text.Trim());
    }

    private async void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认清空",
            Content = "确定清空所有执行日志吗？此操作不可撤销。",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

        var logRepo = App.Services.GetRequiredService<IExecutionLogRepository>();
        await logRepo.ClearAllAsync();
        Log.Information("执行日志已清空");

        await new Wpf.Ui.Controls.MessageBox
        {
            Title = "完成",
            Content = "日志已清空。",
            CloseButtonText = "确定"
        }.ShowDialogAsync();
    }

    private void OpenDbFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = txtDbPath.Text;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (dir != null && System.IO.Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
    }

    #region 配置导出

    private async void ExportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = $"dbkeeper_config_{DateTime.Now:yyyyMMdd_HHmmss}.json",
            Title = "导出配置"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var transferService = App.Services.GetRequiredService<ConfigurationTransferService>();
            var json = await transferService.ExportAsync();
            await System.IO.File.WriteAllTextAsync(dialog.FileName, json, System.Text.Encoding.UTF8);
            Log.Information("配置已导出到: {Path}", dialog.FileName);

            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "导出成功",
                Content = $"配置已导出到:\n{dialog.FileName}\n\n注意：密码已脱敏，导入后需重新填写。",
                CloseButtonText = "确定"
            }.ShowDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "配置导出失败");
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "导出失败",
                Content = $"导出配置时出错：{ex.Message}",
                CloseButtonText = "确定"
            }.ShowDialogAsync();
        }
    }

    #endregion

    #region 配置导入

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON 文件|*.json",
            Title = "导入配置"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
            var transferService = App.Services.GetRequiredService<ConfigurationTransferService>();
            var importResult = await transferService.ImportAsync(json);
            await ApplyImportedTasksToSchedulerAsync(importResult);
            await ApplyRuntimeSettingsAsync();

            var msg = BuildImportSummary(importResult);
            Log.Information(
                "配置导入完成: 新增连接 {AddedConnections}，更新连接 {UpdatedConnections}，新增任务 {AddedTasks}，更新任务 {UpdatedTasks}",
                importResult.AddedConnections,
                importResult.UpdatedConnections,
                importResult.AddedTasks,
                importResult.UpdatedTasks);

            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "导入完成",
                Content = msg,
                CloseButtonText = "确定"
            }.ShowDialogAsync();

            _loaded = false;
            await LoadPageDataAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "配置导入失败");
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "导入失败",
                Content = $"导入配置时出错：{ex.Message}",
                CloseButtonText = "确定"
            }.ShowDialogAsync();
        }
    }

    private static async Task ShowMessage(string title, string content)
    {
        await new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定"
        }.ShowDialogAsync();
    }

    private async Task SaveDefaultBackupDirAsync(string path)
    {
        await _settings.SetAsync("default_backup_dir", path);
        Log.Information("设置变更: default_backup_dir = {Path}", path);
    }

    private async Task ApplyImportedTasksToSchedulerAsync(ConfigurationTransferService.ConfigurationImportResult importResult)
    {
        var scheduler = App.Services.GetRequiredService<SchedulerService>();
        var taskRepo = App.Services.GetRequiredService<ITaskRepository>();
        foreach (var taskInfo in importResult.ImportedTasks)
        {
            if (taskInfo.IsEnabled)
            {
                var task = await taskRepo.GetByIdAsync(taskInfo.TaskId);
                if (task != null)
                    await scheduler.ScheduleTaskAsync(task);
            }
            else
            {
                await scheduler.UnscheduleTaskAsync(taskInfo.TaskId);
            }
        }
    }

    private async Task ApplyRuntimeSettingsAsync()
    {
        var scheduler = App.Services.GetRequiredService<SchedulerService>();
        var backupSync = App.Services.GetRequiredService<BackupFileSyncService>();

        var concurrency = await _settings.GetAsync("max_concurrent_tasks") ?? "3";
        if (int.TryParse(concurrency, out var maxConcurrentTasks))
            scheduler.UpdateConcurrencyLimit(maxConcurrentTasks);

        var scanInterval = await _settings.GetAsync("backup_scan_interval_min") ?? "30";
        if (int.TryParse(scanInterval, out var scanMinutes))
            backupSync.Restart(scanMinutes);
    }

    private static string BuildImportSummary(ConfigurationTransferService.ConfigurationImportResult importResult)
    {
        var lines = new List<string>
        {
            "配置导入完成。",
            string.Empty,
            $"新增连接：{importResult.AddedConnections}",
            $"更新连接：{importResult.UpdatedConnections}",
            $"新增任务：{importResult.AddedTasks}",
            $"更新任务：{importResult.UpdatedTasks}",
            $"更新设置：{importResult.UpdatedSettings}"
        };

        if (importResult.ConnectionsRequiringPassword.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("以下连接密码为空，请前往“连接管理”补填：");
            lines.Add(string.Join("、", importResult.ConnectionsRequiringPassword));
        }

        if (importResult.SkippedTasks.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("以下任务未导入：");
            foreach (var task in importResult.SkippedTasks)
                lines.Add($"- {task}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    #endregion

    /// <summary>拦截鼠标滚轮事件，防止被 NavigationView 父级 ScrollViewer 劫持</summary>
    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
