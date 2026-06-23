using System.Windows;
using System.Windows.Controls;
using DBKeeper.Core.Helpers;
using DBKeeper.Core.Models;
using DBKeeper.Data;
using DBKeeper.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DBKeeper.App.Dialogs;

public partial class BackupPlanDialog : Wpf.Ui.Controls.FluentWindow
{
    public IReadOnlyList<TaskItem> Result { get; private set; } = [];

    public BackupPlanDialog()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            var connRepo = App.Services.GetRequiredService<IConnectionRepository>();
            var connections = await connRepo.GetAllAsync();
            cmbConnection.ItemsSource = connections;
            cmbConnection.SelectionChanged += CmbConnection_SelectionChanged;
            if (connections.Count > 0) cmbConnection.SelectedIndex = 0;
        };
    }

    private async void CmbConnection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbConnection.SelectedItem is not Connection conn) return;

        try
        {
            var databases = await SqlServerClient.GetDatabaseListAsync(conn);
            cmbDbName.ItemsSource = databases;
            HideError();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "获取数据库列表失败");
            cmbDbName.ItemsSource = null;
            ShowError($"加载数据库列表失败：{ex.Message}");
        }
    }

    private void BrowseBackupDir_Click(object sender, RoutedEventArgs e)
    {
        var path = Helpers.FolderPicker.Show("选择备份目录", this);
        if (path != null) txtBackupDir.Text = path;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (cmbConnection.SelectedItem is not Connection conn)
        {
            ShowError("请选择绑定连接");
            return;
        }

        if (!int.TryParse((cmbFullDay.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var fullDay))
        {
            ShowError("请选择全量备份日");
            return;
        }

        var options = new BackupPlanOptions
        {
            PlanName = txtPlanName.Text.Trim(),
            ConnectionId = conn.Id,
            DatabaseName = cmbDbName.Text.Trim(),
            BackupDir = txtBackupDir.Text.Trim(),
            FullBackupDayOfWeek = fullDay,
            BackupTime = txtBackupTime.Text.Trim(),
            RetentionDays = int.TryParse(txtRetention.Text, out var retention) ? retention : 0,
            MinKeepCount = int.TryParse(txtMinKeep.Text, out var minKeep) ? minKeep : -1,
            UseCompression = chkCompress.IsChecked == true,
            VerifyAfterBackup = chkVerify.IsChecked == true,
            CreateCleanupTask = chkCleanup.IsChecked == true,
            CleanupTime = txtCleanupTime.Text.Trim()
        };

        try
        {
            Result = BackupPlanTaskFactory.CreateTasks(options);
            DialogResult = true;
            Close();
        }
        catch (ArgumentException ex)
        {
            ShowError(ex.Message);
        }
    }

    private void Cleanup_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (panelCleanupTime == null) return;
        panelCleanupTime.Visibility = chkCleanup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private void DragBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch { /* 忽略拖拽过程中鼠标状态变化导致的异常 */ }
    }

    private void ShowError(string message)
    {
        errorText.Text = message;
        errorText.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        errorText.Visibility = Visibility.Collapsed;
    }
}
