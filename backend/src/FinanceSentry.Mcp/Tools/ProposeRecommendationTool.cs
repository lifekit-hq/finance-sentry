using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ProposeRecommendationTool(
    ICommandHandler<ProposeRecommendationCommand, RecommendationDto> handler,
    IIdentityResolver identity)
{
    [McpServerTool(Name = "propose_recommendation")]
    [Description("Puts one rule-anchored proposal on one ticker in the owner's Decisions queue (action Add, Trim, Exit, or Hold). Finance Sentry never trades: this queues a proposal, the owner confirms or rejects it, and a confirm only records the decision and shows the order text the owner places at the broker themselves - nothing here places, stages or transmits an order. The proposal must be anchored to a trigger that is true now, and the server checks it: ThesisBreak (a broken thesis on the ticker), IpsBand (triggerRef = the sleeve's asset class, OverBand for Trim/Exit, UnderBand for Add), RiskRule (the held name is over the single-position cap), RadarSignal (a Radar signal on the ticker in the last 30 days; triggerRef = scanner or signal type, optional), EventVerdict (triggerRef = the fired companion event id). Hold is only an answer to a fired event (EventVerdict). The server sizes it from the book and a quote: Exit sells the whole position, Trim sells sizeUsd or, without it, the excess over the cap, Add buys sizeUsd after the risk gate (clamped to the compliant size, refused when there is none); it returns shares, USD, the share of book before and after, and the exact order text. Cite the evidence behind the call (kind Thesis, RiskRule, PolicyReview, Sleeve, RadarSignal, Event, Candidate or Note; a label with the number it rests on; the row id when there is one) - never a bare buy/sell call. A newer proposal on the same ticker supersedes the pending one. Queueing is silent: do not message the owner just because you proposed something. Returns the queued proposal.")]
    public async Task<RecommendationDto?> ExecuteAsync(
        [Description("Ticker as the book holds it, e.g. GRAB.")] string ticker,
        [Description("Add, Trim, Exit, or Hold (Hold only with triggerKind EventVerdict).")] string action,
        [Description("ThesisBreak, IpsBand, RiskRule, RadarSignal, or EventVerdict.")] string triggerKind,
        [Description("What fired: the sleeve asset class for IpsBand, the event id for EventVerdict, the scanner for RadarSignal; ignored for ThesisBreak and RiskRule (the server fills it).")] string? triggerRef,
        [Description("Why, with the numbers (weight vs cap, sleeve drift, the broken trigger). Max 2000 characters.")] string rationale,
        [Description("Low, Medium, or High.")] string confidence,
        [Description("What would make this call wrong - the condition under which the owner should drop it. Max 500 characters.")] string invalidationTrigger,
        [Description("Evidence items: kind, label (with the number), optional refId. At most 12; the trigger is added for you.")] IReadOnlyList<RecommendationEvidence>? evidence = null,
        [Description("USD to trade: required for Add, optional for Trim (defaults to the excess over the cap), ignored for Exit and Hold.")] decimal? sizeUsd = null,
        [Description("Days until it expires undecided, 1-30. Defaults to 14.")] int? expiresInDays = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new ProposeRecommendationCommand(
                effective.Value,
                ticker,
                Parse<RecommendationAction>(action, nameof(action)),
                Parse<RecommendationTriggerKind>(triggerKind, nameof(triggerKind)),
                triggerRef ?? string.Empty,
                rationale,
                Parse<RecommendationConfidence>(confidence, nameof(confidence)),
                invalidationTrigger,
                evidence ?? [],
                sizeUsd,
                expiresInDays),
            cancellationToken);
    }

    private static T Parse<T>(string value, string name)
        where T : struct, Enum
        => Enum.TryParse<T>(value?.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidRecommendationException($"{name} must be one of {string.Join(", ", Enum.GetNames<T>())}.");
}
