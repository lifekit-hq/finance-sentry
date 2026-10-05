using System.Globalization;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using Microsoft.Extensions.Logging;

namespace FinanceSentry.Modules.BrokerageSync.Application.Services;

public interface IIbkrFlexHoldingsSyncService
{
    /// <summary>
    /// Publishes the statement's Open Positions and Cash Report as the user's holdings when no healthy
    /// live (OAuth) credential covers them. Returns the number of holdings written; 0 when skipped.
    /// </summary>
    Task<int> ApplyAsync(Guid userId, FlexStatementXml statement, CancellationToken ct = default);
}

/// <summary>
/// Flex fallback for positions and cash, written into the same <c>BrokerageHoldings</c> rows the
/// OAuth feed uses (provider "ibkr"), so every holdings reader sees one source of truth. A live
/// OAuth credential that synced recently always wins; Flex only fills in when it is absent or
/// unhealthy, and its rows carry <see cref="BrokerageHolding.FlexAsOfDate"/> so the UI can state
/// the daily freshness.
/// </summary>
public sealed class IbkrFlexHoldingsSyncService(
    IIBKRCredentialRepository oauthCredentialRepository,
    IBrokerageHoldingRepository holdingRepository,
    IBrokerageInstrumentRepository instrumentRepository,
    ILogger<IbkrFlexHoldingsSyncService> logger) : IIbkrFlexHoldingsSyncService
{
    private const string Provider = "ibkr";
    private const string CashInstrumentType = "CASH";
    private const string LotLevel = "LOT";
    private const string BaseSummaryCurrency = "BASE_SUMMARY";
    private const string DateFormat = "yyyyMMdd";
    private const int MaxStatementAgeDays = 5;

    // The OAuth sync ticks every 15 minutes; a success within this window means live data is current.
    private static readonly TimeSpan OAuthHealthyWindow = TimeSpan.FromHours(2);

    public async Task<int> ApplyAsync(Guid userId, FlexStatementXml statement, CancellationToken ct = default)
    {
        var positions = statement.OpenPositions
            .Where(p => !string.Equals(p.LevelOfDetail, LotLevel, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var cash = statement.CashReport
            .Where(c => !string.Equals(c.Currency, BaseSummaryCurrency, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A statement without these sections (the user has not ticked them) must never wipe holdings.
        if (positions.Count == 0 && cash.Count == 0)
            return 0;

        var asOf = ParseDate(statement.ToDate);
        if (asOf is null || asOf.Value < DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-MaxStatementAgeDays))
        {
            // Backfill windows and stale statements describe the past, not today's portfolio.
            logger.LogInformation("Flex statement for user {UserId} is not current; skipping holdings.", userId);
            return 0;
        }

        var oauth = await oauthCredentialRepository.GetByUserIdUnscopedAsync(userId, ct);
        if (oauth is { IsActive: true, LastSyncError: null, LastSyncAt: { } lastSync }
            && DateTime.UtcNow - lastSync <= OAuthHealthyWindow)
        {
            logger.LogInformation("Live IBKR feed is healthy for user {UserId}; Flex holdings not applied.", userId);
            return 0;
        }

        var brokerPositions = new List<(string Symbol, string Type, decimal Qty, decimal Usd, decimal? AvgCostUsd, long? Conid, string? Isin)>();
        foreach (var p in positions)
        {
            var qty = ParseDecimal(p.Position);
            if (qty is null or 0m || string.IsNullOrWhiteSpace(p.Symbol))
                continue;
            var currency = p.Currency ?? "USD";
            var value = ParseDecimal(p.PositionValue) ?? 0m;
            var avg = ParseDecimal(p.CostBasisPrice);
            brokerPositions.Add((
                p.Symbol!, p.AssetCategory ?? string.Empty, qty.Value, CurrencyConverter.ToUsd(value, currency),
                avg.HasValue ? CurrencyConverter.ToUsd(avg.Value, currency) : null,
                IbkrFlexMapper.ParseConid(p.Conid), p.Isin));
        }

        foreach (var c in cash)
        {
            var balance = ParseDecimal(c.EndingCash);
            if (balance is null or 0m || string.IsNullOrWhiteSpace(c.Currency))
                continue;
            brokerPositions.Add(($"{c.Currency} Cash", CashInstrumentType, balance.Value,
                CurrencyConverter.ToUsd(balance.Value, c.Currency!), null, null, null));
        }

        var instrumentByConid = await UpsertInstrumentsAsync(userId, brokerPositions, ct);

        var holdings = brokerPositions
            .Select(p => new BrokerageHolding(
                userId, p.Symbol, p.Type, p.Qty, p.Usd, Provider,
                averageCostUsd: p.AvgCostUsd,
                acquiredAt: p.AvgCostUsd.HasValue ? DateTime.UtcNow : null,
                instrumentId: p.Conid.HasValue ? instrumentByConid[p.Conid.Value].Id : null,
                flexAsOfDate: asOf))
            .ToList();

        await holdingRepository.UpsertRangeAsync(holdings, ct);
        await holdingRepository.SaveChangesAsync(ct);

        // Same reconcile as the live sync: drop holdings the statement no longer reports.
        var keys = brokerPositions.Select(p => p.Symbol).ToHashSet(StringComparer.Ordinal);
        var persisted = await holdingRepository.GetByUserIdUnscopedAsync(userId, ct);
        var stale = persisted.Where(h => h.Provider == Provider && !keys.Contains(h.Symbol)).ToList();
        if (stale.Count > 0)
            holdingRepository.RemoveRange(stale);

        foreach (var h in persisted.Where(h => keys.Contains(h.Symbol)))
        {
            var pos = brokerPositions.First(p => p.Symbol == h.Symbol);
            h.SetCostBasis(pos.AvgCostUsd, pos.AvgCostUsd.HasValue ? (h.AcquiredAt ?? DateTime.UtcNow) : h.AcquiredAt);
        }

        await holdingRepository.SaveChangesAsync(ct);

        logger.LogInformation(
            "Applied Flex holdings for user {UserId}: {Count} rows as of {AsOf}.", userId, holdings.Count, asOf);
        return holdings.Count;
    }

    private async Task<Dictionary<long, BrokerageInstrument>> UpsertInstrumentsAsync(
        Guid userId,
        IEnumerable<(string Symbol, string Type, decimal Qty, decimal Usd, decimal? AvgCostUsd, long? Conid, string? Isin)> positions,
        CancellationToken ct)
    {
        var result = new Dictionary<long, BrokerageInstrument>();
        foreach (var p in positions)
        {
            if (p.Conid is not long conid || result.ContainsKey(conid))
                continue;

            var instrument = await instrumentRepository.GetByConidUnscopedAsync(userId, Provider, conid, ct);
            if (instrument is null)
            {
                instrument = new BrokerageInstrument(userId, Provider, conid, p.Symbol, p.Type, p.Isin);
                await instrumentRepository.AddAsync(instrument, ct);
            }
            else
            {
                instrument.RefreshIdentity(p.Symbol, p.Type, p.Isin);
                instrumentRepository.Update(instrument);
            }

            result[conid] = instrument;
        }

        if (result.Count > 0)
            await instrumentRepository.SaveChangesAsync(ct);
        return result;
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
