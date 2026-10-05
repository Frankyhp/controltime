using System.Windows;
using System.Windows.Input;

namespace ControlTime.App.Views;

/// <summary>
/// Interaction logic for TimerOverlayWindow.xaml
/// </summary>
public partial class TimerOverlayWindow : Window
{
    private bool _movedSinceLastSave;

    public TimerOverlayWindow()
    {
        InitializeComponent();

        ShowActivated = false;
    }

    public event Action? DragCompleted;

    public bool MovedSinceLastSave => _movedSinceLastSave;

    public void SetRemainingText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        RemainingTextBlock.Text = text;
    }

    public void MarkPositionSaved() => _movedSinceLastSave = false;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();

            _movedSinceLastSave = true;
            DragCompleted?.Invoke();
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (IsVisible)
        {
            Topmost = true;
        }
    }
}
