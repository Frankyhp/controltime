namespace ControlTime.Tests;

public sealed class InMemoryAppSettingsStore : IAppSettingsStore
{
    public Dictionary<string, string> RawValues { get; } = [];

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(RawValues.GetValueOrDefault(key));

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        RawValues[key] = value;
        return Task.CompletedTask;
    }
}
