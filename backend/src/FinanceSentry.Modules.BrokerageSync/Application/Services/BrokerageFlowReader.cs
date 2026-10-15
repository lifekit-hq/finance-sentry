namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Measures;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// <see cref="IBrokerageFlowReader"/> over the cash rows IBKR (Flex) and Inzhur report, classified by
/// <see cref="BrokerageFlowClassifier"/> and converted to USD at each row's own date (rule 5: today's rate would
/// read a currency move as growth). A provider's link day is its earliest active connection's creation day, the
/// same time <see cref="BrokerageScopeChangeReader"/> reports; rows on or before it are history the linked value
/// already holds. A connection records no deactivation time, so a removed provider is not seen.
/// </summary>
public sealed class BrokerageFlowReader(BrokerageSyncDbContext db, IHistoricalExchangeRateService fx) : IBrokerageFlowReader
{
    public async Task<BrokerageFlowReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default)
    {
        var links = await LinkDaysAsync(userId, ct);
        if (links.Count == 0)
            return BrokerageFlowReading.Empty;

        var rows = await db.BrokerageCashTransactions.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Provider, c.TransactionType, c.Amount, c.Currency, c.DateTime })
            .ToListAsync(ct);
        var classified = rows
            .Select(r => new
            {
                Source = r.Provider.ToLowerInvariant(),
                Class = BrokerageFlowClassifier.Classify(r.Provider, r.TransactionType, r.Amount),
                r.Amount,
                r.Currency,
                Date = DateOnly.FromDateTime(r.DateTime),
            })
            .Where(r => r.Class is not null && links.ContainsKey(r.Source))
            .ToList();

        var sources = links
            .Select(l => new FlowSourceCoverage(
                l.Key, l.Value,
                classified.Any(r => r.Source == l.Key && BrokerageFlowClassifier.IsExternal(r.Class))))
            .OrderBy(s => s.Source, StringComparer.Ordinal)
            .ToList();

        var flows = new List<SourceFlow>();
        foreach (var row in classified.Where(r => r.Date > from && r.Date > links[r.Source]).OrderBy(r => r.Date))
        {
            var usd = await fx.ConvertToUsdAsync(Math.Abs(row.Amount), row.Currency, row.Date, ct);
            flows.Add(new SourceFlow(row.Source, new ClassifiedFlow(row.Date, row.Class!.Value, usd.Amount), usd.Status));
        }

        return new BrokerageFlowReading(sources, flows);
    }

    // The IBKR OAuth and Flex connections read one account, so IBKR's link day is the earlier of the two.
    private async Task<Dictionary<string, DateOnly>> LinkDaysAsync(Guid userId, CancellationToken ct)
    {
        var ibkr = await db.IBKRCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var flex = await db.IBKRFlexCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId && c.IsActive)
            .Select(c => c.CreatedAt).ToListAsync(ct);
        var inzhur = await db.InzhurCredentials.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(c => c.UserId == userId)
            .Select(c => c.CreatedAt).ToListAsync(ct);

        var links = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        AddEarliest(links, BrokerageFlowClassifier.Ibkr, [.. ibkr, .. flex]);
        AddEarliest(links, BrokerageFlowClassifier.Inzhur, inzhur);
        return links;
    }

    private static void AddEarliest(Dictionary<string, DateOnly> links, string source, List<DateTime> createdAt)
    {
        if (createdAt.Count > 0)
            links[source] = DateOnly.FromDateTime(createdAt.Min());
    }
}
