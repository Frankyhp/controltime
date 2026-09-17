using ControlTime.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTime.Infrastructure;

public class ControlTimeDbContext(DbContextOptions<ControlTimeDbContext> options) : DbContext(options)
{
    public DbSet<UsageRuleEntity> UsageRules => Set<UsageRuleEntity>();

    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UsageRuleEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.AllowedDays).IsRequired();
        });

        modelBuilder.Entity<AppSettingEntity>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).IsRequired();
            entity.Property(e => e.Value).IsRequired();
        });
    }
}
