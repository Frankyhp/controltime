using System.ComponentModel;
using System.Windows;
using ControlTime.App.ViewModels;

namespace ControlTime.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _closingForShutdown;

    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        DataContext = viewModel;
    }

    public void CloseForShutdown()
    {
        _closingForShutdown = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingForShutdown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
