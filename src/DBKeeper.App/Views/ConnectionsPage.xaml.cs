using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.Dialogs;
using DBKeeper.App.ViewModels;

namespace DBKeeper.App.Views;

public partial class ConnectionsPage : Page
{
    private ConnectionsViewModel _vm = null!;
    private bool _isCollectionChangedBound;

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

        if (!_isCollectionChangedBound)
        {
            _vm.Connections.CollectionChanged += (_, _) => UpdateEmptyState();
            _isCollectionChangedBound = true;
        }
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

    private async void TestAllConnections_Click(object sender, RoutedEventArgs e)
    {
        await _vm.TestAllConnectionsAsync();
    }

    private async void DeleteConnection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConnectionCardItem item }) return;

        var impact = await _vm.GetDeleteImpactAsync(item);
        var msg = $"确定要删除连接「{item.Model.Name}」吗？此操作不可撤销。";
        if (impact.TaskCount > 0)
        {
            var preview = string.Join("、", impact.TaskNames.Take(5));
            var suffix = impact.TaskNames.Count > 5 ? " 等" : string.Empty;
            msg = $"连接「{item.Model.Name}」关联了 {impact.TaskCount} 个任务，删除后这些任务将变为未绑定连接。\n\n受影响任务：{preview}{suffix}\n\n确定删除吗？";
        }

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
