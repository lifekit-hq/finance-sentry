namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary>
/// <see cref="IPolicyAckReader"/> impl over the Risk acknowledgement store. Worsening is decided by
/// <see cref="IRiskEvaluationService"/> — the one place that knows the ack's step and direction — so a
/// policy re-opens for derived alerts exactly when Risk re-opens it as <c>Worsened</c>.
/// </summary>
public sealed class PolicyAckReader(
    IPolicyViolationAckRepository ackRepo,
    IBookSnapshotReader bookReader,
    IRiskRuleSetRepository ruleSetRepo,
    IAllocationPolicySource allocationPolicySource,
    IDrawdownCheckProvider drawdownProvider,
    IRiskEvaluationService evaluationService) : IPolicyAckReader
{
    public async Task<bool> IsPolicySilencedAsync(Guid userId, string policyKey, CancellationToken ct = default)
    {
        var acks = await ackRepo.ListActiveUnscopedAsync(userId, ct);
        if (!acks.Any(a => a.RuleKey == policyKey))
        {
            return false;
        }

        var book = await bookReader.ReadAsync(userId, ct);
        var ruleSet = await ruleSetRepo.GetCurrentUnscopedAsync(userId, ct);
        var allocationTargets = await allocationPolicySource.GetAllocationTargetsAsync(userId, ct);
        var now = DateTimeOffset.UtcNow;
        var drawdown = await drawdownProvider.GetAsync(userId, book, now, ct);
        var report = evaluationService.Evaluate(book, ruleSet, allocationTargets, acks, now, drawdown);

        return !report.Violations.Any(v => v.RuleKey == policyKey && v.HasWorsenedPastStep);
    }
}
