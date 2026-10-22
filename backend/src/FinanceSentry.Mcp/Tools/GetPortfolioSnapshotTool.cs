using System.ComponentModel;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Abstractions;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetPortfolioSnapshotTool(
    IBookFiguresService bookFigures,
    IIdentityResolver identity)
{
    private const string UnknownBasisState = "Unknown";

    [McpServerTool(Name = "get_portfolio_snapshot")]
    [Description("Unified portfolio snapshot: IBKR brokerage positions + crypto holdings (Binance, Revolut X), each with unrealized P&L (USD and %) when cost basis is known, PLUS total cash (USD). cashUsd counts banking balances AND idle brokerage cash (uninvested currency balances) AND fiat held on crypto venues (e.g. EUR on Revolut X) — the same definition the allocation-drift tool uses; bankingCashUsd/brokerageCashUsd/venueCashUsd give the split. Idle brokerage cash and venue fiat are NOT listed under positions or investedValueUsd. Returns per-position rows and book-level totals (invested value, cash, total value, total cost basis, total unrealized P&L). Each position carries basisState: Verified (a cost basis the fills support, counted in the aggregate), Unverified (fills cover the quantity but disagree with the stored basis) or Unknown (no fills for part of the held quantity); only Verified positions carry costBasis and unrealizedPnlUsd/Pct, and a broker-reported average cost is never used. The aggregate unrealized figures cover only fill-verified positions: totalUnrealizedPnlUsd/Pct and totalCostBasisUsd are filled only when every invested position is verified (headlines.unrealizedPnl.basisStatus COMPLETE), and are null otherwise. When the basis is PARTIAL the verified subset's figures sit in verifiedUnrealizedPnl next to basisStatus, coverage (the share of invested value whose basis is verified), measuredValueUsd and totalValueUsd: that is a gain on part of the book, not the book's gain, so read coverage before quoting it. headlines carries the four book-level figures (totalValue, investedValue, cash, unrealizedPnl) as typed records: method (VALUE, or GAIN_VS_COST for unrealized P&L with basis COST), scope, quality (COMPLETE / ESTIMATED / DEGRADED / UNAVAILABLE), typed reasons (STALE_SOURCE with the source whose read failed and counted as zero, FX_TODAY_RATE, FX_UNKNOWN_1TO1, NO_COST_BASIS when no position has a known cost basis), and for unrealized P&L basisStatus (COMPLETE / PARTIAL / MISSING) plus coverage. Read quality before trusting a total; amounts are USD.")]
    public async Task<PortfolioSnapshot> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return PortfolioSnapshot.Empty;
        }

        var book = await bookFigures.ReadAsync(effective.Value, cancellationToken);

        var positions = book.Positions
            .Select(p => BuildEntry(p))
            .OrderByDescending(p => p.CurrentValue)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var signals = HeadlineSignals.FromBook(book);
        // An estimated source marks only the lines its value sits in: an in-economy entry is invested, not cash.
        var investedSignals = signals with { EstimatedSources = HeadlineSignals.EstimatedIn(book, r => r.InvestedValueUsd) };
        var headlines = new PortfolioHeadlines(
            TotalValue: HeadlineFactory.Value(book.TotalValueUsd, HeadlineScope.Umbrella, today, signals),
            InvestedValue: HeadlineFactory.Value(book.InvestedValueUsd, HeadlineScope.InvestedSleeve, today, investedSignals),
            Cash: HeadlineFactory.Value(book.CashUsd, HeadlineScope.CashSleeve, today,
                signals with { EstimatedSources = HeadlineSignals.EstimatedIn(book, r => r.CashUsd) }),
            UnrealizedPnl: HeadlineFactory.GainVsCost(
                [.. book.Positions.Select(p => new CostedValue(p.UsdValue, p.CostBasisUsd))],
                HeadlineScope.InvestedSleeve, today, investedSignals));

        var verified = BuildVerifiedUnrealizedPnl(positions, headlines.UnrealizedPnl);
        var complete = headlines.UnrealizedPnl.BasisStatus == BasisStatus.Complete;

        return new PortfolioSnapshot(
            Positions: positions,
            CashUsd: book.CashUsd,
            BankingCashUsd: book.BankingCashUsd,
            BrokerageCashUsd: book.BrokerageCashUsd,
            VenueCashUsd: book.VenueCashUsd,
            InvestedValueUsd: book.InvestedValueUsd,
            TotalValueUsd: book.TotalValueUsd,
            TotalCostBasisUsd: complete ? verified?.CostBasisUsd : null,
            TotalUnrealizedPnlUsd: complete ? verified?.UnrealizedPnlUsd : null,
            TotalUnrealizedPnlPct: complete ? verified?.UnrealizedPnlPct : null,
            Headlines: headlines,
            VerifiedUnrealizedPnl: verified);
    }

    /// <summary>
    /// The gain over the positions whose cost basis is verified, with the status and coverage that say how much of
    /// the invested value that is. Null when no position has a basis: there is no figure to qualify.
    /// </summary>
    private static VerifiedUnrealizedPnl? BuildVerifiedUnrealizedPnl(
        IReadOnlyList<PortfolioSnapshotEntry> positions, Headline unrealizedPnl)
    {
        var measured = positions.Where(p => p.CostBasis is > 0m).ToList();
        if (measured.Count == 0)
        {
            return null;
        }

        var costBasis = measured.Sum(p => p.CostBasis!.Value);
        var measuredValue = measured.Sum(p => p.CurrentValue);
        var pnl = measuredValue - costBasis;

        return new VerifiedUnrealizedPnl(
            UnrealizedPnlUsd: pnl,
            UnrealizedPnlPct: Math.Round(pnl / costBasis * 100m, 2),
            CostBasisUsd: costBasis,
            BasisStatus: unrealizedPnl.BasisStatus ?? BasisStatus.Missing,
            Coverage: unrealizedPnl.Coverage,
            MeasuredValueUsd: measuredValue,
            TotalValueUsd: positions.Sum(p => p.CurrentValue),
            PositionsMeasured: measured.Count,
            PositionsTotal: positions.Count);
    }

    private static PortfolioSnapshotEntry BuildEntry(BookFigurePosition p)
    {
        decimal? pnlUsd = p.CostBasisUsd is > 0m ? p.UsdValue - p.CostBasisUsd.Value : null;
        decimal? pnlPct = p.CostBasisUsd is > 0m
            ? Math.Round((p.UsdValue - p.CostBasisUsd.Value) / p.CostBasisUsd.Value * 100m, 2)
            : null;

        return new PortfolioSnapshotEntry(
            Symbol: p.Symbol,
            AssetClass: p.AssetClass,
            Quantity: p.Quantity,
            CostBasis: p.CostBasisUsd,
            CurrentValue: p.UsdValue,
            UnrealizedPnlUsd: pnlUsd,
            UnrealizedPnlPct: pnlPct,
            Provider: p.Provider,
            BasisState: p.BasisState ?? UnknownBasisState);
    }
}

public sealed record PortfolioSnapshotEntry(
    string Symbol,
    string AssetClass,
    decimal Quantity,
    decimal? CostBasis,
    decimal CurrentValue,
    decimal? UnrealizedPnlUsd,
    decimal? UnrealizedPnlPct,
    string Provider,
    string BasisState);

public sealed record PortfolioSnapshot(
    IReadOnlyList<PortfolioSnapshotEntry> Positions,
    decimal CashUsd,
    decimal BankingCashUsd,
    decimal BrokerageCashUsd,
    decimal VenueCashUsd,
    decimal InvestedValueUsd,
    decimal TotalValueUsd,
    decimal? TotalCostBasisUsd,
    decimal? TotalUnrealizedPnlUsd,
    decimal? TotalUnrealizedPnlPct,
    PortfolioHeadlines? Headlines = null,
    VerifiedUnrealizedPnl? VerifiedUnrealizedPnl = null)
{
    public static PortfolioSnapshot Empty { get; } = new([], 0m, 0m, 0m, 0m, 0m, 0m, null, null, null);
}

/// <summary>
/// The unrealized P&amp;L of the positions whose cost basis is verified, carried with what it covers:
/// <see cref="BasisStatus"/> (COMPLETE / PARTIAL), <see cref="Coverage"/> (share of invested value measured),
/// the measured and total value in USD, and how many positions are in it.
/// </summary>
public sealed record VerifiedUnrealizedPnl(
    decimal UnrealizedPnlUsd,
    decimal UnrealizedPnlPct,
    decimal CostBasisUsd,
    BasisStatus BasisStatus,
    decimal? Coverage,
    decimal MeasuredValueUsd,
    decimal TotalValueUsd,
    int PositionsMeasured,
    int PositionsTotal);

/// <summary>The book-level totals of a <see cref="PortfolioSnapshot"/> as typed <see cref="Headline"/> records (USD).</summary>
public sealed record PortfolioHeadlines(
    Headline TotalValue,
    Headline InvestedValue,
    Headline Cash,
    Headline UnrealizedPnl);
