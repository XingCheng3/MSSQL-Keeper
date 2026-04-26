using System.Windows;
using System.Windows.Controls;
using DBKeeper.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DBKeeper.App.Views;

public partial class StorageAnalysisPage : Page
{
    private StorageAnalysisViewModel _vm = null!;

    public StorageAnalysisPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<StorageAnalysisViewModel>();
        DataContext = _vm;
        await _vm.LoadAsync();
    }
}
