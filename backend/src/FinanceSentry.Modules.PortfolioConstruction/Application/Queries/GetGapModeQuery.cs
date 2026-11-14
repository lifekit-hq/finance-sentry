namespace FinanceSentry.Modules.PortfolioConstruction.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.PortfolioConstruction.API.Responses;
using FinanceSentry.Modules.PortfolioConstruction.Application.Services;

/// <summary>
/// Gap mode (spec 726 US4): the viewer's canonical book against the target the universe screen builds for the same
/// archetype, as missing, underweight and overweight names with amounts and sectors. The target is the universe
/// screen's own construction (coverage set, held and watchlist names, stored sector data, the viewer's IPS and risk
/// limits), read through its handler, so the two screens can never disagree about what the target is. Nothing is
/// stored, scored by a model or traded; the figures are informational (FR-018).
/// </summary>
public sealed record GetGapModeQuery(Guid UserId, string? Archetype) : IQuery<GapModeResult>;

public sealed class GetGapModeQueryHandler(
    IQueryHandler<GetUniverseScreenQuery, UniverseScreenResult> universe,
    IBookFiguresService bookFigures,
    TimeProvider clock) : IQueryHandler<GetGapModeQuery, GapModeResult>
{
    public async Task<GapModeResult> Handle(GetGapModeQuery query, CancellationToken ct)
    {
        var target = await universe.Handle(new GetUniverseScreenQuery(query.UserId, query.Archetype), ct);
        var book = await bookFigures.ReadAsync(query.UserId, ct);
        return GapModeCalculator.Compute(target, book, clock.GetUtcNow());
    }
}
