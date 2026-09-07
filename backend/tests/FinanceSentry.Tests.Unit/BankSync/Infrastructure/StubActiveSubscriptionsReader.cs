namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// Test double for <see cref="IActiveSubscriptionsReader"/> returning a fixed set of active
/// installment plans, so categorization tests can exercise the repayment branch without a
/// Subscriptions database. The other two reads stay empty: a caller that reaches for the wrong
/// one gets nothing back rather than a coincidentally right answer.
/// </summary>
internal sealed class StubActiveSubscriptionsReader(params ActiveInstallmentPlan[] plans)
    : IActiveSubscriptionsReader
{
    public static readonly StubActiveSubscriptionsReader Empty = new();

    private readonly IReadOnlyList<ActiveInstallmentPlan> _plans = plans;

    public Task<IReadOnlyList<ActiveSubscriptionSummary>> GetActiveSubscriptionsAsync(
        Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ActiveSubscriptionSummary>>([]);

    public Task<IReadOnlySet<string>> GetActiveCommitmentMerchantKeysAsync(
        Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.Ordinal));

    public Task<IReadOnlyList<ActiveInstallmentPlan>> GetActiveInstallmentPlansAsync(
        Guid userId, CancellationToken ct = default)
        => Task.FromResult(_plans);
}
