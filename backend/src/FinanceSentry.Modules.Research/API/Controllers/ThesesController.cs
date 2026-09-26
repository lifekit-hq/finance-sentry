namespace FinanceSentry.Modules.Research.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("research/theses")]
public class ThesesController(
    IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>> getTheses,
    IQueryHandler<GetThesisEvaluabilityQuery, IReadOnlyList<ThesisEvaluabilityReport>> getEvaluability,
    ICommandHandler<SaveThesisCommand, ThesisDto> saveThesis,
    ICommandHandler<DeleteThesisCommand, bool> deleteThesis) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await getTheses.Handle(new GetThesesQuery(User.RequireUserId()), ct);
        return Ok(items);
    }

    [HttpGet("evaluability")]
    public async Task<IActionResult> Evaluability(CancellationToken ct)
    {
        var report = await getEvaluability.Handle(new GetThesisEvaluabilityQuery(User.RequireUserId()), ct);
        return Ok(report);
    }

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveThesisRequest body, CancellationToken ct)
    {
        var saved = await saveThesis.Handle(
            new SaveThesisCommand(
                User.RequireUserId(),
                body.Id,
                body.Ticker,
                body.ThesisText,
                body.KeyDataPoints ?? [],
                body.Catalysts ?? [],
                body.InvalidationTriggers ?? []),
            ct);
        return Ok(saved);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await deleteThesis.Handle(new DeleteThesisCommand(User.RequireUserId(), id), ct);
        if (!ok)
        {
            throw new ThesisNotFoundException();
        }

        return NoContent();
    }
}

public record SaveThesisRequest(
    Guid? Id,
    string Ticker,
    string ThesisText,
    IReadOnlyList<ThesisDataPoint>? KeyDataPoints,
    IReadOnlyList<ThesisCatalyst>? Catalysts,
    IReadOnlyList<ThesisInvalidationTrigger>? InvalidationTriggers);
