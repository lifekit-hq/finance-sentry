namespace FinanceSentry.Tests.Unit.Subscriptions;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Application.Services.DetectionPhrases;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Tests.Unit.BankSync;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// 1217 end to end: a merchant's raw charges are run through the real detection algorithm on successive daily passes,
/// persisted through the real result service, and read back through the real list query. Each test is one subscription
/// seen before and after a new charge, because a change only exists against a charge the row already held. The context
/// acts for the person: detection reads past the Owner filter by itself, the list query and the assertions read through it.
/// </summary>
public class SubscriptionPriceChangeDetectionTests : IAsyncDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IAlertGeneratorService> _alerts = new();
    private readonly SubscriptionsDbContext _db = new(
        new DbContextOptionsBuilder<SubscriptionsDbContext>().UseInMemoryDatabase($"pricechange-{Guid.NewGuid():N}").Options,
        new FixedCurrentUser(UserId));

    private static SubscriptionDetectionAlgorithm.TxRow Charge(decimal amount, int month, string currency = "EUR") =>
        new(UserId, "Netflix", "Netflix.com", amount, new DateTime(2026, month, 15), null, null, currency);

    // Three months at 9.99: enough for detection to own the row.
    private static SubscriptionDetectionAlgorithm.TxRow[] Established(string currency = "EUR") =>
        [Charge(9.99m, 3, currency), Charge(9.99m, 4, currency), Charge(9.99m, 5, currency)];

    private SubscriptionDetectionResultService Service(ILogger<SubscriptionDetectionResultService>? logger = null) =>
        new(new InMemoryDetectedSubscriptionStore(_db), _alerts.Object, logger ?? NullLogger<SubscriptionDetectionResultService>.Instance);

    private async Task DetectAsync(SubscriptionDetectionResultService service, params SubscriptionDetectionAlgorithm.TxRow[] charges) =>
        await service.UpsertDetectedSubscriptionsAsync(
            UserId.ToString(), SubscriptionDetectionAlgorithm.DetectSubscriptions(DetectionPhraseSet.Seed, charges).ToList());

    private void VerifyAlert(decimal oldAmount, decimal newAmount, Times times) =>
        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            UserId, It.IsAny<Guid>(), "Netflix", oldAmount, newAmount, "EUR", It.IsAny<CancellationToken>()), times);

    private void VerifyNoAlert() =>
        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

    private Task<List<FinanceSentry.Modules.Subscriptions.Domain.SubscriptionPriceChange>> StoredChangesAsync() =>
        _db.SubscriptionPriceChanges.AsNoTracking().ToListAsync();

    private async Task<FinanceSentry.Modules.Subscriptions.API.Responses.SubscriptionDto> ListedAsync()
    {
        var handler = new GetSubscriptionsQueryHandler(new InMemoryDetectedSubscriptionStore(_db));
        var list = await handler.Handle(new GetSubscriptionsQuery(UserId.ToString(), false), default);
        return list.Items.Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task Increase_IsRecordedAndAlertedOnce_AndShownOnTheList()
    {
        var service = Service();
        await DetectAsync(service, Established());
        await DetectAsync(service, [.. Established(), Charge(12.99m, 6)]);

        var change = (await StoredChangesAsync()).Should().ContainSingle().Subject;
        change.OldAmount.Should().Be(9.99m);
        change.NewAmount.Should().Be(12.99m);
        change.ChangePercent.Should().Be(30.03m);
        change.ChargeDate.Should().Be(new DateOnly(2026, 6, 15));
        VerifyAlert(9.99m, 12.99m, Times.Once());

        var listed = await ListedAsync();
        listed.LatestPriceChange.Should().NotBeNull();
        listed.LatestPriceChange!.OldAmount.Should().Be(9.99m);
        listed.LatestPriceChange.NewAmount.Should().Be(12.99m);
        listed.LatestPriceChange.ChargeDate.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task Decrease_IsRecordedButNeverAlerts()
    {
        var service = Service();
        await DetectAsync(service, Established());
        await DetectAsync(service, [.. Established(), Charge(7.99m, 6)]);

        var change = (await StoredChangesAsync()).Should().ContainSingle().Subject;
        change.NewAmount.Should().Be(7.99m);
        change.ChangePercent.Should().BeNegative();
        VerifyNoAlert();
        (await ListedAsync()).LatestPriceChange!.NewAmount.Should().Be(7.99m);
    }

    [Fact]
    public async Task ChangeWithinTheTolerance_RecordsNothing()
    {
        var service = Service();
        await DetectAsync(service, Established());
        await DetectAsync(service, [.. Established(), Charge(10.09m, 6)]);

        (await StoredChangesAsync()).Should().BeEmpty();
        VerifyNoAlert();
        (await ListedAsync()).LatestPriceChange.Should().BeNull();
    }

    [Fact]
    public async Task FxMovedCharge_WithinTheTolerance_RecordsNothing()
    {
        var service = Service();
        await DetectAsync(service, Charge(9.20m, 3), Charge(9.20m, 4), Charge(9.20m, 5));
        await DetectAsync(service, Charge(9.20m, 3), Charge(9.20m, 4), Charge(9.20m, 5), Charge(9.55m, 6));

        (await StoredChangesAsync()).Should().BeEmpty();
        VerifyNoAlert();
    }

    [Fact]
    public async Task MeteredBill_WithinTheTolerance_RecordsNothingOnAnyMonth()
    {
        var service = Service();
        SubscriptionDetectionAlgorithm.TxRow[] metered =
            [Charge(80.00m, 3), Charge(84.50m, 4), Charge(79.00m, 5)];
        await DetectAsync(service, metered);
        await DetectAsync(service, [.. metered, Charge(86.00m, 6)]);

        (await StoredChangesAsync()).Should().BeEmpty();
        VerifyNoAlert();
        (await ListedAsync()).LatestPriceChange.Should().BeNull();
    }

    [Fact]
    public async Task RepeatChargeAtTheNewPrice_DoesNotAlertAgain()
    {
        var service = Service();
        await DetectAsync(service, Established());
        SubscriptionDetectionAlgorithm.TxRow[] raised = [.. Established(), Charge(12.99m, 6)];
        await DetectAsync(service, raised);
        await DetectAsync(service, raised);                                    // the next daily pass, nothing new
        await DetectAsync(service, [.. raised, Charge(12.99m, 7)]);            // the next month's charge, same price

        (await StoredChangesAsync()).Should().ContainSingle();
        VerifyAlert(9.99m, 12.99m, Times.Once());
    }

    [Fact]
    public async Task AFurtherIncrease_IsItsOwnChangeAndAlert_AndTheListShowsTheLatest()
    {
        // Fed as detection results rather than raw charges: with three prices inside the lookback the algorithm itself
        // reports nothing (it needs exactly two to read a repricing), so the second step is what a later pass reports
        // once the first price has aged out of the window.
        static DetectedSubscriptionData Detected(decimal amount, int month) => new(
            "netflix", "Netflix", "monthly", amount, amount, "EUR",
            new DateOnly(2026, month, 15), new DateOnly(2026, month + 1, 15), OccurrenceCount: 4, ConfidenceScore: 4, Category: null);

        var service = Service();
        await service.UpsertDetectedSubscriptionsAsync(UserId.ToString(), [Detected(9.99m, 5)]);
        await service.UpsertDetectedSubscriptionsAsync(UserId.ToString(), [Detected(12.99m, 6)]);
        await service.UpsertDetectedSubscriptionsAsync(UserId.ToString(), [Detected(14.99m, 9)]);

        (await StoredChangesAsync()).Should().HaveCount(2);
        VerifyAlert(9.99m, 12.99m, Times.Once());
        VerifyAlert(12.99m, 14.99m, Times.Once());
        (await ListedAsync()).LatestPriceChange!.NewAmount.Should().Be(14.99m);
    }

    [Fact]
    public async Task ChargeInADifferentCurrency_IsNotAPriceChange()
    {
        // The card moves from a GBP account to a EUR one: £10.99 becomes €13.49, a restated unit and not a rise.
        var service = Service();
        SubscriptionDetectionAlgorithm.TxRow[] gbp =
            [Charge(10.99m, 3, "GBP"), Charge(10.99m, 4, "GBP"), Charge(10.99m, 5, "GBP"), Charge(10.99m, 6, "GBP")];
        await DetectAsync(service, gbp);
        await DetectAsync(service, [.. gbp, Charge(13.49m, 7), Charge(13.49m, 8), Charge(13.49m, 9)]);

        var listed = await ListedAsync();
        listed.Currency.Should().Be("EUR", "the row restates its unit");
        listed.LastKnownAmount.Should().Be(13.49m);
        (await StoredChangesAsync()).Should().BeEmpty();
        listed.LatestPriceChange.Should().BeNull();
        VerifyNoAlert();
    }

    [Fact]
    public async Task ANewSubscription_HasNoEarlierChargeToCompareWith()
    {
        var service = Service();
        await DetectAsync(service, [.. Established(), Charge(12.99m, 6)]);

        (await StoredChangesAsync()).Should().BeEmpty();
        VerifyNoAlert();
    }

    [Fact]
    public async Task AFailingAlert_StillKeepsTheChange_AndDoesNotFailDetection()
    {
        _alerts.Setup(a => a.GeneratePriceHikeAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("alerts down"));
        var service = Service();
        await DetectAsync(service, Established());

        var act = () => DetectAsync(service, [.. Established(), Charge(12.99m, 6)]);

        await act.Should().NotThrowAsync();
        (await StoredChangesAsync()).Should().ContainSingle();
        (await ListedAsync()).LastKnownAmount.Should().Be(12.99m);
    }

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();
}
