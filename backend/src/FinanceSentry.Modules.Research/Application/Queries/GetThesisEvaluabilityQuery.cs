namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Domain.ThesisMonitor;

public record GetThesisEvaluabilityQuery(Guid UserId) : IQuery<IReadOnlyList<ThesisEvaluabilityReport>>;

/// <summary>
/// Per-trigger structural evaluability for every thesis a user owns. Reports what
/// <see cref="Validation.ThesisTriggerVocabulary"/> would reject on save, so a thesis carrying
/// triggers saved before that gate existed shows the gap instead of silently claiming coverage
/// it doesn't have.
/// </summary>
public record ThesisTriggerEvaluabilityView(
    string Metric,
    string Direction,
    decimal Threshold,
    bool IsEvaluable,
    string? Reason);

public record ThesisEvaluabilityReport(
    Guid ThesisId,
    string Ticker,
    int TriggerCount,
    int EvaluableCount,
    bool FullyCovered,
    IReadOnlyList<ThesisTriggerEvaluabilityView> Triggers);

public class GetThesisEvaluabilityQueryHandler(IThesisRepository repo)
    : IQueryHandler<GetThesisEvaluabilityQuery, IReadOnlyList<ThesisEvaluabilityReport>>
{
    public async Task<IReadOnlyList<ThesisEvaluabilityReport>> Handle(
        GetThesisEvaluabilityQuery query, CancellationToken ct)
    {
        var theses = await repo.ListAsync(query.UserId, ct);
        return theses.Select(ToReport).ToList();
    }

    private static ThesisEvaluabilityReport ToReport(InvestmentThesis thesis)
    {
        var views = thesis.InvalidationTriggers
            .Select(trigger =>
            {
                var isEvaluable = ThesisTriggerEvaluability.IsStructurallyEvaluable(trigger, out var reason);
                return new ThesisTriggerEvaluabilityView(
                    trigger.Metric, trigger.Direction, trigger.Threshold, isEvaluable, reason);
            })
            .ToList();

        var evaluableCount = views.Count(v => v.IsEvaluable);

        return new ThesisEvaluabilityReport(
            thesis.Id,
            thesis.Ticker,
            views.Count,
            evaluableCount,
            FullyCovered: views.Count > 0 && evaluableCount == views.Count,
            views);
    }
}
