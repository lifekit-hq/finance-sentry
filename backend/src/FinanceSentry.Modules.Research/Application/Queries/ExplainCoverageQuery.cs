namespace FinanceSentry.Modules.Research.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>Why a ticker is or is not in the coverage universe, rule by rule (see <see cref="CoverageExplanation"/>).</summary>
public sealed record ExplainCoverageQuery(Guid UserId, string Ticker) : IQuery<CoverageExplanation>;

public sealed class ExplainCoverageQueryHandler(
    CoverageExplainer explainer, ICoverageMemoRepository memos, TimeProvider clock)
    : IQueryHandler<ExplainCoverageQuery, CoverageExplanation>
{
    public async Task<CoverageExplanation> Handle(ExplainCoverageQuery query, CancellationToken ct)
    {
        var explanation = await explainer.ExplainAsync(query.UserId, query.Ticker, clock.GetUtcNow(), ct);
        var memo = await memos.GetLatestUnscopedAsync(query.UserId, explanation.Ticker, ct);
        return memo is null
            ? explanation
            : explanation with { Memo = MemoView(memo), Member = explanation.Member is null ? null : explanation.Member with { LatestMemoAt = memo.CreatedAt } };
    }

    private static CoverageMemoView MemoView(CoverageMemo memo)
    {
        var structured = memo.Status == CoverageMemoStatus.Structured ? CoverageMemoParser.Deserialize(memo.Body) : null;
        return new CoverageMemoView(
            memo.Ticker, memo.CreatedAt, memo.Status.ToString(), memo.Verdict, memo.ModelId, memo.CostUsd,
            structured, structured is null ? memo.Body : null);
    }
}
