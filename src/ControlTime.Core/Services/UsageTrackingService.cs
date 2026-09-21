using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;

namespace ControlTime.Core.Services;

public sealed class UsageTrackingService(IUsageSessionStore sessionStore)
{
    private readonly IUsageSessionStore _sessionStore = sessionStore
        ?? throw new ArgumentNullException(nameof(sessionStore));

    private UsageSession? _openSession;
    private bool _loaded;
    private bool _ticking;

    public UsageSession? OpenSession => _openSession;

    public async Task TickAsync(ActivityStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (_ticking)
        {
            return;
        }

        _ticking = true;
        try
        {
            await TrackAsync(status, cancellationToken);
        }
        finally
        {
            _ticking = false;
        }
    }

    private async Task TrackAsync(ActivityStatus status, CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            _openSession = await _sessionStore.GetOpenSessionAsync(cancellationToken);
            _loaded = true;
        }

        if (status.State == ActivityState.Active)
        {
            if (_openSession is null)
            {
                UsageSession.TryCreateOpen(status.Timestamp, out UsageSession? session);

                _openSession = session!;
                await _sessionStore.AddAsync(session!, cancellationToken);
            }

            return;
        }

        if (_openSession is UsageSession open)
        {
            _openSession = null;

            DateTimeOffset closeAt = open.StartedAt > status.LastInputAt
                ? open.StartedAt
                : status.LastInputAt;

            if (open.TryClose(closeAt, out UsageSession? closed).Success)
            {
                await _sessionStore.UpdateAsync(closed!, cancellationToken);
            }
            else
            {
                await _sessionStore.DeleteAsync(open.Id, cancellationToken);
            }
        }
    }
}
