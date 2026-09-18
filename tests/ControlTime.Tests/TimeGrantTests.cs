namespace ControlTime.Tests;

public class TimeGrantTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-120)]
    public void TryCreate_NonPositiveMinutes_Invalid(int minutes)
    {
        var result = TimeGrant.TryCreate(
            new DateTimeOffset(2026, 6, 8, 12, 0, 0, TimeSpan.Zero),
            minutes,
            out var grant);

        Assert.True(result.Failed);
        Assert.Null(grant);
    }

    [Fact]
    public void TryCreate_PositiveMinutes_ExposesFieldsAndAmount()
    {
        var grantedAt = new DateTimeOffset(2026, 6, 8, 12, 30, 0, TimeSpan.FromHours(2));
        var id = Guid.NewGuid();

        var result = TimeGrant.TryCreate(id, grantedAt, 45, out var grant);

        Assert.True(result.Success);
        Assert.NotNull(grant);
        Assert.Equal(id, grant.Id);
        Assert.Equal(grantedAt, grant.GrantedAt);
        Assert.Equal(45, grant.Minutes);
        Assert.Equal(TimeSpan.FromMinutes(45), grant.Amount);
    }
}
