using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class EnforcementService
{
    private readonly IAdminAuthService _adminAuthService;
    private readonly ISessionLock _sessionLock;

    private ActivityState? _lastActivity;
    private bool _lockPerformedForCurrentEpisode;

    public EnforcementService(IAdminAuthService adminAuthService, ISessionLock sessionLock)
    {
        ArgumentNullException.ThrowIfNull(adminAuthService);
        ArgumentNullException.ThrowIfNull(sessionLock);

        _adminAuthService = adminAuthService;
        _sessionLock = sessionLock;
    }

    public void Process(MonitorState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        ActivityState activity = state.Activity.State;
        bool newActivityEpisode = activity == ActivityState.Active && _lastActivity != ActivityState.Active;
        _lastActivity = activity;

        DashboardSnapshot snapshot = state.Snapshot;

        if (!snapshot.HasEnabledRules || snapshot.State != AccessState.Blocked)
        {
            _lockPerformedForCurrentEpisode = false;
            return;
        }

        if (_adminAuthService.IsAdminMode)
        {
            _lockPerformedForCurrentEpisode = false;
            return;
        }

        if (activity == ActivityState.Locked)
        {
            _lockPerformedForCurrentEpisode = true;
            return;
        }

        if (newActivityEpisode)
        {
            _lockPerformedForCurrentEpisode = false;
        }

        if (_lockPerformedForCurrentEpisode)
        {
            return;
        }

        if (!TryLock())
        {
            return;
        }

        _lockPerformedForCurrentEpisode = true;
    }

    private bool TryLock()
    {
        try
        {
            return _sessionLock.TryLock();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
