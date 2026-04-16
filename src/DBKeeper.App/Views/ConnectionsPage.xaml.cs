using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.Dialogs;
using DBKeeper.App.ViewModels;

namespace DBKeeper.App.Views;

public partial class ConnectionsPage : Page
{
    private ConnectionsViewModel _vm = null!;

    public ConnectionsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<ConnectionsViewModel>();
        DataContext = _vm;
        cardList.ItemsSource = _vm.Connections;

        await _vm.LoadAsync();
        UpdateEmptyState();

        _vm.Connections.CollectionChanged += (_, _) => UpdateEmptyState();

        // 加载后自动测试所有连接状态
        foreach (var item in _vm.Connections.ToList())
            _ = _vm.TestConnectionCommand.ExecuteAsync(item);
    }

    private void UpdateEmptyState()
    {
        emptyState.Visibility = _vm.Connections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void NewConnection_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new EditConnectionDialog();
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            await _vm.SaveConnectionAsync(dialog.Result, isNew: true);
        }
    }

    private async void EditConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionCardItem item }) return;
        var dialog = new EditConnectionDialog(item.Model);
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            await _vm.SaveConnectionAsync(dialog.Result, isNew: false);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionCardItem item }) return;
        await _vm.TestConnectionCommand.ExecuteAsync(item);
    }

    private async void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionCardItem item }) return;

        var msg = $"确定要删除连接「{item.Model.Name}」吗？此操作不可撤销。";
        var taskCount = item.PendingDeleteTaskCount;
        if (taskCount > 0)
            msg = $"连接「{item.Model.Name}」关联了 {taskCount} 个任务，删除后任务将无法执行。\n\n确定删除吗？";

        var result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认删除",
            Content = msg,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        }.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            await _vm.ConfirmDeleteAsync(item);
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
