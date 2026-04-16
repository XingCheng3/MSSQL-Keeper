using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.Dialogs;
using DBKeeper.App.ViewModels;
using Serilog;

namespace DBKeeper.App.Views;

public partial class TaskListPage : Page
{
    private TaskListViewModel _vm = null!;

    public TaskListPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<TaskListViewModel>();
        DataContext = _vm;
        taskGrid.ItemsSource = _vm.Tasks;

        await _vm.LoadAsync();
        UpdateEmptyState();
        _vm.Tasks.CollectionChanged += (_, _) => UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        emptyState.Visibility = _vm.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        taskGrid.Visibility = _vm.Tasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void NewTask_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditTaskDialog();
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            await _vm.SaveTaskAsync(dialog.Result, isNew: true);
            Log.Information("新建任务: {Name}, 类型={Type}", dialog.Result.Name, dialog.Result.TaskType);
        }
    }

    private async void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TaskListItem item }) return;
        var dialog = new EditTaskDialog(item.Model);
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            await _vm.SaveTaskAsync(dialog.Result, isNew: false);
            await _vm.LoadAsync();
            Log.Information("编辑任务: {Name}", dialog.Result.Name);
        }
    }

    private async void ExecuteNow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TaskListItem item }) return;
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认执行",
            Content = $"确定立即执行任务「{item.Model.Name}」吗？",
            PrimaryButtonText = "执行",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            Log.Information("手动触发任务: {Name}", item.Model.Name);
            await _vm.ExecuteNowCommand.ExecuteAsync(item);
        }
    }

    private async void ToggleEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TaskListItem item }) return;
        await _vm.ToggleEnabledCommand.ExecuteAsync(item);
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TaskListItem item }) return;
        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认删除",
            Content = $"确定要删除任务「{item.Model.Name}」吗？此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            await _vm.DeleteTaskCommand.ExecuteAsync(item);
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        var selected = cmbType.SelectedItem as ComboBoxItem;
        _vm.FilterType = selected?.Tag?.ToString();
        _vm.ApplyFilter();
        UpdateEmptyState();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.SearchText = txtSearch.Text ?? string.Empty;
        _vm.ApplyFilter();
        UpdateEmptyState();
    }
}
