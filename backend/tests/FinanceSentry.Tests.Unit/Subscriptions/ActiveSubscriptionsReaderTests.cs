namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for the cross-module <c>IActiveSubscriptionsReader</c> adapter (#538).
/// </summary>
public class ActiveSubscriptionsReaderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static DetectedSubscription Make(
        string normalized, string display, string kind = SubscriptionKinds.Subscription)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return DetectedSubscription.Create(
            UserId.ToString(), normalized, display, "monthly", 15m, 15m, "EUR",
            today.AddDays(-30), today, occurrenceCount: 3, confidenceScore: 3,
            category: null, kind: kind);
    }

    private static (ActiveSubscriptionsReader reader, Mock<IDetectedSubscriptionRepository> repo) MakeSut(
        params DetectedSubscription[] rows)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>();
        repo.Setup(r => r.GetActiveByUserIdAsync(UserId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        return (new ActiveSubscriptionsReader(repo.Object), repo);
    }

    [Fact]
    public async Task GetActiveCommitmentMerchantKeys_ReadsOnlyActiveRows()
    {
        // Status filtering lives in the repository's active-only query; going through
        // GetByUserIdAsync would silently pull dismissed and completed commitments in.
        var (reader, repo) = MakeSut(Make("netflix", "Netflix"));

        await reader.GetActiveCommitmentMerchantKeysAsync(UserId);

        repo.Verify(r => r.GetActiveByUserIdAsync(UserId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(
            r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetActiveCommitmentMerchantKeys_IncludesInstallments()
    {
        // Unlike GetActiveSubscriptionsAsync, which is scoped to recurring services for
        // cash-flow projection, "committed spend" covers every kind of active commitment.
        var (reader, _) = MakeSut(
            Make("netflix", "Netflix"),
            Make("installment:allo:2340", "Алло", SubscriptionKinds.Installment));

        var keys = await reader.GetActiveCommitmentMerchantKeysAsync(UserId);

        keys.Should().BeEquivalentTo(["netflix", "installment:allo:2340"]);
    }

    [Fact]
    public async Task GetActiveSubscriptions_StillExcludesInstallments()
    {
        // Regression guard: the new method must not have widened the existing one, which
        // Liquidity's cash-flow projection depends on.
        var (reader, _) = MakeSut(
            Make("netflix", "Netflix"),
            Make("installment:allo:2340", "Алло", SubscriptionKinds.Installment));

        var summaries = await reader.GetActiveSubscriptionsAsync(UserId);

        summaries.Should().ContainSingle().Which.MerchantNameDisplay.Should().Be("Netflix");
    }

    [Fact]
    public async Task GetActiveInstallmentPlans_ExcludesSubscriptionKindCommitments()
    {
        // The kind filter is what keeps categorization (#553) from calling every recurring
        // debit — Netflix, a monthly transfer to a person — a loan repayment.
        var (reader, _) = MakeSut(
            Make("netflix", "Netflix"),
            Make("ліза", "Ліза"),
            Make("516936", "Іпотека", SubscriptionKinds.Installment),
            Make("installment:allo:2340", "Алло", SubscriptionKinds.Installment));

        var plans = await reader.GetActiveInstallmentPlansAsync(UserId);

        plans.Select(p => p.Key).Should().BeEquivalentTo(["516936", "installment:allo:2340"]);
    }

    [Fact]
    public async Task GetActiveInstallmentPlans_CarryTheAmountTheChargesClusterAround()
    {
        // Masked-card keys collide across every card on one BIN, so the amount is the only
        // thing that tells the mortgage apart from a small transfer.
        var (reader, _) = MakeSut(Make("516936", "Іпотека", SubscriptionKinds.Installment));

        var plans = await reader.GetActiveInstallmentPlansAsync(UserId);

        plans.Should().ContainSingle().Which.ExpectedAmount.Should().Be(15m);
    }

    [Fact]
    public async Task GetActiveInstallmentPlans_ReadsOnlyActiveRows()
    {
        var (reader, repo) = MakeSut(Make("installment:allo:2340", "Алло", SubscriptionKinds.Installment));

        await reader.GetActiveInstallmentPlansAsync(UserId);

        repo.Verify(r => r.GetActiveByUserIdAsync(UserId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(
            r => r.GetByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetActiveCommitmentMerchantKeys_NoActiveCommitments_ReturnsEmptySet()
    {
        var (reader, _) = MakeSut();

        var keys = await reader.GetActiveCommitmentMerchantKeysAsync(UserId);

        keys.Should().BeEmpty();
    }
}
