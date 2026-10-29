using ControlTime.Infrastructure.Windows;

namespace ControlTime.Tests;

public class ConsoleSessionInputProbeTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private const uint Session = 1;

    [Fact]
    public void TryGetLastInputAt_SinResultadoPrevio_DevuelveFalsoYLanzaUnaSonda()
    {
        var probeStarted = new ManualResetEventSlim(false);
        int calls = 0;

        var probe = new ConsoleSessionInputProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                probeStarted.Set();
                return Base.AddSeconds(-30);
            },
            new FixedTimeProvider(Base));

        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(probeStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void ResultadoFresco_SeSirveDesdeCacheSinNuevaSonda()
    {
        var time = new FixedTimeProvider(Base);
        int calls = 0;
        var probe = new ConsoleSessionInputProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                return Base.AddSeconds(-30);
            },
            time);

        probe.ProbeOnce(Session);

        Assert.True(probe.TryGetLastInputAt(Session, out DateTimeOffset lastInputAt));
        Assert.Equal(Base.AddSeconds(-30), lastInputAt);
        // La única invocación fue la de ProbeOnce: TryGet sirvió la caché.
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void ResultadoCaducado_DevuelveFalsoYLanzaNuevaSonda()
    {
        var time = new FixedTimeProvider(Base);
        var probeStarted = new ManualResetEventSlim(false);
        var probe = new ConsoleSessionInputProbe(
            _ =>
            {
                probeStarted.Set();
                return Base.AddSeconds(-30);
            },
            time,
            resultLifetime: TimeSpan.FromSeconds(10));

        probe.ProbeOnce(Session);
        time.Now = Base.AddSeconds(11);
        probeStarted.Reset();

        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(probeStarted.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ResultadoDeOtraSesion_NoSeSirve()
    {
        var time = new FixedTimeProvider(Base);
        var probe = new ConsoleSessionInputProbe(
            _ => Base.AddSeconds(-30),
            time);

        probe.ProbeOnce(Session);

        Assert.False(probe.TryGetLastInputAt(Session + 1, out _));
    }

    [Fact]
    public void SondaEnCurso_NoSeLanzaUnaSegunda()
    {
        var release = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        int calls = 0;

        var probe = new ConsoleSessionInputProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                return Base.AddSeconds(-10);
            },
            new FixedTimeProvider(Base));

        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.False(probe.TryGetLastInputAt(Session, out _));

        release.Set();
        Assert.True(WaitUntil(() => probe.TryGetLastInputAt(Session, out _)));
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public void SondaFallona_SigueDevolviendoFalsoYSerepiteTrasElIntervalo()
    {
        var time = new FixedTimeProvider(Base);
        var firstProbeDone = new ManualResetEventSlim(false);
        int calls = 0;

        var probe = new ConsoleSessionInputProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                firstProbeDone.Set();
                return null;
            },
            time,
            probeInterval: TimeSpan.FromSeconds(5));

        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(firstProbeDone.Wait(TimeSpan.FromSeconds(5)));

        // Dentro del intervalo de sondeo no se lanza otra.
        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.Equal(1, Volatile.Read(ref calls));

        time.Now = Base.AddSeconds(6);
        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(WaitUntil(() => Volatile.Read(ref calls) == 2));
    }

    [Fact]
    public void IntervaloDeSonda_RespetadoAunqueElResultadoCaduqueAntes()
    {
        var time = new FixedTimeProvider(Base);
        int calls = 0;
        var firstProbeDone = new ManualResetEventSlim(false);

        var probe = new ConsoleSessionInputProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                firstProbeDone.Set();
                return Base.AddSeconds(-30);
            },
            time,
            probeInterval: TimeSpan.FromSeconds(5),
            resultLifetime: TimeSpan.FromSeconds(3));

        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(firstProbeDone.Wait(TimeSpan.FromSeconds(5)));

        // Resultado fresco: se sirve sin sondear.
        time.Now = Base.AddSeconds(2);
        Assert.True(probe.TryGetLastInputAt(Session, out _));
        Assert.Equal(1, Volatile.Read(ref calls));

        // Resultado caducado pero dentro del intervalo de sondeo: no se sondea aún.
        time.Now = Base.AddSeconds(4);
        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.Equal(1, Volatile.Read(ref calls));

        // Pasado el intervalo desde la última sonda: se lanza una nueva.
        time.Now = Base.AddSeconds(6);
        Assert.False(probe.TryGetLastInputAt(Session, out _));
        Assert.True(WaitUntil(() => Volatile.Read(ref calls) == 2));
    }

    [Fact]
    public void Constructor_RechazaIntervalosNoPositivos()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConsoleSessionInputProbe(probeInterval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConsoleSessionInputProbe(resultLifetime: TimeSpan.Zero));
    }

    private static bool WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }
}
