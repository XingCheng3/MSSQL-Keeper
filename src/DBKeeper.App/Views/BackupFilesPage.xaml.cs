using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.ViewModels;
using DBKeeper.Core.Models;

namespace DBKeeper.App.Views;

public partial class BackupFilesPage : Page
{
    private BackupFilesViewModel _vm = null!;
    private bool _isCollectionChangedBound;

    public BackupFilesPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<BackupFilesViewModel>();
        DataContext = _vm;
        fileGrid.ItemsSource = _vm.Files;
        await _vm.LoadAsync();
        PopulateDatabaseFilter();
        UpdateEmptyState();
        if (!_isCollectionChangedBound)
        {
            _vm.Files.CollectionChanged += (_, _) => UpdateEmptyState();
            _isCollectionChangedBound = true;
        }
    }

    private void PopulateDatabaseFilter()
    {
        var databases = _vm.Files.Select(f => f.SourceDisplay).Distinct().OrderBy(n => n).ToList();
        cmbDatabase.Items.Clear();
        cmbDatabase.Items.Add(new ComboBoxItem { Content = "全部来源", IsSelected = true });
        foreach (var db in databases)
        {
            cmbDatabase.Items.Add(new ComboBoxItem { Content = db, Tag = db });
        }
        cmbDatabase.SelectedIndex = 0;
    }

    private void UpdateEmptyState()
    {
        emptyState.Visibility = _vm.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        fileGrid.Visibility = _vm.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BackupFile file })
            await _vm.TogglePinCommand.ExecuteAsync(file);
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BackupFile file })
            await _vm.OpenFolderCommand.ExecuteAsync(file);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BackupFile file }) return;
        var recordOnly = ShouldDeleteRecordOnly(file);
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认删除",
            Content = recordOnly
                ? $"确定删除备份管理记录「{file.FileName}」吗？\n不会删除磁盘上的实际目录或文件。"
                : $"确定删除备份文件「{file.FileName}」吗？\n磁盘文件将被永久删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            try
            {
                await _vm.DeleteFileCommand.ExecuteAsync(file);
            }
            catch (Exception ex)
            {
                await new Wpf.Ui.Controls.MessageBox
                {
                    Title = "删除失败",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                }.ShowDialogAsync();
            }
        }
    }

    private async void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFiles.Count == 0) return;
        var recordOnlyCount = _vm.SelectedFiles.Count(ShouldDeleteRecordOnly);
        var physicalDeleteCount = _vm.SelectedFiles.Count - recordOnlyCount;
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认批量删除",
            Content = recordOnlyCount > 0
                ? $"确定删除选中的 {_vm.SelectedFiles.Count} 条记录吗？\n其中 {recordOnlyCount} 条只删除管理记录，{physicalDeleteCount} 条会删除磁盘文件。"
                : $"确定删除选中的 {_vm.SelectedFiles.Count} 个备份文件吗？\n磁盘文件将被永久删除，此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            try
            {
                await _vm.BatchDeleteCommand.ExecuteAsync(null);
                btnBatchDelete.IsEnabled = false;
            }
            catch (Exception ex)
            {
                await new Wpf.Ui.Controls.MessageBox
                {
                    Title = "批量删除完成，但存在失败项",
                    Content = ex.Message,
                    CloseButtonText = "确定"
                }.ShowDialogAsync();
            }
        }
    }

    private async void RefreshScan_Click(object sender, RoutedEventArgs e)
    {
        await _vm.SyncAndLoadCommand.ExecuteAsync(null);
        PopulateDatabaseFilter();
        UpdateEmptyState();
    }

    private void RowCheckbox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb && cb.Tag is BackupFile file)
        {
            _vm.ToggleSelection(file, cb.IsChecked == true);
            btnBatchDelete.IsEnabled = _vm.SelectedCount > 0;
        }
    }

    private async void Filter_Changed(object sender, EventArgs e)
    {
        if (_vm == null) return;
        _vm.FilterDatabase = (cmbDatabase.SelectedItem as ComboBoxItem)?.Tag as string;
        _vm.FilterSourceType = (cmbSourceType.SelectedItem as ComboBoxItem)?.Tag as string;
        _vm.FilterStatus = (cmbStatus.SelectedItem as ComboBoxItem)?.Tag as string;
        _vm.FilterDateFrom = dpDateFrom.SelectedDate;
        _vm.FilterDateTo = dpDateTo.SelectedDate;
        await _vm.LoadAsync();
    }

    private static bool ShouldDeleteRecordOnly(BackupFile file)
    {
        return string.Equals(file.Status, "DELETED", StringComparison.OrdinalIgnoreCase)
            || IsDirectorySyncTargetRecord(file);
    }

    private static bool IsDirectorySyncTargetRecord(BackupFile file)
    {
        if (!string.Equals(file.SourceType, "DIRECTORY", StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(file.BackupType, "DIR_DIFF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(file.BackupType, "DIR_FULL", StringComparison.OrdinalIgnoreCase);
    }
}
