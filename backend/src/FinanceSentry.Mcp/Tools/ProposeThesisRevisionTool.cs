using System.ComponentModel;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Revisions;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tools;

[McpServerToolType]
public sealed class ProposeThesisRevisionTool(
    ICommandHandler<ProposeThesisRevisionCommand, ThesisRevisionDto> handler,
    IIdentityResolver identity,
    IHttpContextAccessor httpContextAccessor)
{
    [McpServerTool(Name = "propose_thesis_revision")]
    [Description("Puts one change to a thesis, or one promotion, to the owner as a pending proposal. Nothing is written until the owner confirms it: a promotion then runs the risk gate, a revision is saved with your rationale and evidence in the audit trail. You cannot promote or save a thesis yourself; this is the path. kind: Promote (subjectId = an Active candidate id; thesisText, keyDataPoints, catalysts, invalidationTriggers, narrativeFalsifiers, entryPrice are optional and default from the scorecard and the deterministic trigger prefill), NarrativeUpdate (subjectId = a thesis id; any of thesisText, keyDataPoints, catalysts, narrativeFalsifiers, entryPrice - what you leave out stays as it is), TriggerChange (subjectId = a thesis id; invalidationTriggers only, in the closed trigger vocabulary), Close (subjectId = a thesis id; rationale only). rationale is required and is what the owner decides on; evidence (newsArticleIds, filingAccessions, eventIds, fundamentalsAsOf) says what it rests on. One proposal is open per subject: a newer one supersedes it. Read list_thesis_revisions first, so you supersede deliberately and do not re-propose what the owner just rejected. Proposals expire after expiresInDays (default 14, max 30). Returns the pending proposal.")]
    public async Task<ThesisRevisionDto?> ExecuteAsync(
        [Description("Promote, NarrativeUpdate, TriggerChange or Close.")] string kind,
        [Description("The candidate id (Promote) or the thesis id (every other kind).")] Guid subjectId,
        [Description("Why - the owner decides on this. Required, max 2000 characters.")] string rationale,
        [Description("Proposed thesis narrative (Promote, NarrativeUpdate). Max 4000 characters.")] string? thesisText = null,
        [Description("Proposed key data points (Promote, NarrativeUpdate); [] clears them on an update.")] IReadOnlyList<ThesisDataPoint>? keyDataPoints = null,
        [Description("Proposed catalysts (Promote, NarrativeUpdate); [] clears them on an update.")] IReadOnlyList<ThesisCatalyst>? catalysts = null,
        [Description("Proposed invalidation triggers (Promote, TriggerChange), in the same closed vocabulary save_thesis accepts. Omitted on a Promote, the deterministic prefill from the candidate's scorecard is used.")] IReadOnlyList<ThesisInvalidationTrigger>? invalidationTriggers = null,
        [Description("Proposed narrative falsifiers (Promote, NarrativeUpdate); [] clears them on an update.")] IReadOnlyList<string>? narrativeFalsifiers = null,
        [Description("Proposed entry price per share (Promote, NarrativeUpdate).")] decimal? entryPrice = null,
        [Description("References the proposal rests on: newsArticleIds, filingAccessions, eventIds, fundamentalsAsOf.")] ThesisEvidenceRefs? evidence = null,
        [Description("Promote only: the position size in USD the risk gate checks when the owner confirms. Defaults to 0 (no sizing).")] decimal proposedUsd = 0m,
        [Description("Promote only: a tracking-only promotion that never draws down real cash.")] bool paper = false,
        [Description("Days before an undecided proposal expires (1-30, default 14).")] int? expiresInDays = null,
        CancellationToken cancellationToken = default)
    {
        var effective = identity.GetUserId();
        if (effective is null)
        {
            return null;
        }

        var parsed = Enum.TryParse<ThesisRevisionKind>(kind?.Trim(), ignoreCase: true, out var k) && Enum.IsDefined(k)
            ? k
            : throw new InvalidThesisRevisionException("kind must be Promote, NarrativeUpdate, TriggerChange or Close.");
        return await handler.Handle(
            new ProposeThesisRevisionCommand(
                effective.Value,
                parsed,
                subjectId,
                rationale,
                new ThesisRevisionContent(thesisText, keyDataPoints, catalysts, invalidationTriggers, narrativeFalsifiers, entryPrice),
                evidence,
                proposedUsd,
                paper,
                expiresInDays,
                McpCallerOrigin.Of(httpContextAccessor)),
            cancellationToken);
    }
}
