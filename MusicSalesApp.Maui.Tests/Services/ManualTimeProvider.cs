namespace MusicSalesApp.Maui.Tests.Services;

/// <summary>
/// A clock the test drives by hand, so time-window behaviour can be asserted without sleeping.
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now += duration;
}
