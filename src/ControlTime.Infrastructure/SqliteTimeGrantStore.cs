using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using ControlTime.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTime.Infrastructure;

public class SqliteTimeGrantStore(IDbContextFactory<ControlTimeDbContext> factory) : ITimeGrantStore
{
    public async Task AddAsync(TimeGrant grant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grant);

        await using ControlTimeDbContext context = factory.CreateDbContext();

        context.TimeGrants.Add(new TimeGrantEntity
        {
            Id = grant.Id,
            GrantedAtUtc = grant.GrantedAt.UtcDateTime,
            Minutes = grant.Minutes,
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TimeGrant>> GetGrantsForDayAsync(
        DateOnly day,
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default)
    {
        var dayStartUtc = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), utcOffset).UtcDateTime;
        var dayEndUtc = dayStartUtc.AddDays(1);

        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entities = await context.TimeGrants
            .AsNoTracking()
            .Where(g => g.GrantedAtUtc >= dayStartUtc && g.GrantedAtUtc < dayEndUtc)
            .ToListAsync(cancellationToken);

        List<TimeGrant> grants = [];

        foreach (var entity in entities)
        {
            var result = TimeGrant.TryCreate(
                entity.Id,
                new DateTimeOffset(entity.GrantedAtUtc, TimeSpan.Zero),
                entity.Minutes,
                out var grant);

            if (result.Failed)
            {
                throw new InvalidOperationException($"Time grant '{entity.Id}' has invalid persisted data.");
            }

            grants.Add(grant!);
        }

        return grants;
    }

    public async Task<IReadOnlyList<TimeGrant>> GetGrantsInRangeAsync(
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

        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entities = await context.TimeGrants
            .AsNoTracking()
            .Where(g => g.GrantedAtUtc >= fromUtc && g.GrantedAtUtc < toUtc)
            .ToListAsync(cancellationToken);

        List<TimeGrant> grants = [];

        foreach (var entity in entities)
        {
            var result = TimeGrant.TryCreate(
                entity.Id,
                new DateTimeOffset(entity.GrantedAtUtc, TimeSpan.Zero),
                entity.Minutes,
                out var grant);

            if (result.Failed)
            {
                throw new InvalidOperationException($"Time grant '{entity.Id}' has invalid persisted data.");
            }

            grants.Add(grant!);
        }

        return grants;
    }
}
