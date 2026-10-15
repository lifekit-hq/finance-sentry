using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetNetWorthHistoryTool(
    IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> historyHandler,
    IIdentityResolver identity,
    ILogger<GetNetWorthHistoryTool> logger)
{
    private readonly IQueryHandler<GetNetWorthHistoryQuery, NetWorthHistoryResponse> _historyHandler = historyHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetNetWorthHistoryTool> _logger = logger;

    [McpServerTool(Name = "get_net_worth_history")]
    [Description("Returns historical net worth snapshots (banking + brokerage + crypto totals per day), optionally bounded by from/to dates. staleSleeves lists any sleeves whose value was carried forward from a prior day because that provider's feed was stale/disconnected/failed — treat a day with staleSleeves as a partially estimated net worth, not real movement. isApproximate marks a reconstructed, banking-only day (brokerage/crypto forced to 0, current-not-historical FX rate) rather than a real measured snapshot. cashTotal (bank balances with card debt netted in, idle broker cash, venue fiat) plus brokerageInvested plus cryptoInvested equals totalNetWorth; brokerageInvested/cryptoInvested are the sleeves without their cash. All three are null for days with no split (before roughly 2026-07-08, or a day the split could not be measured) — null means unknown, never zero; the entry's headline types the day's total only and does not describe the split fields. headline is each day's totalNetWorth as a typed record (method VALUE, scope umbrella, USD) whose quality and reasons say the same thing in one vocabulary: DEGRADED with STALE_SOURCE per carried-forward sleeve, ESTIMATED with BACKFILLED_ESTIMATE + FX_TODAY_RATE for a reconstructed day, FX_TODAY_RATE / FX_UNKNOWN_1TO1 for the conversion the day was valued with. sources lists the day's sources one by one (sourceId banking / brokerage / crypto, kind, total = the figure the day counted for it in the entry's currency, health COMPLETE / ESTIMATED / DEGRADED / UNAVAILABLE, reasons such as STALE_SOURCE / FX_TODAY_RATE / FX_UNKNOWN_1TO1); the headline's STALE_SOURCE reasons are the sources whose reasons carry STALE_SOURCE, and staleSleeves is the same set as one comma-separated value. Days recorded before per-source rows existed list the three sleeves from their totals, with STALE_SOURCE on the carried-forward ones.")]
    public async Task<IReadOnlyList<NetWorthHistoryEntry>> ExecuteAsync(
        [Description("Optional inclusive start date (e.g. 2024-01-01).")] DateOnly? fromDate = null,
        [Description("Optional inclusive end date (e.g. 2024-12-31).")] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        NetWorthHistoryResponse response;
        try
        {
            response = await _historyHandler.Handle(
                new GetNetWorthHistoryQuery(userIdVal, fromDate, toDate),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Net worth history query unavailable for user {UserId}; returning empty list.", userIdVal);
            return [];
        }

        return response.Snapshots
            .Select(s => new NetWorthHistoryEntry(
                s.SnapshotDate,
                s.BankingTotal,
                s.BrokerageTotal,
                s.CryptoTotal,
                s.TotalNetWorth,
                s.Currency,
                s.StaleSleeves,
                s.IsApproximate,
                s.CashTotal,
                s.BrokerageInvested,
                s.CryptoInvested,
                HeadlineFactory.Value(
                    s.TotalNetWorth, HeadlineScope.Umbrella, s.SnapshotDate,
                    HeadlineSignals.FromSnapshot(null, s.IsApproximate, s.FxReasons) with { StaleSources = StaleSources(s) }),
                s.Sources))
            .ToList();
    }

    // Stale state comes from the per-source reasons the history query resolved for the day, not the column.
    private static IReadOnlyList<string> StaleSources(NetWorthSnapshotDto snapshot)
        => [.. (snapshot.Sources ?? []).Where(s => s.Reasons.Contains(SourceReasons.StaleSource)).Select(s => s.SourceId)];
}

public sealed record NetWorthHistoryEntry(
    DateOnly SnapshotDate,
    decimal BankingTotal,
    decimal BrokerageTotal,
    decimal CryptoTotal,
    decimal TotalNetWorth,
    string Currency,
    string? StaleSleeves,
    bool IsApproximate,
    decimal? CashTotal = null,
    decimal? BrokerageInvested = null,
    decimal? CryptoInvested = null,
    Headline? Headline = null,
    IReadOnlyList<NetWorthSourceDto>? Sources = null);
