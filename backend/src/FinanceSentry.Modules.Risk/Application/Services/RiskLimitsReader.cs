namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary><see cref="IRiskLimitsReader"/> impl over the internal rule-set repository.</summary>
public sealed class RiskLimitsReader(IRiskRuleSetRepository ruleSets) : IRiskLimitsReader
{
    public async Task<RiskLimits?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
    {
        var current = await ruleSets.GetCurrentUnscopedAsync(userId, ct);
        return current is null ? null : new RiskLimits(current.MaxPositionWeightPct, current.MinCashBufferPct);
    }

    public Task<IReadOnlyList<Guid>> ListUserIdsWithRuleSetsAsync(CancellationToken ct = default)
        => ruleSets.GetUserIdsWithRuleSetsUnscopedAsync(ct);
}
