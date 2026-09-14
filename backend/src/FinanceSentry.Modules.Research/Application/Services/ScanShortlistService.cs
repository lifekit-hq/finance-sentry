namespace FinanceSentry.Modules.Research.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Stage 1 of the opportunity funnel (#558): composes the bounded shortlist stage 2 pays per-ticker
/// bar math for, out of three market-wide signals that need no bars at all — the index constituent
/// list, the quote read for the day's percent change, and the street's recent favourable actions —
/// with the EDGAR fundamentals grade applied last, to a slate the cheap signals already bounded.
///
/// Cost: the quote read covers the whole index, and <c>IMarketDataService</c> resolves it a ticker at
/// a time behind its cache, so a run costs one light quote fetch per constituent. That is the
/// cheapest market-wide momentum signal available today, but it is not a single batched call — a
/// genuinely batched quote endpoint would cut the request count by an order of magnitude.
///
/// Every upstream here degrades rather than throws: no quotes and no actions yields an empty
/// shortlist, which leaves the consuming universe at its core (held + watchlist + lenses) members
/// exactly as it stood before the funnel existed.
/// </summary>
public sealed class ScanShortlistService(
    IIndexConstituentSource constituents,
    IMarketDataService marketData,
    IAnalystActionRepository analystActions,
    ISecEdgarService secEdgar,
    IOptions<OpportunityOptions> options,
    ILogger<ScanShortlistService> logger) : IScanShortlistSource
{
    /// <summary>Street actions that read as a reason to look: an upgrade, new coverage, a raised target.</summary>
    private static readonly AnalystActionType[] FavourableActions =
        [AnalystActionType.Upgrade, AnalystActionType.Initiate, AnalystActionType.TargetChange];

    private readonly OpportunityOptions _options = options.Value;

    public async Task<IReadOnlyList<string>> GetShortlistAsync(CancellationToken ct = default)
    {
        var index = constituents.GetConstituents();
        if (index.Count == 0)
        {
            logger.LogWarning("Scan shortlist: the index constituent source is empty — no stage-1 universe to filter");
            return [];
        }

        var percentChange = await PercentChangeAsync(index, ct);
        var favourable = await FavourableActionCountsAsync(ct);

        var slate = ScanShortlistRules.SelectGradingSlate(index, percentChange, favourable, _options);
        if (slate.Count == 0)
        {
            logger.LogWarning(
                "Scan shortlist: no stage-1 signal over {Constituents} constituents (quotes {Quotes}, street actions " +
                "{Actions}) — the universe stays at its core members",
                index.Count, percentChange.Count, favourable.Count);
            return [];
        }

        var graded = await FundamentalsGrading.GradeAsync(secEdgar, logger, slate.Select(e => e.Ticker), ct);
        var shortlist = ScanShortlistRules.Compose(slate, graded, _options);

        logger.LogInformation(
            "Scan shortlist: {Constituents} constituents -> slate {Slate} (quotes {Quotes}, street actions " +
            "{Actions}) -> shortlist {Shortlist}; {Entries}",
            index.Count, slate.Count, percentChange.Count, favourable.Count, shortlist.Count,
            string.Join(", ", shortlist.Select(Format)));

        return [.. shortlist.Select(e => e.Ticker)];
    }

    private static string Format(ScanShortlistEntry entry)
        => FormattableString.Invariant(
            $"{entry.Ticker}(quality {entry.QualityScore}, surface {entry.SurfaceScore}, actions {entry.FavourableActions}, score {entry.ShortlistScore})");

    /// <summary>
    /// The day's percent change per constituent from the quote read. A quote with no usable
    /// previous close carries no change and simply does not rank — momentum is a percentile over the
    /// names that have one, so a partial quote read narrows the field rather than skewing it.
    /// </summary>
    private async Task<Dictionary<string, decimal>> PercentChangeAsync(
        IReadOnlyList<string> index, CancellationToken ct)
    {
        var changes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var quotes = await marketData.GetQuotesAsync(index, ct);
            foreach (var (ticker, quote) in quotes)
            {
                if (quote.PreviousClose is > 0m)
                {
                    changes[ticker] = 100m * (quote.Price - quote.PreviousClose.Value) / quote.PreviousClose.Value;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Scan shortlist: the quote read failed; ranking stage 1 on street actions only");
        }

        return changes;
    }

    /// <summary>
    /// Favourable street actions per ticker over the lookback window. A target change only counts as
    /// favourable when the new target is above the prior one — a cut is the same action type.
    /// </summary>
    private async Task<Dictionary<string, int>> FavourableActionCountsAsync(CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var since = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddDays(-Math.Max(0, _options.ScanShortlistActionLookbackDays));

        try
        {
            var actions = await analystActions.QueryAsync(
                ticker: null, since, actionType: null, Math.Max(0, _options.ScanShortlistActionLimit), ct);

            foreach (var action in actions.Where(IsFavourable))
            {
                var ticker = action.Ticker.Trim().ToUpperInvariant();
                counts[ticker] = counts.TryGetValue(ticker, out var count) ? count + 1 : 1;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Scan shortlist: the street-action read failed; ranking stage 1 on quotes only");
        }

        return counts;
    }

    private static bool IsFavourable(AnalystAction action)
        => FavourableActions.Contains(action.ActionType)
           && (action.ActionType != AnalystActionType.TargetChange
               || (action.NewTarget, action.PriorTarget) is ({ } raised, { } prior) && raised > prior);
}
