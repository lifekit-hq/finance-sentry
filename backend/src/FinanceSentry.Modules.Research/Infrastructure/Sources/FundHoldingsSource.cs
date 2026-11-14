namespace FinanceSentry.Modules.Research.Infrastructure.Sources;

using FinanceSentry.Modules.Research.Application.Services.Etf;
using FinanceSentry.Modules.Research.Infrastructure.Caching;

/// <summary>
/// <see cref="IFundHoldingsSource"/>: Yahoo first, the issuer's holdings file when Yahoo lists none, each fund's
/// answer cached for a day (issue #1324). A read that could not tell what the symbol is (<see cref="FundKind.Unknown"/>)
/// is not cached, so the next request retries; "not a fund" and "a fund with no holdings" are, since both are answers.
/// </summary>
public sealed class FundHoldingsSource(
    IFundHoldingsProvider primary,
    IFundHoldingsProvider fallback,
    TimeProvider clock) : IFundHoldingsSource, IDisposable
{
    private const int MaxCachedFunds = 2_000;

    private static readonly TimeSpan Ttl = TimeSpan.FromDays(1);

    private readonly TtlCache<FundHoldingsLookup> cache = new(clock, Ttl, MaxCachedFunds);

    public Task<FundHoldingsLookup> GetTopHoldingsAsync(string symbol, CancellationToken ct = default) =>
        cache.GetOrFetchAsync(symbol.Trim(), async token =>
        {
            var lookup = await ReadAsync(symbol.Trim(), token);
            return (lookup, lookup.Kind != FundKind.Unknown);
        }, ct);

    public void Dispose() => cache.Dispose();

    private async Task<FundHoldingsLookup> ReadAsync(string symbol, CancellationToken ct)
    {
        var lookup = await primary.LookupAsync(symbol, ct);
        if (lookup.Kind == FundKind.NotAFund || lookup.Holdings.Count > 0)
        {
            return lookup;
        }

        // Yahoo listed nothing (or failed): the issuer's file may know the fund. A mapped file makes it a fund.
        var issuer = await fallback.LookupAsync(symbol, ct);
        return issuer.Holdings.Count > 0 ? issuer : lookup;
    }
}
