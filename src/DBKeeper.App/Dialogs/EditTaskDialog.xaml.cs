using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using DBKeeper.App.Converters;
using Serilog;

namespace DBKeeper.App.Dialogs;

public partial class EditTaskDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly TaskItem? _existing;

    /// <summary>存储过程参数列表</summary>
    private readonly ObservableCollection<SpParamItem> _spParams = [];

    public TaskItem? Result { get; private set; }

    public EditTaskDialog(TaskItem? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        Loaded += async (_, _) =>
        {
            // 绑定参数列表
            paramsList.ItemsSource = _spParams;

            // 加载连接列表
            var connRepo = App.Services.GetRequiredService<IConnectionRepository>();
            var connections = await connRepo.GetAllAsync();
            cmbConnection.ItemsSource = connections;
            cmbConnection.SelectionChanged += CmbConnection_SelectionChanged;
            if (connections.Count > 0) cmbConnection.SelectedIndex = 0;

            if (existing != null) PopulateFromExisting(existing);
        };
    }

    private void PopulateFromExisting(TaskItem task)
    {
        Title = "编辑任务";
        txtName.Text = task.Name;
        ComboBoxHelper.SelectByTag(cmbType, task.TaskType);
        ComboBoxHelper.SelectByTag(cmbSchedule, task.ScheduleType);

        // 选中连接
        if (cmbConnection.ItemsSource is List<Connection> connections)
        {
            var idx = task.ConnectionId.HasValue
                ? connections.FindIndex(c => c.Id == task.ConnectionId.Value)
                : -1;
            if (idx >= 0) cmbConnection.SelectedIndex = idx;
        }

        // 填充调度配置
        try
        {
            var sc = JsonSerializer.Deserialize<JsonElement>(task.ScheduleConfig);
            if (sc.TryGetProperty("time", out var time)) txtTime.Text = time.GetString() ?? "02:00";
            if (sc.TryGetProperty("day_of_week", out var dow)) ComboBoxHelper.SelectByTag(cmbWeekDay, dow.GetInt32().ToString());
            if (sc.TryGetProperty("day_of_month", out var dom)) txtMonthDay.Text = dom.GetInt32().ToString();
            if (sc.TryGetProperty("interval_minutes", out var iv)) txtInterval.Text = iv.GetInt32().ToString();
            if (sc.TryGetProperty("cron_expression", out var cron)) txtCron.Text = cron.GetString();
        }
        catch (Exception ex) { Log.Warning(ex, "解析任务调度配置失败，使用默认值"); }

        // 填充任务配置
        try
        {
            var tc = JsonSerializer.Deserialize<JsonElement>(task.TaskConfig);
            switch (task.TaskType)
            {
                case "BACKUP":
                    if (tc.TryGetProperty("DatabaseName", out var db)) cmbDbName.Text = db.GetString();
                    if (tc.TryGetProperty("BackupDir", out var dir)) txtBackupDir.Text = dir.GetString();
                    if (tc.TryGetProperty("FileNameTemplate", out var tpl)) txtFilePattern.Text = tpl.GetString();
                    if (tc.TryGetProperty("RetentionDays", out var ret)) txtRetention.Text = ret.GetInt32().ToString();
                    if (tc.TryGetProperty("UseCompression", out var comp)) chkCompress.IsChecked = comp.GetBoolean();
                    if (tc.TryGetProperty("VerifyAfterBackup", out var vfy)) chkVerify.IsChecked = vfy.GetBoolean();
                    if (tc.TryGetProperty("BackupType", out var bt))
                    {
                        var btVal = bt.GetString() ?? "FULL";
                        foreach (ComboBoxItem item in cmbBackupType.Items)
                            if (item.Tag?.ToString() == btVal) { cmbBackupType.SelectedItem = item; break; }
                    }
                    break;
                case "PROCEDURE":
                    if (tc.TryGetProperty("DatabaseName", out var spDb)) cmbSpDb.Text = spDb.GetString();
                    if (tc.TryGetProperty("ProcedureName", out var sp)) txtSpName.Text = sp.GetString();
                    // 加载参数列表
                    if (tc.TryGetProperty("Parameters", out var pars) && pars.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in pars.EnumerateArray())
                        {
                            var name = p.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                            var value = p.TryGetProperty("Value", out var v) ? v.GetString() ?? "" : "";
                            _spParams.Add(new SpParamItem { Name = name, Value = value });
                        }
                    }
                    break;
                case "CUSTOM_SQL":
                    if (tc.TryGetProperty("DatabaseName", out var sqlDb)) cmbSqlDb.Text = sqlDb.GetString();
                    if (tc.TryGetProperty("SqlContent", out var sql)) txtSqlContent.Text = sql.GetString();
                    break;
                case "BACKUP_CLEANUP":
                    if (tc.TryGetProperty("TargetDir", out var tdir)) txtCleanupDir.Text = tdir.GetString();
                    if (tc.TryGetProperty("RetentionDays", out var cret)) txtCleanupRetention.Text = cret.GetInt32().ToString();
                    if (tc.TryGetProperty("MinKeepCount", out var mk)) txtMinKeep.Text = mk.GetInt32().ToString();
                    break;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "解析任务配置失败，使用默认值"); }
    }

    private void TaskType_Changed(object sender, SelectionChangedEventArgs e)
    {
        // 初始化阶段控件还未加载
        if (panelBackup == null) return;
        if (cmbType.SelectedItem is not ComboBoxItem item) return;
        var type = item.Tag?.ToString();
        panelBackup.Visibility = type == "BACKUP" ? Visibility.Visible : Visibility.Collapsed;
        panelProcedure.Visibility = type == "PROCEDURE" ? Visibility.Visible : Visibility.Collapsed;
        panelSql.Visibility = type == "CUSTOM_SQL" ? Visibility.Visible : Visibility.Collapsed;
        panelCleanup.Visibility = type == "BACKUP_CLEANUP" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ScheduleType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (panelTime == null) return;
        if (cmbSchedule.SelectedItem is not ComboBoxItem item) return;
        var type = item.Tag?.ToString();
        panelTime.Visibility = type is "DAILY" or "WEEKLY" or "MONTHLY" ? Visibility.Visible : Visibility.Collapsed;
        panelWeekDay.Visibility = type == "WEEKLY" ? Visibility.Visible : Visibility.Collapsed;
        panelMonthDay.Visibility = type == "MONTHLY" ? Visibility.Visible : Visibility.Collapsed;
        panelInterval.Visibility = type == "INTERVAL" ? Visibility.Visible : Visibility.Collapsed;
        panelCron.Visibility = type == "CRON" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        var path = Helpers.FolderPicker.Show("选择备份目录", this);
        if (path != null) txtBackupDir.Text = path;
    }

    private void BrowseCleanupDir_Click(object sender, RoutedEventArgs e)
    {
        var path = Helpers.FolderPicker.Show("选择清理目录", this);
        if (path != null) txtCleanupDir.Text = path;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            ShowError("任务名称不能为空"); return;
        }
        if (cmbConnection.SelectedItem is not Connection conn)
        {
            ShowError("请选择绑定连接"); return;
        }

        var taskType = (cmbType.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "BACKUP";
        var scheduleType = (cmbSchedule.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "DAILY";

        // 构建调度配置 JSON
        var scheduleConfig = BuildScheduleConfig(scheduleType);
        if (scheduleConfig == null) return;

        // 构建任务配置 JSON
        var taskConfig = BuildTaskConfig(taskType);
        if (taskConfig == null) return;

        Result = new TaskItem
        {
            Id = _existing?.Id ?? 0,
            Name = txtName.Text.Trim(),
            TaskType = taskType,
            ConnectionId = conn.Id,
            IsEnabled = _existing?.IsEnabled ?? true,
            ScheduleType = scheduleType,
            ScheduleConfig = scheduleConfig,
            TaskConfig = taskConfig,
            CreatedAt = _existing?.CreatedAt ?? DateTime.Now.ToString("O"),
            UpdatedAt = DateTime.Now.ToString("O")
        };

        DialogResult = true;
        Close();
    }

    private string? BuildScheduleConfig(string type)
    {
        switch (type)
        {
            case "DAILY":
                return JsonSerializer.Serialize(new { time = txtTime.Text.Trim() });
            case "WEEKLY":
                var dayTag = (cmbWeekDay.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1";
                return JsonSerializer.Serialize(new { time = txtTime.Text.Trim(), day_of_week = int.Parse(dayTag) });
            case "MONTHLY":
                if (!int.TryParse(txtMonthDay.Text, out var monthDay)) { ShowError("每月日期格式错误"); return null; }
                return JsonSerializer.Serialize(new { time = txtTime.Text.Trim(), day_of_month = monthDay });
            case "INTERVAL":
                if (!int.TryParse(txtInterval.Text, out var mins)) { ShowError("间隔分钟格式错误"); return null; }
                return JsonSerializer.Serialize(new { interval_minutes = mins });
            case "CRON":
                if (string.IsNullOrWhiteSpace(txtCron.Text)) { ShowError("Cron 表达式不能为空"); return null; }
                return JsonSerializer.Serialize(new { cron_expression = txtCron.Text.Trim() });
            default:
                return "{}";
        }
    }

    private string? BuildTaskConfig(string type)
    {
        switch (type)
        {
            case "BACKUP":
                if (string.IsNullOrWhiteSpace(cmbDbName.Text)) { ShowError("数据库名不能为空"); return null; }
                if (string.IsNullOrWhiteSpace(txtBackupDir.Text)) { ShowError("备份目录不能为空"); return null; }
                if (ContainsInvalidFileNameChar(txtFilePattern.Text)) { ShowError("文件名规则包含非法文件名字符"); return null; }
                var backupType = cmbBackupType.SelectedItem is ComboBoxItem bt ? bt.Tag?.ToString() ?? "FULL" : "FULL";
                return JsonSerializer.Serialize(new
                {
                    DatabaseName = cmbDbName.Text.Trim(),
                    BackupType = backupType,
                    BackupDir = txtBackupDir.Text.Trim(),
                    FileNameTemplate = txtFilePattern.Text.Trim(),
                    RetentionDays = int.TryParse(txtRetention.Text, out var r) ? r : 30,
                    MinKeepCount = 3,
                    UseCompression = chkCompress.IsChecked == true,
                    VerifyAfterBackup = chkVerify.IsChecked == true,
                    TimeoutSec = 600
                });
            case "PROCEDURE":
                if (string.IsNullOrWhiteSpace(txtSpName.Text)) { ShowError("存储过程名不能为空"); return null; }
                var spParams = _spParams.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
                return JsonSerializer.Serialize(new
                {
                    DatabaseName = cmbSpDb.Text.Trim(),
                    ProcedureName = txtSpName.Text.Trim(),
                    Parameters = spParams,
                    TimeoutSec = 300
                }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            case "CUSTOM_SQL":
                if (string.IsNullOrWhiteSpace(txtSqlContent.Text)) { ShowError("SQL 内容不能为空"); return null; }
                return JsonSerializer.Serialize(new
                {
                    DatabaseName = cmbSqlDb.Text.Trim(),
                    SqlContent = txtSqlContent.Text.Trim(),
                    TimeoutSec = 600
                });
            case "BACKUP_CLEANUP":
                if (string.IsNullOrWhiteSpace(txtCleanupDir.Text)) { ShowError("目标目录不能为空"); return null; }
                return JsonSerializer.Serialize(new
                {
                    TargetDir = txtCleanupDir.Text.Trim(),
                    RetentionDays = int.TryParse(txtCleanupRetention.Text, out var cr) ? cr : 30,
                    MinKeepCount = int.TryParse(txtMinKeep.Text, out var mk) ? mk : 3
                });
            default:
                return "{}";
        }
    }

    private static bool ContainsInvalidFileNameChar(string fileNameTemplate)
    {
        if (string.IsNullOrWhiteSpace(fileNameTemplate)) return false;
        return fileNameTemplate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
    }

    private void ShowError(string msg)
    {
        errorText.Text = msg;
        errorText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (System.Windows.Controls.ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void DragBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch { /* 忽略拖拽过程中鼠标状态变化导致的异常 */ }
    }

    /// <summary>连接选择变更时异步加载数据库列表</summary>
    private async void CmbConnection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbConnection.SelectedItem is not Connection conn) return;

        try
        {
            var databases = await SqlServerClient.GetDatabaseListAsync(conn);
            cmbDbName.ItemsSource = databases;
            cmbSpDb.ItemsSource = databases;
            cmbSqlDb.ItemsSource = databases;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "获取数据库列表失败");
            cmbDbName.ItemsSource = null;
            cmbSpDb.ItemsSource = null;
            cmbSqlDb.ItemsSource = null;
        }
    }

    /// <summary>添加存储过程参数</summary>
    private void AddParam_Click(object sender, RoutedEventArgs e)
    {
        _spParams.Add(new SpParamItem { Name = "", Value = "" });
    }

    /// <summary>删除存储过程参数</summary>
    private void RemoveParam_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SpParamItem item })
            _spParams.Remove(item);
    }

    /// <summary>字段失焦时实时验证</summary>
    private void ValidateField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender == txtName)
            ValidateField(errName, string.IsNullOrWhiteSpace(txtName.Text), "任务名称不能为空");
        else if (sender == cmbDbName)
            ValidateField(errDbName, string.IsNullOrWhiteSpace(cmbDbName.Text) && panelBackup.Visibility == Visibility.Visible, "数据库名不能为空");
        else if (sender == txtBackupDir)
            ValidateField(errBackupDir, string.IsNullOrWhiteSpace(txtBackupDir.Text) && panelBackup.Visibility == Visibility.Visible, "备份目录不能为空");
    }

    private static void ValidateField(TextBlock errorBlock, bool hasError, string message)
    {
        if (hasError)
        {
            errorBlock.Text = message;
            errorBlock.Visibility = Visibility.Visible;
        }
        else
        {
            errorBlock.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>清除所有验证错误提示</summary>
    private void ClearValidationErrors()
    {
        errName.Visibility = Visibility.Collapsed;
        errDbName.Visibility = Visibility.Collapsed;
        errBackupDir.Visibility = Visibility.Collapsed;
        errorText.Visibility = Visibility.Collapsed;
    }
}

/// <summary>存储过程参数项（用于 UI 绑定和 JSON 序列化）</summary>
public class SpParamItem
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
