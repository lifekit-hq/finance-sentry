namespace FinanceSentry.Modules.Research.Tests.Unit;

/// <summary>A clock tests move by hand, so cache expiry is exercised without sleeping.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
