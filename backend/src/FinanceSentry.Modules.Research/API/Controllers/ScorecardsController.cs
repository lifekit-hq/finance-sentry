namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// A single symbol's scorecard with its verdict word and full drill-down (spec 726 US2, FR-021). Verdicts are the
/// owner's: the endpoint needs <c>ai.use</c> (the Owner role), the same gate as the dossier's Ledger read, so a Member
/// of an instance never sees a BUY/AVOID word. It is information, not advice, and the UI says so beside every verdict.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.RequireAiUse)]
[Route("research/scorecards")]
public class ScorecardsController(
    IQueryHandler<GetScorecardQuery, ScorecardResult> handler,
    IQueryHandler<GetScorecardVerdictsQuery, IReadOnlyList<ScorecardVerdictSummary>> verdictsHandler) : ControllerBase
{
    /// <summary>
    /// The verdict word of a comma-separated list of symbols in one call (the Accounts position chips). Same
    /// <c>ai.use</c> gate as the single read: a caller without it gets 403, never a word.
    /// </summary>
    [HttpGet("verdicts")]
    public async Task<IActionResult> GetVerdicts([FromQuery] string? symbols, CancellationToken ct) =>
        Ok(await verdictsHandler.Handle(
            new GetScorecardVerdictsQuery((symbols ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)), ct));

    [HttpGet("{symbol}")]
    public async Task<IActionResult> Get(string symbol, CancellationToken ct) =>
        Ok(await handler.Handle(new GetScorecardQuery(symbol), ct));
}
