namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Aggregate dossier for one symbol: fans out to all per-ticker data sources in parallel and
/// composes the result. Each section degrades to null / empty on failure — never throws (feature 421).
/// </summary>
public sealed record GetAssetDossierQuery(Guid UserId, string Symbol) : IQuery<AssetDossierResult>;

public sealed class GetAssetDossierQueryHandler(
    IBookFiguresService bookFigures,
    IHoldingTaxLotsReader taxLotsReader,
    IAssetSignalReader signalReader,
    IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>> theses,
    IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult> valuation,
    IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult> analystActions,
    IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>> news,
    IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>> earnings)
    : IQueryHandler<GetAssetDossierQuery, AssetDossierResult>
{
    private const int NewsLimit = 10;
    private const int SignalLimit = 50;
    private const int AnalystActionLimit = 20;
    private const int AnalystActionDays = 90;
    private const int EarningsDays = 90;

    public async Task<AssetDossierResult> Handle(GetAssetDossierQuery request, CancellationToken ct)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var userId = request.UserId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Fan-out all source reads in parallel; each is wrapped so one failure cannot 500 the rest.
        var bookTask = Safe(() => bookFigures.ReadAsync(userId, ct));
        var thesesTask = Safe(() => theses.Handle(new GetThesesQuery(userId), ct));
        var valuationTask = Safe(() => valuation.Handle(new GetValuationSnapshotQuery(symbol, null), ct));
        var analystTask = Safe(() => analystActions.Handle(
            new GetAnalystActionsQuery(symbol, today.AddDays(-AnalystActionDays), null, AnalystActionLimit), ct));
        var newsTask = Safe(() => news.Handle(new GetNewsForTickerQuery(symbol, null, NewsLimit), ct));
        var earningsTask = Safe(() => earnings.Handle(
            new GetEarningsCalendarQuery([symbol], null, today, today.AddDays(EarningsDays), null), ct));
        var signalsTask = Safe(() => signalReader.GetRecentAsync(symbol, SignalLimit, ct));

        await Task.WhenAll(bookTask, thesesTask, valuationTask, analystTask, newsTask, earningsTask, signalsTask);

        var book = await bookTask;
        var allTheses = await thesesTask;
        var valuationResult = await valuationTask;
        var analystResult = await analystTask;
        var newsResult = await newsTask;
        var earningsResult = await earningsTask;
        var signalResult = await signalsTask;

        // Position section: find symbol in the book, then fetch tax lots for brokerage positions.
        var position = await BuildPositionSectionAsync(userId, symbol, book, ct);

        // Thesis: first matching ticker (user-scoped, case-insensitive).
        var thesis = allTheses?
            .FirstOrDefault(t => t.Ticker.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        // Analysts: null when not in universe or no actions found.
        var analysts = BuildAnalystsSection(analystResult);

        // Valuation: null when the query failed; keep the result even when NotApplicable so UI
        // can distinguish "no data" from "not applicable for crypto".
        // (Null = source failure; non-null with NotApplicable = crypto.)

        return new AssetDossierResult(
            Symbol: symbol,
            Position: position,
            Thesis: thesis,
            Valuation: valuationResult,
            Analysts: analysts,
            RecentNews: newsResult ?? [],
            NextEarnings: earningsResult?
                .Where(e => e.EventDate >= today)
                .OrderBy(e => e.EventDate)
                .FirstOrDefault(),
            RadarSignals: signalResult ?? [],
            GeneratedAt: DateTimeOffset.UtcNow);
    }

    private async Task<DossierPositionSection?> BuildPositionSectionAsync(
        Guid userId, string symbol, BookFigures? book, CancellationToken ct)
    {
        if (book is null)
        {
            return null;
        }

        var pos = book.Positions.FirstOrDefault(
            p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (pos is null)
        {
            return null;
        }

        var unrealizedPnl = pos.CostBasisUsd is decimal cb ? pos.UsdValue - cb : (decimal?)null;
        var unrealizedPct = pos.CostBasisUsd is decimal cb2 && cb2 > 0m
            ? Math.Round((pos.UsdValue - cb2) / cb2 * 100m, 2)
            : (decimal?)null;

        // Tax lots are only available for brokerage (IBKR) positions.
        IReadOnlyList<DossierTaxLotEntry> taxLots = [];
        if (pos.Provider.Equals("ibkr", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                taxLots = await taxLotsReader.GetForSymbolAsync(userId, symbol, ct) ?? [];
            }
            catch
            {
                // degrade gracefully — tax lot detail is best-effort
            }
        }

        return new DossierPositionSection(
            Provider: pos.Provider,
            AssetClass: pos.AssetClass,
            Quantity: pos.Quantity,
            CurrentValueUsd: pos.UsdValue,
            CostBasisUsd: pos.CostBasisUsd,
            UnrealizedPnlUsd: unrealizedPnl,
            UnrealizedPnlPercent: unrealizedPct,
            TaxLots: taxLots);
    }

    private static DossierAnalystsSection? BuildAnalystsSection(AnalystActionsResult? result)
    {
        if (result is null)
        {
            return null;
        }

        // notInUniverse with no actions = no coverage; return null so the UI hides the section.
        if (result.Coverage == "notInUniverse" && result.Actions.Count == 0)
        {
            return null;
        }

        return new DossierAnalystsSection(
            RecentActions: result.Actions,
            Trends: result.RecommendationTrends ?? [],
            Coverage: result.Coverage);
    }

    /// <summary>Executes <paramref name="fn"/>, returning null on any exception.</summary>
    private static async Task<T?> Safe<T>(Func<Task<T>> fn) where T : class
    {
        try
        {
            return await fn();
        }
        catch
        {
            return null;
        }
    }
}
