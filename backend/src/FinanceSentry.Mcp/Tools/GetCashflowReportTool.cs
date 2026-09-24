using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetCashflowReportTool(
    IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>> moneyFlowHandler,
    IIdentityResolver identity,
    ILogger<GetCashflowReportTool> logger)
{
    private const int MaxMonthsBack = 24;
    private const int DefaultMonthsBack = 6;

    private readonly IQueryHandler<GetMoneyFlowStatisticsQuery, IReadOnlyList<MonthlyFlow>> _moneyFlowHandler = moneyFlowHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetCashflowReportTool> _logger = logger;

    [McpServerTool(Name = "get_cashflow_report")]
    [Description("Returns a monthly cashflow report (inflow, outflow, net) from the classified money-flow statistics — internal transfers between the user's own accounts are excluded. Defaults to the authenticated MCP identity when userId is omitted.")]
    public async Task<IReadOnlyList<CashflowReportEntry>> ExecuteAsync(
        [Description("Optional user GUID. Defaults to the authenticated MCP identity.")] Guid? userId = null,
        [Description("Optional inclusive start date. Defaults to 6 months ago.")] DateOnly? fromDate = null,
        [Description("Optional inclusive end date. Defaults to today.")] DateOnly? toDate = null,
        CancellationToken cancellationToken = default)
    {
        var effective = userId ?? _identity.GetUserId();
        if (effective is null) return [];
        var userIdVal = effective.Value;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = toDate ?? today;
        var from = fromDate ?? to.AddMonths(-DefaultMonthsBack);

        if (from > to)
            return [];

        if (to.DayNumber - from.DayNumber > MaxMonthsBack * 31)
            from = to.AddMonths(-MaxMonthsBack);

        // Months back from "to", not "today" — the window the caller asked for. With no
        // dates given this reduces to DefaultMonthsBack, matching the old default exactly.
        var months = Math.Max(1, ((to.Year - from.Year) * 12) + to.Month - from.Month);

        IReadOnlyList<MonthlyFlow> flows;
        try
        {
            flows = await _moneyFlowHandler.Handle(
                new GetMoneyFlowStatisticsQuery(userIdVal, months),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Money-flow statistics query unavailable for user {UserId}; returning empty cashflow report.", userIdVal);
            return [];
        }

        // Each month has one row per currency (transfers and counterparty-matched transactions
        // already excluded upstream) plus at most one synthetic USD row carrying that month's
        // counterparty flows. Summing InflowUsd/OutflowUsd across every row for a month gives
        // the whole picture with nothing double-counted: the per-currency rows and the
        // synthetic row partition the transaction set, they never overlap it.
        return flows
            .Where(f => IsWithinRange(f.Month, from, to))
            .GroupBy(f => f.Month)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var inflow = g.Sum(f => f.InflowUsd);
                var outflow = g.Sum(f => f.OutflowUsd);
                return new CashflowReportEntry(
                    g.Key,
                    inflow,
                    outflow,
                    inflow - outflow,
                    0);
            })
            .ToList();
    }

    private static bool IsWithinRange(string month, DateOnly from, DateOnly to)
    {
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var monthStart))
            return false;
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return monthStart <= to && monthEnd >= from;
    }
}

public sealed record CashflowReportEntry(
    string Period,
    decimal Inflow,
    decimal Outflow,
    decimal Net,
    int TransactionCount);
