namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Application.Commands;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Domain;
using FinanceSentry.Modules.Subscriptions.Domain.Exceptions;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// A commitment the user adds by hand follows its transactions: it is keyed as the detection job
/// keys the picked transaction, each later charge under that key advances its due date, and it
/// lapses when the charges stop. Regression for rows that kept the dates they were typed with
/// and so read as due long after the last charge (a two-charge hosting plan detection cannot own).
/// </summary>
public class ManualCommitmentTrackingTests
{
    private const string Key = "acme hosting";
    private static readonly Guid UserGuid = Guid.NewGuid();
    private static readonly string UserId = UserGuid.ToString();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static DetectedSubscription Tracked(
        DateOnly chargeDate, string kind = SubscriptionKinds.Subscription, int? termCount = null) =>
        DetectedSubscription.CreateFromTransaction(
            UserId, Key, "Acme Hosting", 10m, "EUR", chargeDate, termCount, kind);

    private static (SubscriptionDetectionResultService sut, Mock<IDetectedSubscriptionRepository> repo) ResultService(
        params DetectedSubscription[] rows)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>();
        repo.Setup(r => r.GetLiveManualUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        repo.Setup(r => r.GetStaleActiveUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => rows.Where(r => r.Status == SubscriptionStatus.Active).ToList());
        return (new SubscriptionDetectionResultService(repo.Object), repo);
    }

    // --- Domain ---

    [Fact]
    public void CreateFromTransaction_KeysByTransactionAndExpectsNextChargeOneMonthOn()
    {
        var row = Tracked(new DateOnly(2026, 5, 21));

        row.MerchantNameNormalized.Should().Be(Key);
        row.IsManual.Should().BeTrue();
        row.IsTracked.Should().BeTrue();
        row.LastChargeDate.Should().Be(new DateOnly(2026, 5, 21));
        row.NextExpectedDate.Should().Be(new DateOnly(2026, 6, 21));
    }

    [Fact]
    public void LegacyHandTypedRow_IsNotTracked()
    {
        var legacy = DetectedSubscription.CreateManual(
            UserId, "Acme Hosting", 10m, "EUR", new DateOnly(2026, 5, 21), null, SubscriptionKinds.Subscription);

        legacy.IsTracked.Should().BeFalse();
    }

    [Fact]
    public void RecordCharge_LaterCharge_AdvancesDueDateAndAmount()
    {
        var row = Tracked(new DateOnly(2026, 5, 21));

        row.RecordCharge(new DateOnly(2026, 6, 22), 16.19m, "EUR").Should().BeTrue();

        row.LastChargeDate.Should().Be(new DateOnly(2026, 6, 22));
        row.NextExpectedDate.Should().Be(new DateOnly(2026, 7, 22));
        row.LastKnownAmount.Should().Be(16.19m);
        row.AverageAmount.Should().Be(16.19m);
        row.OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public void RecordCharge_ChargeAlreadySeen_ChangesNothing()
    {
        var row = Tracked(new DateOnly(2026, 5, 21));

        row.RecordCharge(new DateOnly(2026, 5, 21), 99m).Should().BeFalse();

        row.OccurrenceCount.Should().Be(1);
        row.LastKnownAmount.Should().Be(10m);
    }

    [Fact]
    public void RecordCharge_OnLapsedRow_ReactivatesIt()
    {
        var row = Tracked(new DateOnly(2026, 5, 21));
        row.MarkPotentiallyCancelled();

        row.RecordCharge(new DateOnly(2026, 9, 21), 10m);

        row.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public void RecordCharge_InstallmentReachingItsTerm_Completes()
    {
        var row = Tracked(new DateOnly(2026, 5, 21), SubscriptionKinds.Installment, termCount: 2);

        row.RecordCharge(new DateOnly(2026, 6, 21), 10m);

        row.Status.Should().Be(SubscriptionStatus.Completed);
    }

    // --- Detection result service ---

    [Fact]
    public async Task TrackManualCommitments_AdvancesTrackedRowByMatchingChargesOnly()
    {
        var row = Tracked(Today.AddDays(-70));
        var (sut, repo) = ResultService(row);

        await sut.TrackManualCommitmentsAsync(UserId,
        [
            new CommitmentCharge(Key, Today.AddDays(-40), 10m, "EUR"),
            new CommitmentCharge(Key, Today.AddDays(-10), 11m, "EUR"),
            new CommitmentCharge("someone else", Today.AddDays(-1), 50m, "EUR"),
        ]);

        row.LastChargeDate.Should().Be(Today.AddDays(-10));
        row.NextExpectedDate.Should().Be(Today.AddDays(-10).AddMonths(1));
        row.LastKnownAmount.Should().Be(11m);
        row.OccurrenceCount.Should().Be(3);
        repo.Verify(r => r.UpsertAsync(row, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TrackManualCommitments_LeavesLegacyHandTypedRowAlone()
    {
        var legacy = DetectedSubscription.CreateManual(
            UserId, "Acme Hosting", 10m, "EUR", Today.AddDays(-70), null, SubscriptionKinds.Subscription);
        var (sut, repo) = ResultService(legacy);

        await sut.TrackManualCommitmentsAsync(UserId,
            [new CommitmentCharge(Key, Today.AddDays(-10), 10m, "EUR")]);

        legacy.LastChargeDate.Should().Be(Today.AddDays(-70));
        repo.Verify(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkStale_TrackedManualRowWhoseChargesStopped_IsPotentiallyCancelled()
    {
        // The reproduced case: the last charge was months ago, so the commitment must lapse
        // instead of staying active with a due date long in the past.
        var row = Tracked(Today.AddDays(-105));
        var (sut, _) = ResultService(row);

        await sut.MarkStaleAsPotentiallyCancelledAsync(UserId);

        row.Status.Should().Be(SubscriptionStatus.PotentiallyCancelled);
    }

    [Fact]
    public async Task MarkStale_LegacyHandTypedRow_StaysActive()
    {
        var legacy = DetectedSubscription.CreateManual(
            UserId, "Acme Hosting", 10m, "EUR", Today.AddDays(-105), null, SubscriptionKinds.Subscription);
        var (sut, _) = ResultService(legacy);

        await sut.MarkStaleAsPotentiallyCancelledAsync(UserId);

        legacy.Status.Should().Be(SubscriptionStatus.Active);
    }

    // --- Add from a transaction ---

    private static readonly Guid TransactionId = Guid.NewGuid();

    private static (AddCommitmentCommandHandler sut, Mock<IDetectedSubscriptionRepository> repo) AddHandler(
        CommitmentTransaction? transaction, DetectedSubscription? existing = null)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>();
        repo.Setup(r => r.FindByUserAndMerchantUnscopedAsync(UserId, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        var reader = new Mock<ICommitmentTransactionReader>();
        reader.Setup(r => r.FindAsync(UserGuid, TransactionId, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        return (new AddCommitmentCommandHandler(repo.Object, reader.Object), repo);
    }

    private static readonly CommitmentTransaction Picked =
        new(Key, "ACME HOSTING GMBH", 16.19m, "EUR", new DateOnly(2026, 6, 22));

    [Fact]
    public async Task Add_FromTransaction_StoresRowKeyedByTheTransaction()
    {
        var (sut, repo) = AddHandler(Picked);
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, "Acme", null, null), default);

        saved.Should().NotBeNull();
        saved!.MerchantNameNormalized.Should().Be(Key);
        saved.MerchantNameDisplay.Should().Be("Acme");
        saved.LastKnownAmount.Should().Be(16.19m);
        saved.Currency.Should().Be("EUR");
        saved.LastChargeDate.Should().Be(new DateOnly(2026, 6, 22));
        saved.Kind.Should().Be(SubscriptionKinds.Subscription);
    }

    [Fact]
    public async Task Add_Installment_KeepsTermAndPlanStart()
    {
        var (sut, repo) = AddHandler(Picked);
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Installment, null, 20m, 12), default);

        saved!.TermCount.Should().Be(12);
        saved.StartDate.Should().Be(new DateOnly(2026, 6, 22));
        saved.MerchantNameDisplay.Should().Be("ACME HOSTING GMBH");
        saved.LastKnownAmount.Should().Be(20m);
    }

    [Fact]
    public async Task Add_TransactionNotFound_Throws404()
    {
        var (sut, _) = AddHandler(null);

        var act = () => sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null), default);

        await act.Should().ThrowAsync<CommitmentTransactionNotFoundException>();
    }

    [Fact]
    public async Task Add_ChargesAlreadyTracked_Throws409()
    {
        var (sut, _) = AddHandler(Picked, existing: Tracked(new DateOnly(2026, 6, 22)));

        var act = () => sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null), default);

        await act.Should().ThrowAsync<CommitmentAlreadyTrackedException>();
    }

    [Fact]
    public async Task Add_UnknownKind_Throws400()
    {
        var (sut, _) = AddHandler(Picked);

        var act = () => sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, "loan", null, null, null), default);

        await act.Should().ThrowAsync<InvalidCommitmentKindException>();
    }
}
