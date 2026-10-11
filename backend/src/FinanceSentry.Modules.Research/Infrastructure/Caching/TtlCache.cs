namespace FinanceSentry.Modules.Research.Infrastructure.Caching;

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// A bounded per-key result cache for the provider adapters. Each instance owns a size-limited
/// <see cref="MemoryCache"/> (the shared <see cref="IMemoryCache"/> has no limit and its entries carry
/// no size), so a long-running process cannot grow it past <c>maxEntries</c>. Freshness is judged on the
/// injected <see cref="TimeProvider"/>, so tests move time instead of sleeping; the cache's own
/// expiry only evicts. Concurrent misses on one key share a single in-flight fetch (other keys fetch
/// in parallel); the fetch is not tied to any one caller's token, each caller only stops waiting on its own.
/// </summary>
public sealed class TtlCache<TValue> : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly TimeSpan _ttl;
    private readonly MemoryCache _entries;
    private readonly ConcurrentDictionary<string, Lazy<Task<TValue>>> _inFlight = new();

    public TtlCache(TimeProvider clock, TimeSpan ttl, int maxEntries)
    {
        _clock = clock;
        _ttl = ttl;
        _entries = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
    }

    /// <summary>The cached value for <paramref name="key"/> while fresh; otherwise runs <paramref name="fetch"/>.</summary>
    /// <param name="fetch">Returns the value and whether it may be cached (a failure is not).</param>
    public async Task<TValue> GetOrFetchAsync(
        string key, Func<CancellationToken, Task<(TValue Value, bool Cacheable)>> fetch, CancellationToken ct)
    {
        if (TryGetFresh(key, out var hit))
        {
            return hit;
        }

        var flight = _inFlight.GetOrAdd(
            key.ToUpperInvariant(), normalized => new Lazy<Task<TValue>>(() => FetchAsync(normalized, key, fetch)));
        return await flight.Value.WaitAsync(ct);
    }

    public bool TryGetFresh(string key, out TValue value)
    {
        if (_entries.TryGetValue(key.ToUpperInvariant(), out Entry? entry)
            && entry is not null
            && _clock.GetUtcNow() - entry.StoredAt < _ttl)
        {
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(string key, TValue value)
    {
        using var cacheEntry = _entries.CreateEntry(key.ToUpperInvariant());
        cacheEntry.Value = new Entry(value, _clock.GetUtcNow());
        cacheEntry.Size = 1;
        cacheEntry.AbsoluteExpirationRelativeToNow = _ttl;
    }

    public void Dispose() => _entries.Dispose();

    private async Task<TValue> FetchAsync(
        string normalizedKey, string key, Func<CancellationToken, Task<(TValue Value, bool Cacheable)>> fetch)
    {
        try
        {
            if (TryGetFresh(key, out var hit))
            {
                return hit;
            }

            var (value, cacheable) = await fetch(CancellationToken.None);
            if (cacheable)
            {
                Set(key, value);
            }

            return value;
        }
        finally
        {
            _inFlight.TryRemove(normalizedKey, out _);
        }
    }

    private sealed record Entry(TValue Value, DateTimeOffset StoredAt);
}
