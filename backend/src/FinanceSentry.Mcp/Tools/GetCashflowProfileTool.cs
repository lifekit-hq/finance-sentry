using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetCashflowProfileTool(
    IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse> profileHandler,
    IIdentityResolver identity,
    ILogger<GetCashflowProfileTool> logger)
{
    private readonly IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse> _profileHandler = profileHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetCashflowProfileTool> _logger = logger;

    [McpServerTool(Name = "get_cashflow_profile")]
    [Description("Returns the person's burn, savings rate and runway over the trailing complete months (the in-progress month is never counted; fewer than three complete months, or no income across them, gives status InsufficientHistory with a reason and no figure). All amounts are USD. incomeMonthlyMedian is the median month's income (the month's net plus its outflow) and burnMonthlyMedian the median month's honest outflow (counterparty and transfer corrected). savingsRate is POOLED: the sum of the months' net over the sum of their income, a fraction (0.25 is 25%) that is negative while spending exceeds income; a per-month ratio is never returned. runwayMonths is the latest snapshot's cashTotal over burnMonthlyMedian, rounded to one decimal; it is null with runwayReason NO_SNAPSHOT, NO_CASH_SPLIT (the snapshot has no cash split — unknown, not zero), NO_CASH (status NoCash: cash is zero or negative) or NO_BURN (no spending). committedShare is the pooled share of outflow the committed-outflow policy claims, a floor (it under-counts), null when not supplied. fromMonth/toMonth name the months covered (completeMonths of them); hasStaleSleeves is true when a source of the latest snapshot was carried forward. These figures are context for the person's own planning, never a trading signal or advice, and not a forecast: do not turn them into buy, sell or hold calls.")]
    public async Task<CashflowProfileResponse?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return null;
        var userIdVal = effective.Value;

        try
        {
            return await _profileHandler.Handle(new GetCashflowProfileQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cashflow profile query unavailable for user {UserId}; returning null.", userIdVal);
            return null;
        }
    }
}
