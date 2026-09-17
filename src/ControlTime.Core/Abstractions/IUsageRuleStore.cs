using ControlTime.Core.Domain;

namespace ControlTime.Core.Abstractions;

public interface IUsageRuleStore
{
    Task<IReadOnlyList<UsageRule>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(UsageRule rule, CancellationToken cancellationToken = default);

    Task UpdateAsync(UsageRule rule, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}
