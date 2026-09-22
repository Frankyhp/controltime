using System.Windows;
using System.Windows.Controls;
using ControlTime.App.ViewModels;

namespace ControlTime.App.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
    }

    private UsageHistoryViewModel? ViewModel => DataContext as UsageHistoryViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }
}
