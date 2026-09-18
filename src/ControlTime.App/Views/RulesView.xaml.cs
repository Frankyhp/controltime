using System.Windows;
using System.Windows.Controls;
using ControlTime.App.ViewModels;

namespace ControlTime.App.Views;

public partial class RulesView : UserControl
{
    public RulesView()
    {
        InitializeComponent();
    }

    private RulesViewModel? ViewModel => DataContext as RulesViewModel;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel?.Stop();

    private async void OnSetupPasswordClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.SetupPasswordAsync(SetupPasswordBox.Password, SetupConfirmPasswordBox.Password);
            SetupPasswordBox.Clear();
            SetupConfirmPasswordBox.Clear();
        }
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.UnlockAsync(UnlockPasswordBox.Password);
            UnlockPasswordBox.Clear();
        }
    }

    private async void OnGrantConfirmClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel)
        {
            await viewModel.ConfirmGrantAsync(GrantPasswordBox.Password);
            GrantPasswordBox.Clear();
        }
    }
}
