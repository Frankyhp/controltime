using ControlTime.Core.Domain;

namespace ControlTime.Core.Abstractions;

public interface IActivityMonitor
{
    ActivityStatus GetCurrentStatus();
}
