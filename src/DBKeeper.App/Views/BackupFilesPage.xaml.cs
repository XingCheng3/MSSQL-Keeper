using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.ViewModels;
using DBKeeper.Core.Models;

namespace DBKeeper.App.Views;

public partial class BackupFilesPage : Page
{
    private BackupFilesViewModel _vm = null!;

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
        _vm.Files.CollectionChanged += (_, _) => UpdateEmptyState();
    }

    private void PopulateDatabaseFilter()
    {
        var databases = _vm.Files.Select(f => f.DatabaseName).Distinct().OrderBy(n => n).ToList();
        cmbDatabase.Items.Clear();
        cmbDatabase.Items.Add(new ComboBoxItem { Content = "全部数据库", IsSelected = true });
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
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认删除",
            Content = $"确定删除备份文件「{file.FileName}」吗？\n磁盘文件将被永久删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            await _vm.DeleteFileCommand.ExecuteAsync(file);
    }

    private async void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedFiles.Count == 0) return;
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认批量删除",
            Content = $"确定删除选中的 {_vm.SelectedFiles.Count} 个备份文件吗？\n磁盘文件将被永久删除，此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            await _vm.BatchDeleteCommand.ExecuteAsync(null);
            btnBatchDelete.IsEnabled = false;
        }
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
        _vm.FilterStatus = (cmbStatus.SelectedItem as ComboBoxItem)?.Tag as string;
        _vm.FilterDateFrom = dpDateFrom.SelectedDate;
        _vm.FilterDateTo = dpDateTo.SelectedDate;
        await _vm.LoadAsync();
    }
}
