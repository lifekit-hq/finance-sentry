namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The Decisions queue (#1069). Confirm only records the owner's decision - Finance Sentry never places, stages or
/// transmits an order; the owner places it at the broker from the order text.
/// </summary>
[ApiController]
[Authorize]
[Route("research/recommendations")]
public class RecommendationsController(
    IQueryHandler<GetRecommendationsQuery, RecommendationsResult> listHandler,
    ICommandHandler<DecideRecommendationCommand, RecommendationDto> decideHandler) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await listHandler.Handle(new GetRecommendationsQuery(User.RequireUserId()), ct));

    [HttpPost("{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, [FromBody] ConfirmRecommendationRequest? request, CancellationToken ct)
        => Ok(await decideHandler.Handle(
            new DecideRecommendationCommand(User.RequireUserId(), id, RecommendationVerdict.Confirm, request?.Note), ct));

    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectRecommendationRequest request, CancellationToken ct)
        => Ok(await decideHandler.Handle(
            new DecideRecommendationCommand(User.RequireUserId(), id, RecommendationVerdict.Reject, request.Reason), ct));
}

public sealed record ConfirmRecommendationRequest(string? Note);

public sealed record RejectRecommendationRequest(string? Reason);
