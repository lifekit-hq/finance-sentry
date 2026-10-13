namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Dossier;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.Scoring;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

/// <summary>
/// Aggregate dossier for one symbol: reads all per-ticker data sources in turn (they share one
/// scoped DbContext) and composes the result. Each section degrades to null / empty on failure — never throws (feature 421).
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
    IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>> earnings,
    IPositionCapSource positionCap,
    IPriceHistoryService priceHistory,
    IFundamentalsService fundamentals,
    IAssetPriceTargetRepository priceTargets,
    IRecommendationRepository recommendations,
    PositionReturnsReader returnsReader)
    : IQueryHandler<GetAssetDossierQuery, AssetDossierResult>
{
    private const int NewsLimit = 10;
    private const int SignalLimit = 50;
    private const int AnalystActionLimit = 20;
    private const int AnalystActionDays = 90;
    private const int EarningsDays = 90;
    private const int FundamentalFactsPerConcept = 8;
    private const int PercentScale = 100;
    private const int Decimals = 1;

    public async Task<AssetDossierResult> Handle(GetAssetDossierQuery request, CancellationToken ct)
    {
        var symbol = request.Symbol.Trim().ToUpperInvariant();
        var userId = request.UserId;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Read the sources one after another: the theses, valuation, analyst and news handlers share this
        // request's one scoped ResearchDbContext, which allows a single operation at a time. Each read is wrapped
        // so one failure cannot 500 the rest.
        var book = await Safe(() => bookFigures.ReadAsync(userId, ct));
        var allTheses = await Safe(() => theses.Handle(new GetThesesQuery(userId), ct));
        var valuationResult = await Safe(() => valuation.Handle(new GetValuationSnapshotQuery(symbol, null), ct));
        var newsResult = await Safe(() => news.Handle(new GetNewsForTickerQuery(symbol, null, NewsLimit), ct));
        var earningsResult = await Safe(() => earnings.Handle(
            new GetEarningsCalendarQuery([symbol], null, today, today.AddDays(EarningsDays), EarningsEventType.Earnings), ct));
        var signalResult = await Safe(() => signalReader.GetRecentAsync(symbol, SignalLimit, ct));
        var capFraction = await Safe(() => positionCap.GetMaxPositionWeightAsync(userId, ct));
        var userTarget = await Safe(() => priceTargets.GetAsync(userId, symbol, ct));
        var pending = await Safe(() => recommendations.FindPendingAsync(userId, symbol, ct));

        var bookPosition = book?.Positions.FirstOrDefault(
            p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
        var isCrypto = DossierTickers.IsCrypto(bookPosition?.AssetClass) || valuationResult?.NotApplicable == true;

        // Crypto has no analyst coverage or company fundamentals: its valuation is reduced to the not-applicable
        // marker the page keys its crypto layout on, and the analyst source is not read.
        if (isCrypto && valuationResult is { NotApplicable: false })
        {
            valuationResult = null;
        }

        valuationResult = InMajorUnits(valuationResult);

        var analystResult = isCrypto
            ? null
            : await Safe(() => analystActions.Handle(
                new GetAnalystActionsQuery(symbol, today.AddDays(-AnalystActionDays), null, AnalystActionLimit), ct));

        var yearHistory = await Safe(() => priceHistory.GetAsync(
            DossierTickers.ToChartTicker(symbol, bookPosition?.AssetClass), ChartRange.OneYear, ct)) ?? PriceHistory.Empty;
        var benchmarkHistory = await Safe(() => priceHistory.GetAsync(DossierTickers.Benchmark, ChartRange.OneYear, ct))
            ?? PriceHistory.Empty;
        var facts = isCrypto
            ? null
            : (await Safe(() => fundamentals.GetFundamentalsAsync(symbol, FundamentalFactsPerConcept, ct)))?.Facts;

        var capPercent = capFraction is decimal cap ? Math.Round(cap * PercentScale, 2) : (decimal?)null;

        // Position section: find symbol in the book, then fetch tax lots and the return decomposition.
        var position = await BuildPositionSectionAsync(userId, symbol, book, bookPosition, capPercent, ct);

        // Thesis: first matching ticker (user-scoped, case-insensitive).
        var thesis = allTheses?
            .FirstOrDefault(t => t.Ticker.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        // Analysts: null when not in universe or no actions found.
        var analysts = BuildAnalystsSection(analystResult);

        // Valuation: null when the query failed; keep the result even when NotApplicable so UI
        // can distinguish "no data" from "not applicable for crypto".
        // (Null = source failure; non-null with NotApplicable = crypto.)

        var performance = PerformanceMath.Build(
            yearHistory.Closes.Select(c => (c.Date, c.Close)).ToList(),
            benchmarkHistory.Closes.Select(c => c.Close).ToList(),
            yearHistory.Currency);

        var checks = DossierChecksBuilder.Build(BuildCheckInputs(
            isCrypto, analysts, valuationResult, facts, yearHistory, performance, position, capPercent, thesis));

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
            GeneratedAt: DateTimeOffset.UtcNow,
            Performance: yearHistory.Closes.Count > 0 ? performance : null,
            Checks: checks,
            UserTarget: userTarget is null
                ? null
                : new DossierUserTarget(userTarget.TargetPrice, userTarget.Currency, userTarget.UpdatedAt),
            PendingRecommendation: pending is null ? null : RecommendationDto.From(pending));
    }

    private async Task<DossierPositionSection?> BuildPositionSectionAsync(
        Guid userId, string symbol, BookFigures? book, BookFigurePosition? pos, decimal? capPercent, CancellationToken ct)
    {
        if (book is null || pos is null)
        {
            return null;
        }

        var unrealizedPnl = pos.CostBasisUsd is decimal cb ? pos.UsdValue - cb : (decimal?)null;
        var unrealizedPct = pos.CostBasisUsd is decimal cb2 && cb2 > 0m
            ? Math.Round((pos.UsdValue - cb2) / cb2 * 100m, 2)
            : (decimal?)null;

        var shareOfBook = book.TotalValueUsd > 0m
            ? Math.Round(pos.UsdValue / book.TotalValueUsd * 100m, 2)
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

        var reading = await Safe(() => returnsReader.ReadAsync(userId, pos, ct));

        return new DossierPositionSection(
            Provider: pos.Provider,
            AssetClass: pos.AssetClass,
            Quantity: pos.Quantity,
            CurrentValueUsd: pos.UsdValue,
            CostBasisUsd: pos.CostBasisUsd,
            UnrealizedPnlUsd: unrealizedPnl,
            UnrealizedPnlPercent: unrealizedPct,
            TaxLots: taxLots,
            ShareOfBookPercent: shareOfBook,
            PositionCapPercent: capPercent,
            Returns: reading?.Returns);
    }

    private static DossierCheckInputs BuildCheckInputs(
        bool isCrypto,
        DossierAnalystsSection? analysts,
        ValuationSnapshotResult? valuation,
        IReadOnlyList<FundamentalFact>? facts,
        PriceHistory yearHistory,
        DossierPerformance performance,
        DossierPositionSection? position,
        decimal? capPercent,
        ThesisDto? thesis)
    {
        var latestTrend = analysts?.Trends.OrderByDescending(t => t.Period).FirstOrDefault();
        var totalRatings = latestTrend is null
            ? (int?)null
            : latestTrend.StrongBuy + latestTrend.Buy + latestTrend.Hold + latestTrend.Sell + latestTrend.StrongSell;

        decimal? revenueYoy = null;
        decimal? epsYoy = null;
        if (facts is { Count: > 0 })
        {
            // Same basis as the fundamentals scorer: quarterly when the filer has quarterly facts, else annual.
            var periodType = facts.Any(f => FundamentalMath.MatchesPeriodType(f, ThesisPeriodType.Quarter))
                ? ThesisPeriodType.Quarter
                : ThesisPeriodType.Annual;
            revenueYoy = AsPercent(PositiveBaseYoy(
                facts, FundamentalMath.YoyConceptByMetric[ThesisMetric.RevenueYoy], periodType));
            epsYoy = AsPercent(PositiveBaseYoy(
                facts, FundamentalMath.YoyConceptByMetric[ThesisMetric.EpsYoy], periodType));
        }

        return new DossierCheckInputs(
            IsCrypto: isCrypto,
            BuyRatings: latestTrend is null ? null : latestTrend.StrongBuy + latestTrend.Buy,
            TotalRatings: totalRatings,
            ImpliedUpsidePct: valuation?.ImpliedUpsidePct,
            RevenueYoyPct: revenueYoy,
            EpsYoyPct: epsYoy,
            TrailingPe: valuation?.Metrics.TrailingPe.Value,
            TrailingPeFiveYearAvg: valuation?.Metrics.TrailingPe.FiveYearAvg,
            ForwardPe: valuation?.Metrics.ForwardPe.Value,
            PeerForwardPes: valuation?.PeerSet?.Peers.Select(p => p.ForwardPe).OfType<decimal>().ToList() ?? [],
            YearCloses: yearHistory.Closes.Select(c => c.Close).ToList(),
            YearChangePct: performance.YearChangePct,
            BenchmarkYearChangePct: performance.BenchmarkYearChangePct,
            IsHeld: position is not null,
            ShareOfBookPercent: position?.ShareOfBookPercent,
            PositionCapPercent: capPercent,
            HasThesis: thesis is not null,
            ThesisBroken: thesis?.BrokenAt is not null);
    }

    /// <summary>
    /// Latest year-over-year change of a concept, or null when the same period a year earlier is missing or not
    /// positive: growth off a zero or loss-making base is not a meaningful rate (a loss-to-profit turnaround
    /// would otherwise read as a collapse), so a check shows "no comparable filing" rather than a wrong figure.
    /// </summary>
    public static decimal? PositiveBaseYoy(
        IReadOnlyList<FundamentalFact> facts, string concept, ThesisPeriodType periodType)
    {
        var periods = FundamentalMath.SelectPeriods(facts, concept, periodType);
        if (periods.Count == 0)
        {
            return null;
        }

        var prior = FundamentalMath.PriorYearFact(periods[0], periods);
        return prior is { Value: > 0m } ? FundamentalMath.SafeYoy(periods[0].Value, prior.Value) : null;
    }

    private static ValuationSnapshotResult? InMajorUnits(ValuationSnapshotResult? valuation)
    {
        if (valuation is null)
        {
            return null;
        }

        var (currency, divisor) = MinorUnitCurrency.Normalize(valuation.Currency);
        return valuation with
        {
            Price = valuation.Price / divisor,
            ConsensusTarget = valuation.ConsensusTarget / divisor,
            TargetLow = valuation.TargetLow / divisor,
            TargetHigh = valuation.TargetHigh / divisor,
            Currency = currency,
        };
    }

    private static decimal? AsPercent(decimal? fraction)
        => fraction is decimal f ? Math.Round(f * PercentScale, Decimals) : null;

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
    private static async Task<T?> Safe<T>(Func<Task<T>> fn)
    {
        try
        {
            return await fn();
        }
        catch
        {
            return default;
        }
    }
}
