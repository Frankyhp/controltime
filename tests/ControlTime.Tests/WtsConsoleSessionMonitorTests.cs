using ControlTime.Infrastructure.Windows;

namespace ControlTime.Tests;

public class WtsConsoleSessionMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);

    [Fact]
    public void EvaluateActive_SinSonda_MantieneOptimismo()
    {
        ActivityStatus status = WtsConsoleSessionMonitor.EvaluateActive(Now, null, IdleThreshold);

        Assert.Equal(ActivityState.Active, status.State);
        Assert.Equal(Now, status.LastInputAt);
    }

    [Fact]
    public void EvaluateActive_ConSondaReciente_ActiveConUltimaEntradaReal()
    {
        DateTimeOffset lastInput = Now.AddSeconds(-30);

        ActivityStatus status =
            WtsConsoleSessionMonitor.EvaluateActive(Now, lastInput, IdleThreshold);

        Assert.Equal(ActivityState.Active, status.State);
        Assert.Equal(lastInput, status.LastInputAt);
    }

    [Fact]
    public void EvaluateActive_UltimaEntradaAntigua_IdleCierraEnLaEntradaReal()
    {
        DateTimeOffset lastInput = Now - IdleThreshold;

        ActivityStatus status =
            WtsConsoleSessionMonitor.EvaluateActive(Now, lastInput, IdleThreshold);

        Assert.Equal(ActivityState.Idle, status.State);
        Assert.Equal(lastInput, status.LastInputAt);
    }

    [Fact]
    public void EvaluateActive_SondaFutura_SeRecortaAlAhora()
    {
        ActivityStatus status = WtsConsoleSessionMonitor.EvaluateActive(
            Now,
            Now.AddSeconds(30),
            IdleThreshold);

        Assert.Equal(ActivityState.Active, status.State);
        Assert.Equal(Now, status.LastInputAt);
    }
}
