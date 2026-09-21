namespace ControlTime.Core.Abstractions;

public interface IAutostartRegistrar
{
    void Enable();

    void Disable();

    bool IsEnabled();
}
