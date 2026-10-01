namespace FinanceSentry.Modules.Risk.Application.Queries;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Repositories;

public record RiskRuleSetDto(
    Guid UserId,
    int Version,
    decimal? MaxPositionWeightPct,
    decimal? MaxSleeveWeightPct,
    decimal? MinCashBufferPct,
    decimal? MaxLossPerThesisPct,
    decimal? MaxNewPositionPct,
    int? TurnoverBudgetPerQuarter,
    DateTimeOffset CreatedAt)
{
    public static RiskRuleSetDto FromEntity(RiskRuleSet r) => new(
        r.UserId, r.Version, r.MaxPositionWeightPct, r.MaxSleeveWeightPct, r.MinCashBufferPct,
        r.MaxLossPerThesisPct, r.MaxNewPositionPct, r.TurnoverBudgetPerQuarter, r.CreatedAt);
}

public record GetRiskRuleSetQuery(Guid UserId) : IQuery<RiskRuleSetDto?>;

public sealed class GetRiskRuleSetQueryHandler(IRiskRuleSetRepository repo)
    : IQueryHandler<GetRiskRuleSetQuery, RiskRuleSetDto?>
{
    public async Task<RiskRuleSetDto?> Handle(GetRiskRuleSetQuery query, CancellationToken ct)
    {
        var current = await repo.GetCurrentUnscopedAsync(query.UserId, ct);
        return current is null ? null : RiskRuleSetDto.FromEntity(current);
    }
}
