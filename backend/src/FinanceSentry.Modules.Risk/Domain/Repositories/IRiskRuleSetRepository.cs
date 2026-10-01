namespace FinanceSentry.Modules.Risk.Domain.Repositories;

public interface IRiskRuleSetRepository
{
    Task<RiskRuleSet?> GetCurrentAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's current rule set for the daily check and the cross-module readers, which can run with no person in scope. Opts out of the Owner query filter.</summary>
    Task<RiskRuleSet?> GetCurrentUnscopedAsync(Guid userId, CancellationToken ct = default);

    Task<RiskRuleSet> SaveNewVersionAsync(RiskRuleSet ruleSet, CancellationToken ct = default);

    /// <summary>Every user with a current rule set, for the portfolio scan sweep. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<Guid>> GetUserIdsWithRuleSetsUnscopedAsync(CancellationToken ct = default);
}
