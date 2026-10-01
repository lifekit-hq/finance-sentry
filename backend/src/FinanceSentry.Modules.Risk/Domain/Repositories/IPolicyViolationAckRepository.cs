namespace FinanceSentry.Modules.Risk.Domain.Repositories;

public interface IPolicyViolationAckRepository
{
    Task<IReadOnlyList<PolicyViolationAck>> ListActiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's active acknowledgements for the daily check and the alert-side reader, which can run with no person in scope. Opts out of the Owner query filter.</summary>
    Task<IReadOnlyList<PolicyViolationAck>> ListActiveUnscopedAsync(Guid userId, CancellationToken ct = default);

    Task<PolicyViolationAck?> FindActiveAsync(Guid userId, string ruleKey, string subject, CancellationToken ct = default);

    Task AddAsync(PolicyViolationAck ack, CancellationToken ct = default);

    Task DeactivateAsync(Guid id, CancellationToken ct = default);
}
