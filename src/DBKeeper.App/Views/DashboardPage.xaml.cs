using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.ViewModels;

namespace DBKeeper.App.Views;

public partial class DashboardPage : Page
{
    private DashboardViewModel _vm = null!;

    public DashboardPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext != null)
        {
            await ((DashboardViewModel)DataContext).LoadAsync();
            return;
        }

        _vm = App.Services.GetRequiredService<DashboardViewModel>();
        DataContext = _vm;
        recentGrid.ItemsSource = _vm.RecentLogs;
        upcomingList.ItemsSource = _vm.UpcomingTasks;
        connectionStatusList.ItemsSource = _vm.Connections;
        await _vm.LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _vm.LoadAsync();
    }

    private void ScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta);
        e.Handled = true;
    }
}
