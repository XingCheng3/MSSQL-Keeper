using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using DBKeeper.App.ViewModels;

namespace DBKeeper.App.Views;

public partial class ExecutionLogsPage : Page
{
    private ExecutionLogsViewModel _vm = null!;

    public ExecutionLogsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<ExecutionLogsViewModel>();
        DataContext = _vm;
        logGrid.ItemsSource = _vm.Logs;
        await _vm.LoadAsync();
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e) => await _vm.PrevPageCommand.ExecuteAsync(null);
    private async void NextPage_Click(object sender, RoutedEventArgs e) => await _vm.NextPageCommand.ExecuteAsync(null);

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.FilterStatus = (cmbStatus.SelectedItem as ComboBoxItem)?.Tag as string;
        _vm.CurrentPage = 1;
        await _vm.LoadAsync();
    }

    private async void TaskFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.FilterTaskName = txtTaskFilter.Text;
        _vm.CurrentPage = 1;
        await _vm.LoadAsync();
    }

    private async void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.StartDate = dpStart.SelectedDate;
        _vm.EndDate = dpEnd.SelectedDate;
        _vm.CurrentPage = 1;
        await _vm.LoadAsync();
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        await _vm.ExportCsvCommand.ExecuteAsync(null);
    }
}
