namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// The user's detected commitments in the shape an estimate needs: what each costs a month now and when it
/// ends. Callers include jobs with no person in scope, so every read names the user.
/// </summary>
public interface IActiveCommitmentsReader
{
    Task<IReadOnlyList<ActiveCommitment>> GetActiveCommitmentsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The user's commitments that stopped (an installment paid off, a subscription or rent that lapsed), so a caller
    /// can tell a line whose commitment ended from one that never had any.
    /// </summary>
    Task<IReadOnlyList<ActiveCommitment>> GetEndedCommitmentsAsync(Guid userId, CancellationToken ct = default);
}

/// <param name="Key">
/// The key the detector stored the commitment under (the one its charges resolve to). For a legacy hand-typed row
/// (<paramref name="IsTracked"/> false) it is a <c>manual:</c> key no charge ever takes, so a caller re-derives a
/// transaction-matching key from <paramref name="DisplayName"/>.
/// </param>
/// <param name="Kind"><see cref="SubscriptionKinds"/> value: an open-ended service or a fixed-term installment.</param>
/// <param name="Amount">What the latest charge cost, native, in <paramref name="Currency"/>.</param>
/// <param name="EndDate">Final payment month, when known.</param>
public sealed record ActiveCommitment(
    string Key,
    string DisplayName,
    bool IsTracked,
    string Kind,
    string Cadence,
    decimal Amount,
    string Currency,
    DateOnly? EndDate);
