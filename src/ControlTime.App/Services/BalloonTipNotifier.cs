using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.App.Services;

public sealed class BalloonTipNotifier : INotifier, IDisposable
{
    private const int TimeoutMilliseconds = 10_000;

    private readonly NotifyIcon _notifyIcon = new()
    {
        Icon = SystemIcons.Exclamation,
        Text = "ControlTime",
        Visible = false,
    };

    public void Notify(UsageWarning warning)
    {
        ArgumentNullException.ThrowIfNull(warning);

        string remaining = Format(warning.Remaining);

        string title = warning.Threshold.Minutes switch
        {
            >= 60 => "ControlTime — tiempo de uso casi agotado",
            _ => $"ControlTime — te quedan {remaining} de uso",
        };

        string message = warning.EffectiveDailyAllowance is TimeSpan allowance
            ? $"Solo te quedan {remaining} del tiempo permitido de hoy ({Format(allowance)})."
            : $"Solo te quedan {remaining} de tu tiempo de uso.";

        _notifyIcon.ShowBalloonTip(TimeoutMilliseconds, title, message, ToolTipIcon.Warning);
    }

    public void Dispose() => _notifyIcon.Dispose();

    private static string Format(TimeSpan time) =>
        string.Format(
            CultureInfo.GetCultureInfo("es-ES"),
            "{0}:{1:00}",
            (int)time.TotalHours,
            time.Minutes);
}
