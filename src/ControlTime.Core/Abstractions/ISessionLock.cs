namespace ControlTime.Core.Abstractions;

public interface ISessionLock
{
    bool TryLock();
}
