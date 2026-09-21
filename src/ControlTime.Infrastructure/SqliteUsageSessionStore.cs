using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTime.Infrastructure;

public class SqliteUsageSessionStore(ControlTimeDbContext context) : IUsageSessionStore
{
    public async Task AddAsync(UsageSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        context.UsageSessions.Add(ToEntity(session));

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UsageSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        UsageSessionEntity? entity = await context.UsageSessions
            .FirstOrDefaultAsync(e => e.Id == session.Id, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Usage session '{session.Id}' does not exist.");
        }

        entity.StartedAtUtc = session.StartedAt.UtcDateTime;
        entity.EndedAtUtc = session.EndedAt?.UtcDateTime;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UsageSession>> GetSessionsOverlappingAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(to));
        }

        DateTime fromUtc = from.UtcDateTime;
        DateTime toUtc = to.UtcDateTime;

        var entities = await context.UsageSessions
            .AsNoTracking()
            .Where(s => s.StartedAtUtc < toUtc && (s.EndedAtUtc == null || s.EndedAtUtc > fromUtc))
            .ToListAsync(cancellationToken);

        List<UsageSession> sessions = [];

        foreach (UsageSessionEntity entity in entities)
        {
            sessions.Add(ToDomain(entity));
        }

        return sessions;
    }

    private static UsageSessionEntity ToEntity(UsageSession session) => new()
    {
        Id = session.Id,
        StartedAtUtc = session.StartedAt.UtcDateTime,
        EndedAtUtc = session.EndedAt?.UtcDateTime,
    };

    private static UsageSession ToDomain(UsageSessionEntity entity)
    {
        DateTimeOffset startedAt = new(entity.StartedAtUtc, TimeSpan.Zero);

        DomainResult result = entity.EndedAtUtc is DateTime endedAtUtc
            ? UsageSession.TryCreate(
                entity.Id,
                startedAt,
                new DateTimeOffset(endedAtUtc, TimeSpan.Zero),
                out var closed)
            : UsageSession.TryCreateOpen(entity.Id, startedAt, out closed);

        if (result.Failed)
        {
            throw new InvalidOperationException($"Usage session '{entity.Id}' has invalid persisted data.");
        }

        return closed!;
    }
}
