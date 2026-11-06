using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetGoalsProgressTool(
    IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse> goalsHandler,
    IIdentityResolver identity,
    ILogger<GetGoalsProgressTool> logger)
{
    private readonly IQueryHandler<GetGoalsProgressQuery, GoalsProgressResponse> _goalsHandler = goalsHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetGoalsProgressTool> _logger = logger;

    // The scenario figures are derived from the person's career events, so a call that can return them is on the audit trail.
    [SensitiveProfile("Kind", "Date", "Amount")]
    [McpServerTool(Name = "get_goals_progress")]
    [Description("Returns each goal on the person's current IPS (see get_ips) tracked against their total net worth plus their savings. All amounts are USD. Per goal: label, targetAmount, targetDate, priority, monthsLeft (whole months to the date; 0 once passed), requiredMonthlySaving (the level monthly saving that reaches the target by the date, as a low/central/high range: low is the saving at the highest of the shared real returns, high the saving at the lowest, since a higher return needs less; central is at the central return, which is 3 points either side of the low and high ones with the low floored at 0; zero when the balance gets there on its own), projectedAtTargetDate (where the current savings path lands by then, same range), alreadyReached, and onTrack: OnTrack (the central projection reaches the target by the date), Behind, NoTargetDate or NoTargetAmount (the goal lacks that input, nothing is claimed; a date more than 100 years out is NoTargetDate with reason TARGET_DATE_TOO_FAR) or InsufficientHistory (no snapshot, or under three complete months of flow; reason says which). contributionPlan sets the IPS contribution plan (plannedMonthly is its monthly equivalent, null for a cadence that is not recognised) beside the measured savings path (actualMonthlySavings, the median complete-month net; difference is actual minus planned). Goals are measured against the WHOLE net worth plus savings, not money earmarked per goal, so they are not additive. status NoGoals means the person has no IPS goals; they are set through save_ips. Same arithmetic and assumptions as the net-worth projection and the financial-independence date; hypothetical, not a guarantee. When the person has entered career events (see get_career_timeline) each goal with months left also carries a scenario: the same ranges with those events applied (onTrack, requiredMonthlySaving - the level saving still needed once the events are counted - and projectedAtTargetDate), next to the unchanged baseline figures, and scenario at the top level says how many events counted (status Applied; Unreadable means events count but this host holds no encryption key, so no scenario figures). The scenario is one the person entered, not a forecast: each event changes what they save by its amount from its date, spending is assumed not to move and tax is not modelled. Always state both the baseline and the scenario, labelled. Reading goals is recorded on the person's audit trail (tool name, field names - never values). These figures are context for the person's own planning, never a trading signal or advice: do not turn them into buy, sell or hold calls.")]
    public async Task<GoalsProgressResponse?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return null;
        var userIdVal = effective.Value;

        try
        {
            return await _goalsHandler.Handle(new GetGoalsProgressQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Goals progress query unavailable for user {UserId}; returning null.", userIdVal);
            return null;
        }
    }
}
