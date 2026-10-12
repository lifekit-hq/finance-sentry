using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Wealth.Application.Queries;
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
    [Description("Returns historical net worth snapshots (banking + brokerage + crypto totals per day), optionally bounded by from/to dates. staleSleeves lists any sleeves whose value was carried forward from a prior day because that provider's feed was stale/disconnected/failed — treat a day with staleSleeves as a partially estimated net worth, not real movement. isApproximate marks a reconstructed, banking-only day (brokerage/crypto forced to 0, current-not-historical FX rate) rather than a real measured snapshot. cashTotal (bank balances with card debt netted in, idle broker cash, venue fiat) plus brokerageInvested plus cryptoInvested equals totalNetWorth; brokerageInvested/cryptoInvested are the sleeves without their cash. All three are null for days with no split (before roughly 2026-07-08, or a day the split could not be measured) — null means unknown, never zero.")]
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
                s.CryptoInvested))
            .ToList();
    }
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
    decimal? CryptoInvested = null);
