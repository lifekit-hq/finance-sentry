namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Composes the analyst-actions universe: checked-in index seed ∪ equity holdings ∪ watchlist ∪ open
/// opportunity candidates, following the Radar compose-and-deactivate pattern. Departed members flip
/// inactive (never deleted). Breadth beyond the book is a requirement (FR-002).
/// </summary>
public sealed class AnalystUniverseService(
    IAnalystUniverseRepository universe,
    ResearchDbContext research,
    IBrokerageHoldingsReader brokerage,
    IBankingTotalsReader banking,
    IIndexConstituentSource constituents,
    ILogger<AnalystUniverseService> logger) : IAnalystUniverseService
{
    private const string EquityInstrumentType = "STK";

    public async Task<IReadOnlyList<AnalystUniverseMember>> SyncAsync(CancellationToken ct = default)
    {
        var resolved = new Dictionary<string, UniverseReason>(StringComparer.OrdinalIgnoreCase);

        // Ownership reasons win over IndexConstituent (added last, first-write-wins).
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        foreach (var userId in userIds)
        {
            foreach (var holding in await brokerage.GetHoldingsAsync(userId, ct))
            {
                if (string.Equals(holding.InstrumentType, EquityInstrumentType, StringComparison.OrdinalIgnoreCase))
                {
                    Add(resolved, holding.Symbol, UniverseReason.Holding);
                }
            }
        }

        var watchlist = await research.WatchlistItems.AsNoTracking().Select(w => w.Ticker).ToListAsync(ct);
        foreach (var ticker in watchlist)
        {
            Add(resolved, ticker, UniverseReason.Watchlist);
        }

        var candidateTickers = await research.OpportunityCandidates.AsNoTracking()
            .Where(c => c.Status == CandidateStatus.Active)
            .Select(c => c.Ticker)
            .ToListAsync(ct);
        foreach (var ticker in candidateTickers)
        {
            Add(resolved, ticker, UniverseReason.Candidate);
        }

        foreach (var ticker in constituents.GetConstituents())
        {
            Add(resolved, ticker, UniverseReason.IndexConstituent);
        }

        var members = resolved
            .Select(kv => new AnalystUniverseMember { Ticker = kv.Key, Reason = kv.Value, Active = true })
            .ToList();
        await universe.UpsertMembersAsync(members, ct);

        var all = await universe.ListAllAsync(ct);
        var departed = all
            .Where(m => m.Active && !resolved.ContainsKey(m.Ticker) && m.Reason != UniverseReason.Manual)
            .Select(m => m.Ticker)
            .ToList();
        if (departed.Count > 0)
        {
            await universe.DeactivateAsync(departed, ct);
        }

        var active = await universe.ListActiveAsync(ct);
        logger.LogInformation("Analyst universe synced: {Active} active members ({Departed} deactivated)",
            active.Count, departed.Count);
        return active;
    }

    private static void Add(Dictionary<string, UniverseReason> resolved, string ticker, UniverseReason reason)
    {
        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.Length is > 0 and <= 12)
        {
            resolved.TryAdd(normalized, reason);
        }
    }
}
