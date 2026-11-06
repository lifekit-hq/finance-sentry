using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Mcp.Auditing;
using FinanceSentry.Modules.Wealth.Application.Queries;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class GetCareerTimelineTool(
    IQueryHandler<GetCareerTimelineQuery, CareerTimelineResponse> timelineHandler,
    IIdentityResolver identity,
    ILogger<GetCareerTimelineTool> logger)
{
    private readonly IQueryHandler<GetCareerTimelineQuery, CareerTimelineResponse> _timelineHandler = timelineHandler;
    private readonly IIdentityResolver _identity = identity;
    private readonly ILogger<GetCareerTimelineTool> _logger = logger;

    // Every call is put on the audit trail before it runs, by the field names the tool can disclose.
    [SensitiveProfile("Label", "Amount", "Note", "Kind", "Currency", "Date", "EndDate")]
    [McpServerTool(Name = "get_career_timeline")]
    [Description("Returns the dated career-money events the person ENTERED, earliest first: events with kind (IncomeStep: income changes from the date on by a signed monthly amount; OneOffIn: a single amount received, such as a bonus; OneOffOut: a single amount spent; Leave: income falls by a monthly amount from date through endDate), date, endDate (leave only), label, amount in its own currency, note, counted (true while the event shapes the scenario: not retired and dated after today) and retired. countedEvents is how many are counted. These events build a SCENARIO that get_goals_progress, and the net-worth and financial-independence projections, show beside the baseline: a scenario the person entered, not a forecast; each event changes what they save by its amount from its date, their spending is assumed not to move and tax is not modelled; events already in the past are in the baseline's history and are not counted twice. readable false means this host holds no encryption key: events come back with only their kind, dates, currency and counted/retired, no label, amount or note (the same call made by the in-app agent returns them in full). Never use it to guess a salary, an employer or a person; use only what is returned. Reading this is recorded on the person's audit trail (tool name, field names, who asked - never values). Context for the person's own planning, never advice or a trading signal.")]
    public async Task<CareerTimelineResponse?> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var effective = _identity.GetUserId();
        if (effective is null) return null;
        var userIdVal = effective.Value;

        try
        {
            return await _timelineHandler.Handle(new GetCareerTimelineQuery(userIdVal), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Career timeline query unavailable for user {UserId}; returning null.", userIdVal);
            return null;
        }
    }
}
