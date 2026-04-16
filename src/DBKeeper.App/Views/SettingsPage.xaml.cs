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

        // 加载当前设置
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
        Log.Information("设置变更: max_concurrent_tasks = {Value}", val);
    }

    private async void ScanInterval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || cmbScanInterval.SelectedItem is not ComboBoxItem item) return;
        var val = item.Tag?.ToString() ?? "30";
        await _settings.SetAsync("backup_scan_interval_min", val);
        Log.Information("设置变更: backup_scan_interval_min = {Value}", val);
    }

    private async void BrowseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        var path = Helpers.FolderPicker.Show("选择默认备份目录");
        if (path != null)
        {
            txtDefaultBackupDir.Text = path;
            await _settings.SetAsync("default_backup_dir", path);
            Log.Information("设置变更: default_backup_dir = {Path}", path);
        }
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
            var connRepo = App.Services.GetRequiredService<IConnectionRepository>();
            var taskRepo = App.Services.GetRequiredService<ITaskRepository>();

            var connections = await connRepo.GetAllAsync();
            var tasks = await taskRepo.GetAllAsync();
            var allSettings = await _settings.GetAllAsync();

            // 导出数据：密码脱敏
            var exportData = new
            {
                version = "1.0",
                exported_at = DateTime.Now.ToString("O"),
                connections = connections.Select(c => new
                {
                    c.Name,
                    c.Host,
                    c.Username,
                    password = "",  // 密码脱敏
                    c.DefaultDb,
                    c.TimeoutSec,
                    c.IsDefault,
                    c.Remark
                }),
                tasks = tasks.Select(t => new
                {
                    t.Name,
                    t.TaskType,
                    connection_name = connections.FirstOrDefault(c => c.Id == t.ConnectionId)?.Name ?? "",
                    t.IsEnabled,
                    t.ScheduleType,
                    t.ScheduleConfig,
                    t.TaskConfig
                }),
                settings = allSettings.ToDictionary(s => s.Key, s => s.Value)
            };

            var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
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
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // 验证版本
            if (!root.TryGetProperty("version", out var versionElem))
            {
                await ShowMessage("导入失败", "无效的配置文件格式：缺少 version 字段。");
                return;
            }

            var connRepo = App.Services.GetRequiredService<IConnectionRepository>();
            var taskRepo = App.Services.GetRequiredService<ITaskRepository>();

            var existingConns = await connRepo.GetAllAsync();
            var existingTasks = await taskRepo.GetAllAsync();
            var newConnNames = new List<string>();
            var skippedConnNames = new List<string>();

            // 导入连接
            if (root.TryGetProperty("connections", out var connsElem))
            {
                foreach (var connObj in connsElem.EnumerateArray())
                {
                    var name = connObj.GetProperty("name").GetString()!;
                    var existing = existingConns.FirstOrDefault(c => c.Name == name);

                    if (existing != null)
                    {
                        // 已存在的连接 → 跳过，保留原密码
                        skippedConnNames.Add(name);
                        Log.Information("导入跳过已有连接: {Name}（保留原密码）", name);
                    }
                    else
                    {
                        // 新连接 → 插入（密码为空）
                        var conn = new Connection
                        {
                            Name = name,
                            Host = connObj.GetProperty("host").GetString()!,
                            Username = connObj.GetProperty("username").GetString()!,
                            Password = "",  // 密码为空，需用户后续填写
                            DefaultDb = connObj.TryGetProperty("default_db", out var db) ? db.GetString() : null,
                            TimeoutSec = connObj.TryGetProperty("timeout_sec", out var ts) ? ts.GetInt32() : 30,
                            IsDefault = connObj.TryGetProperty("is_default", out var id) && id.GetBoolean(),
                            Remark = connObj.TryGetProperty("remark", out var rm) ? rm.GetString() : null,
                        };
                        await connRepo.InsertAsync(conn);
                        newConnNames.Add(name);
                        Log.Information("导入新连接: {Name}", name);
                    }
                }
            }

            // 刷新连接列表以获取新插入的 ID
            existingConns = await connRepo.GetAllAsync();

            // 导入任务
            if (root.TryGetProperty("tasks", out var tasksElem))
            {
                foreach (var taskObj in tasksElem.EnumerateArray())
                {
                    var taskName = taskObj.GetProperty("name").GetString()!;
                    var connName = taskObj.TryGetProperty("connection_name", out var cn) ? cn.GetString() : "";

                    // 匹配连接
                    var matchedConn = existingConns.FirstOrDefault(c => c.Name == connName);
                    if (matchedConn == null)
                    {
                        Log.Warning("导入任务跳过: {TaskName}（未找到关联连接 {ConnName}）", taskName, connName);
                        continue;
                    }

                    // 检查是否已有同名任务
                    if (existingTasks.Any(t => t.Name == taskName && t.ConnectionId == matchedConn.Id))
                    {
                        Log.Information("导入跳过已有任务: {TaskName}", taskName);
                        continue;
                    }

                    var task = new TaskItem
                    {
                        Name = taskName,
                        TaskType = taskObj.GetProperty("task_type").GetString()!,
                        ConnectionId = matchedConn.Id,
                        IsEnabled = taskObj.TryGetProperty("is_enabled", out var ie) && ie.GetBoolean(),
                        ScheduleType = taskObj.GetProperty("schedule_type").GetString()!,
                        ScheduleConfig = taskObj.GetProperty("schedule_config").GetString()!,
                        TaskConfig = taskObj.GetProperty("task_config").GetString()!,
                    };
                    await taskRepo.InsertAsync(task);
                    Log.Information("导入新任务: {TaskName}", taskName);
                }
            }

            // 导入设置
            if (root.TryGetProperty("settings", out var settingsElem))
            {
                foreach (var prop in settingsElem.EnumerateObject())
                {
                    // 不覆盖已有设置
                    var existing = await _settings.GetAsync(prop.Name);
                    if (string.IsNullOrEmpty(existing))
                    {
                        await _settings.SetAsync(prop.Name, prop.Value.GetString() ?? "");
                    }
                }
            }

            // 汇总提示
            var msg = "配置导入完成。\n\n";
            if (newConnNames.Count > 0)
                msg += $"新增连接 ({newConnNames.Count}): {string.Join("、", newConnNames)}\n\n";
            if (skippedConnNames.Count > 0)
                msg += $"跳过已有连接 ({skippedConnNames.Count}): {string.Join("、", skippedConnNames)}\n\n";
            if (newConnNames.Count > 0)
                msg += "⚠️ 新增连接的密码为空，请前往「连接管理」补填密码。";

            Log.Information("配置导入完成: 新增 {ConnCount} 个连接", newConnNames.Count);

            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "导入完成",
                Content = msg.TrimEnd(),
                CloseButtonText = "确定"
            }.ShowDialogAsync();

            // 重新加载设置页
            _loaded = false;
            Page_Loaded(this, new RoutedEventArgs());
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

    #endregion

    /// <summary>拦截鼠标滚轮事件，防止被 NavigationView 父级 ScrollViewer 劫持</summary>
    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
