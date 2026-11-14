namespace FinanceSentry.Modules.Research.Application.Services.Catalyst;

/// <summary>
/// Spaces requests to one provider at least <c>interval</c> apart, however many callers share it: the SEC's fair-access policy
/// and GDELT's one-request-per-five-seconds rule are both rates, not concurrency caps. The time source is injected so a test
/// proves the spacing without sleeping.
/// </summary>
public sealed class RequestPacer(TimeSpan interval, TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset? _last;

    /// <summary>A pacer for <paramref name="perSecond"/> requests a second.</summary>
    public static RequestPacer PerSecond(int perSecond, TimeProvider clock)
        => new(TimeSpan.FromSeconds(1d / Math.Max(1, perSecond)), clock);

    /// <summary>Waits until the next request is allowed, then claims the slot.</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_last is { } last)
            {
                var wait = last + interval - clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, clock, ct);
                }
            }

            _last = clock.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>Keys of the request pacers the catalyst detection shares per provider (keyed singletons).</summary>
public static class CatalystPacers
{
    public const string Sec = "catalyst-sec";
    public const string Gdelt = "catalyst-gdelt";
}
