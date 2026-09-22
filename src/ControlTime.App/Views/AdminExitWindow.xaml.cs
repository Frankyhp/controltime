using System.Windows;
using ControlTime.Core.Services;

namespace ControlTime.App.Views;

/// <summary>
/// Diálogo mínimo de salida controlada: la contraseña solo se verifica,
/// nunca activa el modo administrador.
/// </summary>
public partial class AdminExitWindow : Window
{
    private readonly IAdminAuthService _adminAuthService;

    public AdminExitWindow(IAdminAuthService adminAuthService)
    {
        ArgumentNullException.ThrowIfNull(adminAuthService);

        _adminAuthService = adminAuthService;

        InitializeComponent();

        Loaded += (_, _) => PasswordBoxControl.Focus();
    }

    private async void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        string password = PasswordBoxControl.Password;

        bool verified = await _adminAuthService.VerifyPasswordAsync(password);

        if (verified)
        {
            DialogResult = true;
            return;
        }

        PasswordBoxControl.Clear();
        PasswordBoxControl.Focus();

        ErrorText.Text = "Contraseña incorrecta o demasiados intentos. Espera unos segundos.";
        ErrorText.Visibility = Visibility.Visible;
    }
}
