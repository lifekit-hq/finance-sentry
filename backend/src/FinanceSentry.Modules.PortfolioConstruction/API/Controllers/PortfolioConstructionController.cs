namespace FinanceSentry.Modules.PortfolioConstruction.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Queries;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>
/// The ranked universe screen with the archetype's suggested weights (spec 726 US1, US3, FR-021). The verdicts on it
/// are the owner's: the endpoint needs <c>ai.use</c> (the Owner role), the same gate as the scorecard read, so a
/// Member of an instance never sees a verdict word. It is information, not advice, and the UI says so.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.RequireAiUse)]
[Route("portfolio-construction")]
public class PortfolioConstructionController(
    IQueryHandler<GetUniverseScreenQuery, UniverseScreenResult> handler,
    IOptions<PortfolioConstructionOptions> options) : ControllerBase
{
    [HttpGet("universe")]
    public async Task<IActionResult> GetUniverse([FromQuery] string? archetype, CancellationToken ct)
    {
        if (!GetUniverseScreenQueryHandler.TryResolve(options.Value, archetype, out _))
        {
            return BadRequest(new { errorCode = "UNKNOWN_ARCHETYPE", message = $"Unknown archetype '{archetype}'." });
        }

        return Ok(await handler.Handle(new GetUniverseScreenQuery(User.RequireUserId(), archetype), ct));
    }
}
