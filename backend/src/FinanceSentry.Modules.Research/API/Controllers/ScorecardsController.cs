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
    IQueryHandler<GetScorecardVerdictsQuery, IReadOnlyList<ScorecardVerdictSummary>> verdictsHandler,
    IQueryHandler<GetVerdictCoverageAuditQuery, VerdictCoverageAudit> coverageAuditHandler) : ControllerBase
{
    /// <summary>
    /// The verdict-coverage audit (#1413): every held, thesis, watched and coverage symbol of the caller with the state of
    /// each axis and section a verdict reads and a typed gap reason where one has no data. Read-only; the caller's own
    /// holdings are read at request time. Same <c>ai.use</c> gate as the verdicts it audits.
    /// </summary>
    [HttpGet("coverage-audit")]
    public async Task<IActionResult> GetCoverageAudit(CancellationToken ct) =>
        Ok(await coverageAuditHandler.Handle(new GetVerdictCoverageAuditQuery(User.RequireUserId()), ct));

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
