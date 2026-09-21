namespace ControlTime.Tests;

internal sealed class TestDbContextFactory : IDbContextFactory<ControlTimeDbContext>
{
    private readonly DbContextOptions<ControlTimeDbContext> _options;

    public TestDbContextFactory(string databasePath)
    {
        _options = new DbContextOptionsBuilder<ControlTimeDbContext>()
            .UseSqlite(ControlTimeStorage.GetConnectionString(databasePath))
            .Options;
    }

    public ControlTimeDbContext CreateDbContext() => new(_options);

    public ControlTimeDbContext CreateDbContext(string[] args) => CreateDbContext();

    public ValueTask<ControlTimeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => new(CreateDbContext());
}
