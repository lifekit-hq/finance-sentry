namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("research/assets")]
public class AssetDossierController(
    IQueryHandler<GetAssetDossierQuery, AssetDossierResult> handler,
    IQueryHandler<GetAssetLedgerReadQuery, AssetLedgerReadResult> ledgerReadHandler,
    ICommandHandler<GenerateAssetLedgerReadCommand, AssetLedgerReadResult> generateHandler) : ControllerBase
{
    [HttpGet("{symbol}/dossier")]
    public async Task<IActionResult> Get(string symbol, CancellationToken ct)
    {
        var result = await handler.Handle(new GetAssetDossierQuery(User.RequireUserId(), symbol), ct);
        return Ok(result);
    }

    /// <summary>Cached asset narrative — instant, never invokes the agent (feature 421, US3).</summary>
    [HttpGet("{symbol}/narrative")]
    public async Task<IActionResult> GetNarrative(string symbol, CancellationToken ct)
    {
        var result = await ledgerReadHandler.Handle(
            new GetAssetLedgerReadQuery(User.RequireUserId(), symbol), ct);
        return Ok(result);
    }

    /// <summary>
    /// Generates the asset narrative through the agent loop and caches it. A fresh cached copy is
    /// returned as-is unless <c>force=true</c>.
    /// </summary>
    [HttpPost("{symbol}/narrative")]
    public async Task<IActionResult> GenerateNarrative(
        string symbol, [FromQuery] bool force, CancellationToken ct)
    {
        var result = await generateHandler.Handle(
            new GenerateAssetLedgerReadCommand(User.RequireUserId(), symbol, force), ct);
        return Ok(result);
    }
}
