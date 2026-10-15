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
    [McpServerTool(Name = "get_portfolio_snapshot")]
    [Description("Unified portfolio snapshot: IBKR brokerage positions + crypto holdings (Binance, Revolut X), each with unrealized P&L (USD and %) when cost basis is known, PLUS total cash (USD). cashUsd counts banking balances AND idle brokerage cash (uninvested currency balances) AND fiat held on crypto venues (e.g. EUR on Revolut X) — the same definition the allocation-drift tool uses; bankingCashUsd/brokerageCashUsd/venueCashUsd give the split. Idle brokerage cash and venue fiat are NOT listed under positions or investedValueUsd. Returns per-position rows and book-level totals (invested value, cash, total value, total cost basis, total unrealized P&L). unrealizedPnlUsd/Pct are null when cost basis is unavailable. headlines carries the four book-level figures (totalValue, investedValue, cash, unrealizedPnl) as typed records: method (VALUE, or GAIN_VS_COST for unrealized P&L with basis COST), scope, quality (COMPLETE / ESTIMATED / DEGRADED / UNAVAILABLE), typed reasons (STALE_SOURCE with the source whose read failed and counted as zero, FX_TODAY_RATE, FX_UNKNOWN_1TO1), and for unrealized P&L basisStatus (COMPLETE / PARTIAL / MISSING) plus coverage, the share of invested value whose cost basis is known. Read quality before trusting a total; amounts are USD.")]
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

        var withBasis = positions.Where(p => p.CostBasis is > 0m).ToList();
        decimal? totalCostBasisUsd = withBasis.Count > 0 ? withBasis.Sum(p => p.CostBasis!.Value) : null;
        decimal? totalPnlUsd = withBasis.Count > 0 ? withBasis.Sum(p => p.CurrentValue - p.CostBasis!.Value) : null;
        decimal? totalPnlPct = totalCostBasisUsd is > 0m
            ? Math.Round(totalPnlUsd!.Value / totalCostBasisUsd.Value * 100m, 2)
            : null;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var signals = HeadlineSignals.FromBook(book);
        var headlines = new PortfolioHeadlines(
            TotalValue: HeadlineFactory.Value(book.TotalValueUsd, HeadlineScope.Umbrella, today, signals),
            InvestedValue: HeadlineFactory.Value(book.InvestedValueUsd, HeadlineScope.InvestedSleeve, today, signals),
            Cash: HeadlineFactory.Value(book.CashUsd, HeadlineScope.CashSleeve, today, signals),
            UnrealizedPnl: HeadlineFactory.GainVsCost(
                [.. book.Positions.Select(p => new CostedValue(p.UsdValue, p.CostBasisUsd))],
                HeadlineScope.InvestedSleeve, today, signals));

        return new PortfolioSnapshot(
            Positions: positions,
            CashUsd: book.CashUsd,
            BankingCashUsd: book.BankingCashUsd,
            BrokerageCashUsd: book.BrokerageCashUsd,
            VenueCashUsd: book.VenueCashUsd,
            InvestedValueUsd: book.InvestedValueUsd,
            TotalValueUsd: book.TotalValueUsd,
            TotalCostBasisUsd: totalCostBasisUsd,
            TotalUnrealizedPnlUsd: totalPnlUsd,
            TotalUnrealizedPnlPct: totalPnlPct,
            Headlines: headlines);
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
            Provider: p.Provider);
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
    string Provider);

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
    PortfolioHeadlines? Headlines = null)
{
    public static PortfolioSnapshot Empty { get; } = new([], 0m, 0m, 0m, 0m, 0m, 0m, null, null, null);
}

/// <summary>The book-level totals of a <see cref="PortfolioSnapshot"/> as typed <see cref="Headline"/> records (USD).</summary>
public sealed record PortfolioHeadlines(
    Headline TotalValue,
    Headline InvestedValue,
    Headline Cash,
    Headline UnrealizedPnl);
