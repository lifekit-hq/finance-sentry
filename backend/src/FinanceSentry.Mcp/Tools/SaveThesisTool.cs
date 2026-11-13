using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class SaveThesisTool(
    ICommandHandler<SaveThesisCommand, ThesisDto> handler,
    IIdentityResolver identity,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "save_thesis")]
    [Description("Creates or updates an investment thesis. Pass id=null to create; pass an existing id to update. Every create and revision is journaled (origin User or Ledger, prior and new version, rationale, evidence) and a revision can be undone with revert_thesis_revision. Pass rationale (why) and evidence (the news article ids, filing accessions, event ids and fundamentals as-of it rests on) so the trail says what the change rested on. At least one invalidationTrigger is expected for thesis-break detection to work. Provide entryPrice so that price_drawdown triggers are anchored to your entry, not the asset's historical peak.")]
    public async Task<ThesisDto?> ExecuteAsync(
        [Description("Ticker symbol the thesis is about.")] string ticker,
        [Description("Free-form thesis narrative — the investment case in Denys's words.")] string thesisText,
        [Description("Structured key data points that back the thesis.")] IReadOnlyList<ThesisDataPoint> keyDataPoints,
        [Description("Upcoming catalysts (dates + events) that could confirm or break the thesis.")] IReadOnlyList<ThesisCatalyst> catalysts,
        [Description("Invalidation triggers — quantitative conditions that mark the thesis broken (e.g. price_drawdown greaterThan 0.30 means 30% loss from entry). relative_return breaks the thesis on sustained benchmark-relative underperformance: set benchmarkTicker (e.g. \"SPY\" for the S&P 500, or any other ticker) and windowDays (the trailing trading-day return window compared between subject and benchmark) alongside direction/threshold/consecutivePeriods — e.g. relative_return lessThan -0.05 with benchmarkTicker=SPY, windowDays=63, consecutivePeriods=5 breaks when the position trails the S&P 500 by more than 5% over a trailing 63-trading-day window on each of the last 5 trading days.")] IReadOnlyList<ThesisInvalidationTrigger> invalidationTriggers,
        [Description("Entry price per share (the price you paid, not the asset's historical peak). Required for price_drawdown triggers to be anchored to entry.")] decimal? entryPrice = null,
        [Description("Thesis id when updating; null to create.")] Guid? id = null,
        [Description("Optional contemporaneous reasoning captured at creation time (FR-008b decision journal). Only applied on create.")] string? decisionNote = null,
        [Description("Why the thesis is being created or revised; kept in the audit trail.")] string? rationale = null,
        [Description("References the change rests on: newsArticleIds, filingAccessions, eventIds, fundamentalsAsOf. Kept in the audit trail.")] ThesisEvidenceRefs? evidence = null,
        [Description("Narrative falsifiers: free-text kill-switches no metric can express (a lost customer, a ruling, a management change). Not monitored by any job: they are model-checked, and handed to Ledger on every thesis review. Omit to leave the stored ones unchanged on an update; pass [] to clear them. At most 20, 500 characters each.")] IReadOnlyList<string>? narrativeFalsifiers = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new SaveThesisCommand(
                effective.Value,
                id,
                ticker,
                thesisText,
                keyDataPoints ?? [],
                catalysts ?? [],
                invalidationTriggers ?? [],
                entryPrice,
                decisionNote,
                McpCallerOrigin.Of(httpContextAccessor),
                rationale,
                evidence,
                narrativeFalsifiers),
            cancellationToken);
    }
}
