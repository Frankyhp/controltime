using ControlTime.Core.Abstractions;
using ControlTime.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ControlTime.Infrastructure;

public class SqliteUsageRuleStore(
    IDbContextFactory<ControlTimeDbContext> factory,
    ILogger<SqliteUsageRuleStore>? logger = null) : IUsageRuleStore
{
    public async Task<IReadOnlyList<UsageRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entities = await context.UsageRules.AsNoTracking().ToListAsync(cancellationToken);

        List<UsageRule> rules = [];

        foreach (var entity in entities)
        {
            try
            {
                rules.Add(UsageRuleMapper.ToDomain(entity));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(
                    ex,
                    "Se ha ignorado la regla persistida '{RuleId}' ({RuleName}) por contener datos inválidos.",
                    entity.Id,
                    entity.Name);
            }
        }

        return rules;
    }

    public async Task AddAsync(UsageRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using ControlTimeDbContext context = factory.CreateDbContext();

        context.UsageRules.Add(UsageRuleMapper.ToEntity(rule));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(UsageRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entity = await context.UsageRules.FindAsync([rule.Id], cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Usage rule '{rule.Id}' was not found.");
        }

        var mapped = UsageRuleMapper.ToEntity(rule);
        context.Entry(entity).CurrentValues.SetValues(mapped);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entity = await context.UsageRules.FindAsync([id], cancellationToken);

        if (entity is null)
        {
            return false;
        }

        context.UsageRules.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
