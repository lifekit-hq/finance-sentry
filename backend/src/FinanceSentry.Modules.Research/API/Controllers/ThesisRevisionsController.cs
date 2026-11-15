namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The owner's confirm surface for thesis revisions Ledger proposes. Confirm is the single step that writes the thesis;
/// a promotion runs the risk gate first and answers 409 with the refused rule, leaving the proposal pending, when the
/// gate refuses. Owner-only (<c>ai.use</c>), and the decide handler refuses any non-owner actor again.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.RequireAiUse)]
[Route("research/thesis-revisions")]
public class ThesisRevisionsController(
    IQueryHandler<GetThesisRevisionsQuery, ThesisRevisionsResult> listHandler,
    ICommandHandler<DecideThesisRevisionCommand, DecideThesisRevisionResult> decideHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await listHandler.Handle(new GetThesisRevisionsQuery(User.RequireUserId()), ct));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmThesisRevisionRequest? request, CancellationToken ct)
    {
        var result = await decideHandler.Handle(
            new DecideThesisRevisionCommand(
                User.RequireUserId(), id, ThesisRevisionVerdict.Confirm, request?.Note, request?.OverrideRisk ?? false), ct);
        return result.Outcome == ThesisRevisionOutcome.RiskGateRefused ? Conflict(result) : Ok(result);
    }

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectThesisRevisionRequest request, CancellationToken ct)
        => Ok(await decideHandler.Handle(
            new DecideThesisRevisionCommand(User.RequireUserId(), id, ThesisRevisionVerdict.Reject, request.Reason), ct));
}

public sealed record ConfirmThesisRevisionRequest(string? Note, bool OverrideRisk = false);

public sealed record RejectThesisRevisionRequest(string? Reason);
