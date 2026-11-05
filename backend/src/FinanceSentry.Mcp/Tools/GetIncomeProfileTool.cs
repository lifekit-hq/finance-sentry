using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetIncomeProfileTool(
    IQueryHandler<GetIncomeProfileQuery, IncomeProfileResponse> profileHandler,
    IIdentityResolver identity,
    ILogger<GetIncomeProfileTool> logger)
{
    private readonly IQueryHandler<GetIncomeProfileQuery, IncomeProfileResponse> _profileHandler = profileHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetIncomeProfileTool> _logger = logger;

    // Every call is put on the audit trail before it runs, by the field names the tool can disclose.
    [SensitiveProfile("Label", "Amount", "Kind", "Currency", "Cadence", "GrossOrNet", "StartDate", "EndDate")]
    [McpServerTool(Name = "get_income_profile")]
    [Description("Returns the income the person has DECLARED next to the income that was OBSERVED. sources lists each declared income: kind (Salary, Side, Other), label, amount per cadence in its own currency (Monthly, Weekly, Fortnightly, Annual, Irregular), grossOrNet, startDate, endDate (null while it continues), active, and monthlyEquivalentUsd (null for an irregular source). declaredMonthlyUsd is the sum of the active sources' monthly equivalents in USD, declaredBasis their basis (Net, Gross or Mixed). observedMonthlyUsd is the cashflow profile's median month of income in USD (null with observedReason when fewer than three complete months exist). variance compares them: Matches (within 10%), ObservedLower, ObservedHigher, NoObserved, NoDeclared (no active declared income), NotComparable (declared gross, mixed or only irregular sources cannot be held against income that landed after tax), DeclaredUnreadable (this host holds no encryption key: sources come back with no label, amount or monthly equivalent, only their kind, currency, cadence, basis and dates; the same call made by the in-app agent returns them in full). Declared income never replaces the observed figure: use observed income for any projection and declared income only to say what the person expects. Reading this is recorded on the person's audit trail (tool name, field names, who asked - never values). Context for the person's own planning, never advice or a trading signal.")]
    public async Task<IncomeProfileResponse?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return null;
        var userIdVal = effective.Value;

        try
        {
            return await _profileHandler.Handle(new GetIncomeProfileQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Income profile query unavailable for user {UserId}; returning null.", userIdVal);
            return null;
        }
    }
}
