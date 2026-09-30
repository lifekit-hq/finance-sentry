namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Risk.Domain.Repositories;

/// <summary><see cref="IPolicyAckReader"/> impl over the Risk acknowledgement store.</summary>
public sealed class PolicyAckReader(IPolicyViolationAckRepository ackRepo) : IPolicyAckReader
{
    public async Task<bool> IsPolicyAcknowledgedAsync(Guid userId, string policyKey, CancellationToken ct = default)
        => (await ackRepo.ListActiveAsync(userId, ct)).Any(a => a.RuleKey == policyKey);
}
