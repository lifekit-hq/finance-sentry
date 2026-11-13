namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.Coverage;

/// <summary>Why a ticker is or is not in the coverage universe, rule by rule (see <see cref="CoverageExplanation"/>).</summary>
public sealed record ExplainCoverageQuery(Guid UserId, string Ticker) : IQuery<CoverageExplanation>;

public sealed class ExplainCoverageQueryHandler(CoverageExplainer explainer, TimeProvider clock)
    : IQueryHandler<ExplainCoverageQuery, CoverageExplanation>
{
    public Task<CoverageExplanation> Handle(ExplainCoverageQuery query, CancellationToken ct)
        => explainer.ExplainAsync(query.UserId, query.Ticker, clock.GetUtcNow(), ct);
}
