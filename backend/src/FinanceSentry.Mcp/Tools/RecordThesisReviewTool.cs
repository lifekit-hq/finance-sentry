using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class RecordThesisReviewTool(
    ICommandHandler<RecordThesisReviewCommand, RecordThesisReviewResult> handler,
    IIdentityResolver identity,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "record_thesis_review")]
    [Description("Records a review of a thesis that changes nothing in it - the 'no change' record that closes a thesis review. Appends a price-stamped Reviewed event to the thesis's event trail: decisionNote is your verdict (for example 'no change' or 'hold'), audit.rationale is the reasoning and audit.evidence what it rested on; origin is User or Ledger. The thesis itself is not touched and nothing is put to the owner. Needs no companion event: use it whether the review was prompted by a ThesisReviewDue event, a break, or the owner asking. A review that wants the thesis changed goes through propose_thesis_revision instead. Read it back with list_thesis_events (subjectId = the thesis id). Refused (THESIS_NOT_FOUND) for a thesis that is not the caller's, and (INVALID_THESIS_REVIEW) for a blank or over-long verdict (200 characters) or rationale (2000).")]
    public async Task<RecordThesisReviewResult?> ExecuteAsync(
        [Description("The id of the thesis that was reviewed (from list_theses).")] Guid thesisId,
        [Description("The verdict of the review, for example 'no change' or 'hold'. Required, max 200 characters.")] string verdict,
        [Description("Why the thesis stands as it is: what was checked and what it showed. Required, max 2000 characters.")] string rationale,
        [Description("References the review rests on: newsArticleIds, filingAccessions, eventIds, fundamentalsAsOf.")] ThesisEvidenceRefs? evidence = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        return await handler.Handle(
            new RecordThesisReviewCommand(
                effective.Value, thesisId, verdict, rationale, evidence, McpCallerOrigin.Of(httpContextAccessor)),
            cancellationToken);
    }
}
