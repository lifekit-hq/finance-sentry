namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("research/assets")]
public class AssetDossierController(
    IQueryHandler<GetAssetDossierQuery, AssetDossierResult> handler,
    IQueryHandler<GetAssetLedgerReadQuery, AssetLedgerReadResult> ledgerReadHandler,
    ICommandHandler<GenerateAssetLedgerReadCommand, AssetLedgerReadResult> generateHandler,
    IQueryHandler<GetAssetChartQuery, AssetChartResult> chartHandler,
    ICommandHandler<SetAssetPriceTargetCommand, DossierUserTarget> setTargetHandler,
    ICommandHandler<ClearAssetPriceTargetCommand, bool> clearTargetHandler) : ControllerBase
{
    [HttpGet("{symbol}/dossier")]
    public async Task<IActionResult> Get(string symbol, CancellationToken ct)
    {
        var result = await handler.Handle(new GetAssetDossierQuery(User.RequireUserId(), symbol), ct);
        return Ok(result);
    }

    /// <summary>Cached asset narrative — instant, never invokes the agent (feature 421, US3).</summary>
    [Authorize(Policy = AuthPolicies.RequireAiUse)]
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
    [Authorize(Policy = AuthPolicies.RequireAiUse)]
    [HttpPost("{symbol}/narrative")]
    public async Task<IActionResult> GenerateNarrative(
        string symbol, [FromQuery] bool force, CancellationToken ct)
    {
        var result = await generateHandler.Handle(
            new GenerateAssetLedgerReadCommand(User.RequireUserId(), symbol, force), ct);
        return Ok(result);
    }

    /// <summary>Price chart for one range (1M, 6M, YTD, 1Y, 5Y, MAX) with the S&amp;P 500, the user's fills and avg cost.</summary>
    [HttpGet("{symbol}/chart")]
    public async Task<IActionResult> GetChart(string symbol, [FromQuery] string? range, CancellationToken ct)
    {
        var canonical = range is null ? ChartRange.OneYear : ChartRange.Normalize(range)
            ?? throw new InvalidChartRangeException(range);
        var result = await chartHandler.Handle(new GetAssetChartQuery(User.RequireUserId(), symbol, canonical), ct);
        return Ok(result);
    }

    /// <summary>Sets or replaces the user's own fair-value target for the symbol.</summary>
    [HttpPut("{symbol}/target")]
    public async Task<IActionResult> SetTarget(string symbol, [FromBody] SetAssetPriceTargetRequest request, CancellationToken ct)
    {
        var result = await setTargetHandler.Handle(
            new SetAssetPriceTargetCommand(User.RequireUserId(), symbol, request.TargetPrice, request.Currency), ct);
        return Ok(result);
    }

    /// <summary>Removes the user's target for the symbol; 204 whether or not one was set.</summary>
    [HttpDelete("{symbol}/target")]
    public async Task<IActionResult> ClearTarget(string symbol, CancellationToken ct)
    {
        await clearTargetHandler.Handle(new ClearAssetPriceTargetCommand(User.RequireUserId(), symbol), ct);
        return NoContent();
    }
}

public record SetAssetPriceTargetRequest(decimal TargetPrice, string? Currency);
