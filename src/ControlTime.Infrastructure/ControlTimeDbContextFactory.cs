using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ControlTime.Infrastructure;

public sealed class ControlTimeDbContextFactory : IDesignTimeDbContextFactory<ControlTimeDbContext>
{
    public ControlTimeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ControlTimeDbContext>()
            .UseSqlite(ControlTimeStorage.GetConnectionString())
            .Options;

        return new ControlTimeDbContext(options);
    }
}
