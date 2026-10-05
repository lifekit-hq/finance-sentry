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
        DateOnly chargeDate, string kind = SubscriptionKinds.Subscription, int? termCount = null, int chargeCount = 1,
        string cadence = SubscriptionCadences.Monthly) =>
        DetectedSubscription.CreateFromTransaction(
            UserId, Key, "Acme Hosting", 10m, "EUR", chargeDate, chargeCount, termCount, kind, cadence);

    private static DetectedSubscription Legacy(DateOnly startDate, string kind = SubscriptionKinds.Subscription, int? termCount = null) =>
        DetectedSubscription.CreateManual(UserId, "Acme Hosting", 10m, "EUR", startDate, termCount, kind);

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
    public void CreateFromTransaction_AnnualCadence_ExpectsNextChargeAYearOnAndAdvancesByAYear()
    {
        var row = Tracked(new DateOnly(2026, 5, 21), cadence: SubscriptionCadences.Annual);

        row.Cadence.Should().Be(SubscriptionCadences.Annual);
        row.NextExpectedDate.Should().Be(new DateOnly(2027, 5, 21));

        row.RecordCharge(new DateOnly(2027, 5, 22), 10m, "EUR");

        row.NextExpectedDate.Should().Be(new DateOnly(2028, 5, 22));
    }

    [Fact]
    public void CreateFromTransaction_Installment_LeavesStartDateForTheEstimate()
    {
        var row = Tracked(new DateOnly(2026, 5, 21), SubscriptionKinds.Installment, termCount: 12, chargeCount: 5);

        row.StartDate.Should().BeNull();
        row.OccurrenceCount.Should().Be(5);
    }

    [Fact]
    public void LegacyHandTypedRow_IsNotTracked()
    {
        var legacy = Legacy(new DateOnly(2026, 5, 21));

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
        var legacy = Legacy(Today.AddDays(-70));
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
        var legacy = Legacy(Today.AddDays(-105));
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
        new(Key, "ACME HOSTING GMBH", 16.19m, "EUR", new DateOnly(2026, 6, 22), 1);

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
    public async Task Add_Installment_KeepsTermAndLeavesPlanStartForTheEstimate()
    {
        var (sut, repo) = AddHandler(Picked);
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Installment, null, 20m, 12), default);

        saved!.TermCount.Should().Be(12);
        saved.StartDate.Should().BeNull();
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
    public async Task Add_ActiveRowOfSameKindHoldsTheKey_Throws409NamingIt()
    {
        var (sut, _) = AddHandler(Picked, existing: Tracked(new DateOnly(2026, 6, 22)));

        var act = () => sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null), default);

        (await act.Should().ThrowAsync<CommitmentAlreadyTrackedException>())
            .Which.Message.Should().Contain("Acme Hosting");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Dismissed)]
    [InlineData(SubscriptionStatus.Completed)]
    [InlineData(SubscriptionStatus.PotentiallyCancelled)]
    public async Task Add_InactiveRowHoldsTheKey_RestoresItFromThePick(string status)
    {
        var existing = Tracked(new DateOnly(2026, 1, 21));
        SetStatus(existing, status);
        var (sut, repo) = AddHandler(Picked, existing);

        var id = await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, "Acme", 20m, null), default);

        id.Should().Be(existing.Id);
        existing.Status.Should().Be(SubscriptionStatus.Active);
        existing.DismissedAt.Should().BeNull();
        existing.MerchantNameDisplay.Should().Be("Acme");
        existing.LastKnownAmount.Should().Be(20m);
        existing.LastChargeDate.Should().Be(new DateOnly(2026, 6, 22));
        repo.Verify(r => r.UpsertAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_ActiveRowOfOtherKindHoldsTheKey_ReKindsItFromThePick()
    {
        var existing = Tracked(new DateOnly(2026, 5, 22));
        var (sut, _) = AddHandler(Picked, existing);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Installment, null, null, 12), default);

        existing.Kind.Should().Be(SubscriptionKinds.Installment);
        existing.TermCount.Should().Be(12);
        existing.IsManual.Should().BeTrue();
        existing.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Add_PickedLaterPayment_StartsOccurrencesAtItsChargeCount()
    {
        var (sut, repo) = AddHandler(Picked with { ChargeCount = 5 });
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Installment, null, null, 12), default);

        saved!.OccurrenceCount.Should().Be(5);
        saved.RemainingPayments.Should().Be(7);
    }

    [Fact]
    public async Task Add_PickedFinalPayment_CompletesTheInstallment()
    {
        var (sut, repo) = AddHandler(Picked with { ChargeCount = 12 });
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Installment, null, null, 12), default);

        saved!.Status.Should().Be(SubscriptionStatus.Completed);
    }

    [Fact]
    public async Task Add_PickedOlderCharge_AnchorsTheRowOnTheLatestSameKeyCharge()
    {
        var latest = new CommitmentTransaction(Key, "ACME HOSTING GMBH", 17.5m, "USD", new DateOnly(2026, 6, 22), 3);
        var (sut, repo) = AddHandler(latest);
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null), default);

        saved!.LastChargeDate.Should().Be(new DateOnly(2026, 6, 22));
        saved.NextExpectedDate.Should().Be(new DateOnly(2026, 7, 22));
        saved.LastKnownAmount.Should().Be(17.5m);
        saved.Currency.Should().Be("USD");
        saved.OccurrenceCount.Should().Be(3);
    }

    [Fact]
    public async Task Add_SingleChargeHistory_UsesTheRequestedCadence()
    {
        var (sut, repo) = AddHandler(Picked);
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null, SubscriptionCadences.Annual), default);

        saved!.Cadence.Should().Be(SubscriptionCadences.Annual);
        saved.NextExpectedDate.Should().Be(new DateOnly(2027, 6, 22));
    }

    [Fact]
    public async Task Add_NoHistoryAndNoRequestedCadence_DefaultsToMonthly()
    {
        var (sut, repo) = AddHandler(Picked);
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null), default);

        saved!.Cadence.Should().Be(SubscriptionCadences.Monthly);
    }

    [Fact]
    public async Task Add_StoresTheSubmittedCadenceEvenWhenHistorySuggestsAnother()
    {
        var (sut, repo) = AddHandler(Picked with { ChargeCount = 2, Cadence = SubscriptionCadences.Annual });
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null, SubscriptionCadences.Monthly), default);

        saved!.Cadence.Should().Be(SubscriptionCadences.Monthly);
    }

    [Fact]
    public async Task Add_UnknownCadence_Throws400()
    {
        var (sut, _) = AddHandler(Picked);

        var act = () => sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null, "weekly"), default);

        await act.Should().ThrowAsync<InvalidCommitmentCadenceException>();
    }

    [Fact]
    public async Task Add_RestoringARow_StoresTheSubmittedCadence()
    {
        var existing = Tracked(new DateOnly(2025, 6, 22));
        existing.MarkDismissed();
        var (sut, _) = AddHandler(Picked, existing);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Subscription, null, null, null, SubscriptionCadences.Annual), default);

        existing.Cadence.Should().Be(SubscriptionCadences.Annual);
        existing.NextExpectedDate.Should().Be(new DateOnly(2027, 6, 22));
    }

    [Fact]
    public async Task Add_PickedAmongSeveralCharges_LeavesInstallmentStartForTheEstimate()
    {
        var (sut, repo) = AddHandler(Picked with { ChargeCount = 5, Cadence = SubscriptionCadences.Monthly });
        DetectedSubscription? saved = null;
        repo.Setup(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<DetectedSubscription, CancellationToken>((s, _) => saved = s);

        await sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, SubscriptionKinds.Installment, null, null, 12), default);

        saved!.StartDate.Should().BeNull();
        saved.OccurrenceCount.Should().Be(5);
    }

    [Fact]
    public async Task Add_UnknownKind_Throws400()
    {
        var (sut, _) = AddHandler(Picked);

        var act = () => sut.Handle(new AddCommitmentCommand(
            UserGuid, TransactionId, "loan", null, null, null), default);

        await act.Should().ThrowAsync<InvalidCommitmentKindException>();
    }

    private static void SetStatus(DetectedSubscription row, string status)
    {
        switch (status)
        {
            case SubscriptionStatus.Dismissed: row.MarkDismissed(); break;
            case SubscriptionStatus.Completed: row.MarkCompleted(); break;
            default: row.MarkPotentiallyCancelled(); break;
        }
    }

    // --- Link a legacy row to a transaction ---

    private static (LinkCommitmentCommandHandler sut, Mock<IDetectedSubscriptionRepository> repo) LinkHandler(
        DetectedSubscription? row, CommitmentTransaction? transaction, DetectedSubscription? holder = null)
    {
        var repo = new Mock<IDetectedSubscriptionRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(row);
        repo.Setup(r => r.FindByUserAndMerchantUnscopedAsync(UserId, Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(holder);
        var reader = new Mock<ICommitmentTransactionReader>();
        reader.Setup(r => r.FindAsync(UserGuid, TransactionId, It.IsAny<CancellationToken>())).ReturnsAsync(transaction);
        return (new LinkCommitmentCommandHandler(repo.Object, reader.Object), repo);
    }

    [Fact]
    public async Task Link_LegacyRow_ReKeysItAndTakesTheTransactionAsItsLastCharge()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var (sut, repo) = LinkHandler(legacy, Picked with { ChargeCount = 3 });

        await sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId), default);

        legacy.IsTracked.Should().BeTrue();
        legacy.MerchantNameNormalized.Should().Be(Key);
        legacy.MerchantNameDisplay.Should().Be("Acme Hosting");
        legacy.LastChargeDate.Should().Be(new DateOnly(2026, 6, 22));
        legacy.NextExpectedDate.Should().Be(new DateOnly(2026, 7, 22));
        legacy.LastKnownAmount.Should().Be(16.19m);
        legacy.OccurrenceCount.Should().Be(3);
        legacy.Status.Should().Be(SubscriptionStatus.Active);
        repo.Verify(r => r.UpsertAsync(legacy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Link_PickedOlderCharge_AnchorsTheRowOnTheLatestSameKeyCharge()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var latest = new CommitmentTransaction(Key, "ACME HOSTING GMBH", 17.5m, "USD", new DateOnly(2026, 6, 22), 3);
        var (sut, _) = LinkHandler(legacy, latest);

        await sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId), default);

        legacy.LastChargeDate.Should().Be(new DateOnly(2026, 6, 22));
        legacy.NextExpectedDate.Should().Be(new DateOnly(2026, 7, 22));
        legacy.LastKnownAmount.Should().Be(17.5m);
        legacy.Currency.Should().Be("USD");
        legacy.OccurrenceCount.Should().Be(3);
    }

    [Fact]
    public async Task Link_SingleChargeHistory_UsesTheRequestedCadence()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var (sut, _) = LinkHandler(legacy, Picked);

        await sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId, SubscriptionCadences.Annual), default);

        legacy.Cadence.Should().Be(SubscriptionCadences.Annual);
        legacy.NextExpectedDate.Should().Be(new DateOnly(2027, 6, 22));
    }

    [Fact]
    public async Task Link_StoresTheSubmittedCadenceEvenWhenHistorySuggestsAnother()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var (sut, _) = LinkHandler(legacy, Picked with { ChargeCount = 2, Cadence = SubscriptionCadences.Annual });

        await sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId, SubscriptionCadences.Monthly), default);

        legacy.Cadence.Should().Be(SubscriptionCadences.Monthly);
    }

    [Fact]
    public async Task Link_UnknownCadence_Throws400()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var (sut, _) = LinkHandler(legacy, Picked);

        var act = () => sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId, "weekly"), default);

        await act.Should().ThrowAsync<InvalidCommitmentCadenceException>();
    }

    [Fact]
    public async Task Link_LegacyInstallment_KeepsItsKindTermAndStart()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21), SubscriptionKinds.Installment, termCount: 12);
        var (sut, _) = LinkHandler(legacy, Picked with { ChargeCount = 6 });

        await sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId), default);

        legacy.Kind.Should().Be(SubscriptionKinds.Installment);
        legacy.TermCount.Should().Be(12);
        legacy.StartDate.Should().Be(new DateOnly(2026, 1, 21));
        legacy.RemainingPayments.Should().Be(6);
    }

    [Fact]
    public async Task Link_ActiveRowAlreadyHoldsTheKey_Throws409NamingIt()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var (sut, repo) = LinkHandler(legacy, Picked, holder: Tracked(new DateOnly(2026, 6, 22)));

        var act = () => sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId), default);

        (await act.Should().ThrowAsync<CommitmentAlreadyTrackedException>())
            .Which.Message.Should().Contain("Acme Hosting");
        legacy.IsTracked.Should().BeFalse();
        repo.Verify(r => r.UpsertAsync(It.IsAny<DetectedSubscription>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Link_InactiveRowHoldsTheKey_SupersedesIt()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var dismissed = Tracked(new DateOnly(2026, 2, 21));
        dismissed.MarkDismissed();
        var (sut, repo) = LinkHandler(legacy, Picked, holder: dismissed);

        await sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId), default);

        repo.Verify(r => r.DeleteAsync(dismissed, It.IsAny<CancellationToken>()), Times.Once);
        legacy.MerchantNameNormalized.Should().Be(Key);
    }

    [Fact]
    public async Task Link_RowAlreadyTracked_Throws409()
    {
        var tracked = Tracked(new DateOnly(2026, 6, 22));
        var (sut, _) = LinkHandler(tracked, Picked);

        var act = () => sut.Handle(new LinkCommitmentCommand(UserGuid, tracked.Id, TransactionId), default);

        await act.Should().ThrowAsync<CommitmentAlreadyLinkedException>();
    }

    [Fact]
    public async Task Link_OtherUsersRow_Throws404()
    {
        var foreign = DetectedSubscription.CreateManual(
            Guid.NewGuid().ToString(), "Acme Hosting", 10m, "EUR", new DateOnly(2026, 1, 21), null, SubscriptionKinds.Subscription);
        var (sut, _) = LinkHandler(foreign, Picked);

        var act = () => sut.Handle(new LinkCommitmentCommand(UserGuid, foreign.Id, TransactionId), default);

        await act.Should().ThrowAsync<SubscriptionNotFoundException>();
    }

    [Fact]
    public async Task Link_TransactionNotFound_Throws404()
    {
        var legacy = Legacy(new DateOnly(2026, 1, 21));
        var (sut, _) = LinkHandler(legacy, null);

        var act = () => sut.Handle(new LinkCommitmentCommand(UserGuid, legacy.Id, TransactionId), default);

        await act.Should().ThrowAsync<CommitmentTransactionNotFoundException>();
    }
}
