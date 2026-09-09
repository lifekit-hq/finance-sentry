namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.Subscriptions.Application.Services;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// End-to-end cover for 044/US1: a merchant's raw charge series is run through the real
/// detection algorithm, persisted through the real upsert service, read back through the
/// real hygiene reader, and fed to the real <see cref="PriceHikeDetectionJob"/>.
///
/// Every other test of this sentinel hands it a summary record built by hand, which hid the
/// defect these tests exist for: the detector's amount clustering and the sentinel's 15%
/// threshold were tuned against each other such that no charge series could satisfy both.
/// A hike big enough to fire the sentinel split into its own amount cluster and dropped
/// under the occurrence gate, so the subscription vanished from detection entirely; a hike
/// small enough to stay clustered was diluted by its own charge in the average it was
/// compared to. Only a test that spans both halves can tell the difference.
/// </summary>
public class PriceHikeSentinelPipelineTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static SubscriptionDetectionJob.TxRow Charge(
        decimal amount, int year, int month, int day, string currency = "EUR") =>
        new(UserId, "Netflix", "Netflix.com", amount, new DateTime(year, month, day), null, null, currency);

    /// <summary>
    /// Runs the whole US1 path: detect → persist → read → alert, and hands back what the
    /// sentinel saw so a test can assert on the baseline as well as on the alert.
    /// </summary>
    private async Task<IReadOnlyList<SubscriptionHygieneSummary>> RunPipelineAsync(
        params SubscriptionDetectionJob.TxRow[] charges)
    {
        await using var db = new SubscriptionsDbContext(
            new DbContextOptionsBuilder<SubscriptionsDbContext>()
                .UseInMemoryDatabase($"pricehike-{Guid.NewGuid():N}").Options);

        var detected = SubscriptionDetectionJob.DetectSubscriptions(charges).ToList();
        var upserts = new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(db));
        await upserts.UpsertDetectedSubscriptionsAsync(UserId.ToString(), detected);

        var reader = new SubscriptionHygieneSummaryReader(db);
        var job = new PriceHikeDetectionJob(
            reader,
            _alerts.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<ILogger<PriceHikeDetectionJob>>());

        await job.ExecuteAsync();

        return await reader.GetAllActiveAsync();
    }

    private void VerifyAlert(decimal baseline, decimal current, Times times) =>
        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            UserId, It.IsAny<Guid>(), It.IsAny<string>(),
            baseline, current, "EUR", It.IsAny<CancellationToken>()),
            times);

    private void VerifyNoAlert() =>
        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task FreshSingleStepHike_ReachesTheSentinelAndFires()
    {
        // Netflix at €10.99 for five months, then the standard-plan increase to €13.49
        // (+22.7%). Before the pre-step baseline existed, detection returned nothing at all
        // here: €13.49 is 22.7% away from €10.99, past the 15% cluster tolerance, so the
        // new price formed a cluster of one and failed the three-occurrence gate.
        var summaries = await RunPipelineAsync(
            Charge(10.99m, 2026, 3, 15),
            Charge(10.99m, 2026, 4, 15),
            Charge(10.99m, 2026, 5, 15),
            Charge(10.99m, 2026, 6, 15),
            Charge(10.99m, 2026, 7, 15),
            Charge(13.49m, 2026, 8, 15));

        VerifyAlert(baseline: 10.99m, current: 13.49m, Times.Once());
        summaries.Should().ContainSingle().Which.OccurrenceCount.Should().Be(6);
    }

    [Fact]
    public async Task DriftBelowThreshold_StaysQuiet()
    {
        // A €0.50 rounding drift (+4.5%) stays inside one amount cluster, so there is no
        // step and no baseline — the sentinel must swallow it.
        await RunPipelineAsync(
            Charge(10.99m, 2026, 3, 15),
            Charge(10.99m, 2026, 4, 15),
            Charge(10.99m, 2026, 5, 15),
            Charge(11.49m, 2026, 6, 15));

        VerifyNoAlert();
    }

    [Fact]
    public async Task PriceCut_IsRecordedButNeverAlerts()
    {
        // A downgrade steps the amount the same way a hike does, so it must survive
        // clustering and record its baseline — but a cheaper charge is not a hike.
        var summaries = await RunPipelineAsync(
            Charge(13.49m, 2026, 4, 15),
            Charge(13.49m, 2026, 5, 15),
            Charge(13.49m, 2026, 6, 15),
            Charge(13.49m, 2026, 7, 15),
            Charge(10.99m, 2026, 8, 15));

        var summary = summaries.Should().ContainSingle().Subject;
        summary.PreviousAmount.Should().Be(13.49m);
        summary.LastKnownAmount.Should().Be(10.99m);
        VerifyNoAlert();
    }

    [Fact]
    public async Task SettledNewPrice_StopsAlerting()
    {
        // Three charges into the new price it is no longer news: the current cluster stands
        // on its own occurrences, the stale baseline clears, and the sentinel goes quiet
        // instead of re-raising the same hike every month for the rest of the lookback.
        await RunPipelineAsync(
            Charge(10.99m, 2026, 3, 15),
            Charge(10.99m, 2026, 4, 15),
            Charge(10.99m, 2026, 5, 15),
            Charge(13.49m, 2026, 6, 15),
            Charge(13.49m, 2026, 7, 15),
            Charge(13.49m, 2026, 8, 15));

        VerifyNoAlert();
    }

    [Fact]
    public async Task CurrencyChange_IsNotMistakenForAHike()
    {
        // The user's card moves from a GBP account to a EUR one mid-series. The same
        // subscription is now billed €13.49 where it was billed £10.99 — a 22.7% step
        // against the old number that is not a rise in price at all, only a change of unit.
        // Detection groups by merchant alone, so both currencies land in one group; nothing
        // downstream of that group can tell the two apart.
        var summaries = await RunPipelineAsync(
            Charge(10.99m, 2026, 3, 15, "GBP"),
            Charge(10.99m, 2026, 4, 15, "GBP"),
            Charge(10.99m, 2026, 5, 15, "GBP"),
            Charge(10.99m, 2026, 6, 15, "GBP"),
            Charge(10.99m, 2026, 7, 15, "GBP"),
            Charge(13.49m, 2026, 8, 15, "EUR"));

        VerifyNoAlert();

        // And the subscription is not collateral damage: one euro charge is not yet a billing
        // arrangement, so the row keeps being reported in the pounds it is still billed in.
        var summary = summaries.Should().ContainSingle().Subject;
        summary.Currency.Should().Be("GBP");
        summary.OccurrenceCount.Should().Be(5);
        summary.LastKnownAmount.Should().Be(10.99m);
    }

    [Fact]
    public async Task HikeAfterAMoveToANewCurrency_FiresInTheNewUnit()
    {
        // £9.29 and €10.99 are the same real price, so once billing has moved the merchant
        // has three amount clusters — the one shape SplitAtPriceStep refuses outright, which
        // left the genuine €10.99 → €13.49 rise with no series to be seen against at all.
        var summaries = await RunPipelineAsync(
            Charge(9.29m, 2026, 3, 15, "GBP"),
            Charge(9.29m, 2026, 4, 15, "GBP"),
            Charge(10.99m, 2026, 5, 15),
            Charge(10.99m, 2026, 6, 15),
            Charge(13.49m, 2026, 7, 15));

        VerifyAlert(baseline: 10.99m, current: 13.49m, Times.Once());

        var summary = summaries.Should().ContainSingle().Subject;
        summary.Currency.Should().Be("EUR");
        summary.OccurrenceCount.Should().Be(3);
    }

    [Fact]
    public async Task CurrencyChange_RestatesTheStoredUnitOnTheExistingRow()
    {
        // Detection re-runs daily onto the row it already wrote. If the update path could not
        // restate Currency, the row would keep saying GBP while its amounts turned into euros
        // — and the spend summaries run ToUsd over that field.
        await using var db = new SubscriptionsDbContext(
            new DbContextOptionsBuilder<SubscriptionsDbContext>()
                .UseInMemoryDatabase($"pricehike-{Guid.NewGuid():N}").Options);

        var upserts = new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(db));

        SubscriptionDetectionJob.TxRow[] beforeMove =
        [
            Charge(10.99m, 2026, 3, 15, "GBP"),
            Charge(10.99m, 2026, 4, 15, "GBP"),
            Charge(10.99m, 2026, 5, 15, "GBP"),
        ];
        await upserts.UpsertDetectedSubscriptionsAsync(
            UserId.ToString(), SubscriptionDetectionJob.DetectSubscriptions(beforeMove).ToList());

        SubscriptionDetectionJob.TxRow[] afterMove =
        [
            .. beforeMove,
            Charge(13.49m, 2026, 6, 15), Charge(13.49m, 2026, 7, 15), Charge(13.49m, 2026, 8, 15),
        ];
        await upserts.UpsertDetectedSubscriptionsAsync(
            UserId.ToString(), SubscriptionDetectionJob.DetectSubscriptions(afterMove).ToList());

        var summary = (await new SubscriptionHygieneSummaryReader(db).GetAllActiveAsync())
            .Should().ContainSingle().Subject;

        summary.Currency.Should().Be("EUR");
        summary.LastKnownAmount.Should().Be(13.49m);
    }

    [Fact]
    public async Task FlatSubscription_NeverAlerts()
    {
        await RunPipelineAsync(
            Charge(10.99m, 2026, 5, 15),
            Charge(10.99m, 2026, 6, 15),
            Charge(10.99m, 2026, 7, 15),
            Charge(10.99m, 2026, 8, 15));

        VerifyNoAlert();
    }

    [Fact]
    public async Task SecondRun_OverTheSameSeries_ReportsTheSameBaseline()
    {
        // Detection re-runs daily over the same window and upserts onto the existing row;
        // the baseline must survive that update path, not only the initial insert.
        var charges = new[]
        {
            Charge(10.99m, 2026, 3, 15),
            Charge(10.99m, 2026, 4, 15),
            Charge(10.99m, 2026, 5, 15),
            Charge(10.99m, 2026, 6, 15),
            Charge(10.99m, 2026, 7, 15),
            Charge(13.49m, 2026, 8, 15),
        };

        await using var db = new SubscriptionsDbContext(
            new DbContextOptionsBuilder<SubscriptionsDbContext>()
                .UseInMemoryDatabase($"pricehike-{Guid.NewGuid():N}").Options);

        var upserts = new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(db));
        var detected = SubscriptionDetectionJob.DetectSubscriptions(charges).ToList();
        await upserts.UpsertDetectedSubscriptionsAsync(UserId.ToString(), detected);
        await upserts.UpsertDetectedSubscriptionsAsync(UserId.ToString(), detected);

        var summary = (await new SubscriptionHygieneSummaryReader(db).GetAllActiveAsync())
            .Should().ContainSingle().Subject;

        summary.PreviousAmount.Should().Be(10.99m);
        summary.LastKnownAmount.Should().Be(13.49m);
        summary.HikeBaseline.Should().Be(10.99m);
    }

    [Fact]
    public async Task StaleBaseline_IsClearedOnTheNextRun()
    {
        // The row is first written mid-hike, then re-detected once the price has settled.
        // If the update path merged instead of overwriting, the sentinel would keep firing
        // against a baseline detection no longer reports.
        await using var db = new SubscriptionsDbContext(
            new DbContextOptionsBuilder<SubscriptionsDbContext>()
                .UseInMemoryDatabase($"pricehike-{Guid.NewGuid():N}").Options);

        var upserts = new SubscriptionDetectionResultService(new DetectedSubscriptionRepository(db));

        SubscriptionDetectionJob.TxRow[] midHike =
        [
            Charge(10.99m, 2026, 3, 15), Charge(10.99m, 2026, 4, 15),
            Charge(10.99m, 2026, 5, 15), Charge(13.49m, 2026, 6, 15),
        ];
        await upserts.UpsertDetectedSubscriptionsAsync(
            UserId.ToString(), SubscriptionDetectionJob.DetectSubscriptions(midHike).ToList());

        SubscriptionDetectionJob.TxRow[] settled =
        [
            .. midHike, Charge(13.49m, 2026, 7, 15), Charge(13.49m, 2026, 8, 15),
        ];
        await upserts.UpsertDetectedSubscriptionsAsync(
            UserId.ToString(), SubscriptionDetectionJob.DetectSubscriptions(settled).ToList());

        var summary = (await new SubscriptionHygieneSummaryReader(db).GetAllActiveAsync())
            .Should().ContainSingle().Subject;

        summary.PreviousAmount.Should().BeNull();
        summary.HikeBaseline.Should().Be(13.49m);
    }
}
