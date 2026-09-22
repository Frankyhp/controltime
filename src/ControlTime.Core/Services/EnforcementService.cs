using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class EnforcementService
{
    public static readonly TimeSpan ReLockGracePeriod = TimeSpan.FromSeconds(30);

    private readonly IAdminAuthService _adminAuthService;
    private readonly ISessionLock _sessionLock;
    private readonly TimeProvider _timeProvider;

    private ActivityState? _lastActivity;
    private bool _lockPerformedForCurrentEpisode;
    private DateTimeOffset? _relockAt;

    public EnforcementService(
        IAdminAuthService adminAuthService,
        ISessionLock sessionLock,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(adminAuthService);
        ArgumentNullException.ThrowIfNull(sessionLock);

        _adminAuthService = adminAuthService;
        _sessionLock = sessionLock;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
            _relockAt = null;
            return;
        }

        if (_adminAuthService.IsAdminMode)
        {
            _lockPerformedForCurrentEpisode = false;
            _relockAt = null;
            return;
        }

        if (activity == ActivityState.Locked)
        {
            _lockPerformedForCurrentEpisode = true;
            _relockAt = null;
            return;
        }

        if (newActivityEpisode)
        {
            _lockPerformedForCurrentEpisode = false;
            _relockAt = _timeProvider.GetUtcNow() + ReLockGracePeriod;
        }

        if (_lockPerformedForCurrentEpisode)
        {
            return;
        }

        if (_relockAt is DateTimeOffset relockAt && _timeProvider.GetUtcNow() < relockAt)
        {
            return;
        }

        if (!TryLock())
        {
            return;
        }

        _lockPerformedForCurrentEpisode = true;
        _relockAt = null;
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
