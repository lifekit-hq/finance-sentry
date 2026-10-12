namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Process-wide cache for Monobank account snapshots (balance, credit limit, product/account
/// type) keyed by token-hash + externalAccountId. The /personal/client-info endpoint is
/// rate-limited to one call per 60 seconds per token, so the first per-account sync of a cycle
/// primes this and the sibling accounts' syncs read from it. Entries expire after the same
/// window so stale values can't linger past the rate-limit cooldown.
/// </summary>
public sealed class MonobankBalanceCache(TimeProvider clock) : IDisposable
{
    private const int MaxEntries = 10_000;
    private const int EntrySize = 1;

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    // A dedicated size-limited instance: the shared IMemoryCache has no SizeLimit and its other
    // entries carry no Size. The cache's own expiry only evicts; freshness is judged on `clock`.
    private readonly MemoryCache _entries = new(new MemoryCacheOptions { SizeLimit = MaxEntries });

    public void Set(string token, string externalAccountId, BankAccountInfo info)
    {
        var now = clock.GetUtcNow();
        Store(BuildKey(token, externalAccountId), new Entry(info, now));
        Store(BuildTokenPrefix(token), new Entry(null, now));
    }

    public BankAccountInfo? TryGet(string token, string externalAccountId)
    {
        if (_entries.TryGetValue(BuildKey(token, externalAccountId), out Entry? entry)
            && entry is not null
            && clock.GetUtcNow() - entry.StoredAt < Ttl)
        {
            return entry.Info;
        }
        return null;
    }

    /// <summary>
    /// Whether any account snapshot for this token is still fresh — i.e. client-info was called for
    /// the token within the TTL, so another call now would likely hit the rate limit.
    /// </summary>
    public bool HasFreshEntries(string token)
        => _entries.TryGetValue(BuildTokenPrefix(token), out Entry? marker)
            && marker is not null
            && clock.GetUtcNow() - marker.StoredAt < Ttl;

    public void Dispose() => _entries.Dispose();

    private void Store(string key, Entry entry)
    {
        using var cacheEntry = _entries.CreateEntry(key);
        cacheEntry.Value = entry;
        cacheEntry.Size = EntrySize;
        cacheEntry.AbsoluteExpirationRelativeToNow = Ttl;
    }

    private static string BuildKey(string token, string externalAccountId)
        => BuildTokenPrefix(token) + externalAccountId;

    private static string BuildTokenPrefix(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) + "|";

    // Info is null on the per-token marker, which only records when client-info last ran.
    private sealed record Entry(BankAccountInfo? Info, DateTimeOffset StoredAt);
}
