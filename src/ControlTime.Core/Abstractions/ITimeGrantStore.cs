using ControlTime.Core.Domain;

namespace ControlTime.Core.Abstractions;

public interface ITimeGrantStore
{
    Task AddAsync(TimeGrant grant, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimeGrant>> GetGrantsForDayAsync(
        DateOnly day,
        TimeSpan utcOffset,
        CancellationToken cancellationToken = default);
}
