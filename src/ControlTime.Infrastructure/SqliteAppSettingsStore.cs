using ControlTime.Core.Abstractions;
using ControlTime.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTime.Infrastructure;

public class SqliteAppSettingsStore(IDbContextFactory<ControlTimeDbContext> factory) : IAppSettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entity = await context.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Key == key, cancellationToken);

        return entity?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await using ControlTimeDbContext context = factory.CreateDbContext();

        var entity = await context.AppSettings.FindAsync([key], cancellationToken);

        if (entity is null)
        {
            context.AppSettings.Add(new AppSettingEntity { Key = key, Value = value });
        }
        else
        {
            entity.Value = value;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
