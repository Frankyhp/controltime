using ControlTime.Core.Domain;

namespace ControlTime.Core.Abstractions;

public interface INotifier
{
    void Notify(UsageWarning warning);
}
