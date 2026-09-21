using ControlTime.Core.Domain;

namespace ControlTime.Core.Abstractions;

public interface IUsageSessionStore
{
    Task AddAsync(UsageSession session, CancellationToken cancellationToken = default);

    Task UpdateAsync(UsageSession session, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UsageSession>> GetSessionsOverlappingAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<UsageSession?> GetOpenSessionAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
