using System.Windows;
using System.Windows.Controls;
using ControlTime.App.ViewModels;

namespace ControlTime.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel?.Stop();
}
