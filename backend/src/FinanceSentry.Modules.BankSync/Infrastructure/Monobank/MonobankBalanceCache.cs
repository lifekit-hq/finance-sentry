namespace FinanceSentry.Modules.BankSync.Infrastructure.Monobank;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Modules.BankSync.Domain.Interfaces;

/// <summary>
/// Process-wide cache for Monobank account snapshots (balance, credit limit, product/account
/// type) keyed by token-hash + externalAccountId. The /personal/client-info endpoint is
/// rate-limited to one call per 60 seconds per token, so the first per-account sync of a cycle
/// primes this and the sibling accounts' syncs read from it. Entries expire after the same
/// window so stale values can't linger past the rate-limit cooldown.
/// </summary>
public sealed class MonobankBalanceCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    public void Set(string token, string externalAccountId, BankAccountInfo info)
    {
        _entries[BuildKey(token, externalAccountId)] = new Entry(info, DateTime.UtcNow);
    }

    public BankAccountInfo? TryGet(string token, string externalAccountId)
    {
        if (_entries.TryGetValue(BuildKey(token, externalAccountId), out var entry)
            && DateTime.UtcNow - entry.StoredAt < Ttl)
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
    {
        var prefix = BuildTokenPrefix(token);
        var now = DateTime.UtcNow;
        return _entries.Any(e => e.Key.StartsWith(prefix, StringComparison.Ordinal) && now - e.Value.StoredAt < Ttl);
    }

    private static string BuildKey(string token, string externalAccountId)
        => BuildTokenPrefix(token) + externalAccountId;

    private static string BuildTokenPrefix(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))) + "|";

    private readonly record struct Entry(BankAccountInfo Info, DateTime StoredAt);
}
