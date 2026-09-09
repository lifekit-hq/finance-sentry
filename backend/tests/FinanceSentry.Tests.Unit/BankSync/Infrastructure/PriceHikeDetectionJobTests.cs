namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="PriceHikeDetectionJob"/> (044/US1).
/// Verifies the threshold gate, minimum occurrence guard, and per-subscription alert dispatch.
/// Dedup logic is the alert generator's responsibility and is not tested here.
/// </summary>
public class PriceHikeDetectionJobTests
{
    private readonly Mock<ISubscriptionHygieneSummaryReader> _reader = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static IConfiguration DefaultConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithThreshold(decimal threshold) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HygieneSentinels:PriceHikeThreshold"] = threshold.ToString("F4"),
            })
            .Build();

    private PriceHikeDetectionJob MakeJob(IConfiguration? config = null) =>
        new(_reader.Object, _alerts.Object, config ?? DefaultConfig(),
            Mock.Of<ILogger<PriceHikeDetectionJob>>());

    [Fact]
    public async Task ExecuteAsync_AlertFired_WhenLastKnownExceedsThreshold()
    {
        var userId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var sub = new SubscriptionHygieneSummary(
            subId, userId, "Netflix", AverageAmount: 10m, LastKnownAmount: 12m,
            Currency: "EUR", OccurrenceCount: 5, Kind: "subscription");

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([sub]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            userId, subId, "Netflix", 10m, 12m, "EUR", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenHikeBelowThreshold()
    {
        var sub = new SubscriptionHygieneSummary(
            Guid.NewGuid(), Guid.NewGuid(), "Spotify",
            AverageAmount: 10m, LastKnownAmount: 10.5m,
            Currency: "EUR", OccurrenceCount: 5, Kind: "subscription");

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([sub]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenOccurrenceCountBelowMinimum()
    {
        // 20% hike but only 2 occurrences — not enough history to trust the average.
        var sub = new SubscriptionHygieneSummary(
            Guid.NewGuid(), Guid.NewGuid(), "NewApp",
            AverageAmount: 10m, LastKnownAmount: 12m,
            Currency: "EUR", OccurrenceCount: 2, Kind: "subscription");

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([sub]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MeasuresAgainstPreviousAmount_WhenDetectionSawAPriceStep()
    {
        // The average covers the current price only, so a settled hike shows no rise in it;
        // the pre-step price is what the raise is actually against.
        var userId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var sub = new SubscriptionHygieneSummary(
            subId, userId, "Netflix", AverageAmount: 13.49m, LastKnownAmount: 13.49m,
            Currency: "EUR", OccurrenceCount: 6, Kind: "subscription", PreviousAmount: 10.99m);

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([sub]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            userId, subId, "Netflix", 10.99m, 13.49m, "EUR", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoAlert_WhenBaselineIsZero()
    {
        var sub = new SubscriptionHygieneSummary(
            Guid.NewGuid(), Guid.NewGuid(), "Free",
            AverageAmount: 0m, LastKnownAmount: 5m,
            Currency: "EUR", OccurrenceCount: 5, Kind: "subscription");

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([sub]);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UsesConfigurableThreshold()
    {
        // 5% hike — above a 3% threshold, below the 15% default.
        var sub = new SubscriptionHygieneSummary(
            Guid.NewGuid(), Guid.NewGuid(), "Service",
            AverageAmount: 100m, LastKnownAmount: 105m,
            Currency: "EUR", OccurrenceCount: 5, Kind: "subscription");

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([sub]);

        await MakeJob(ConfigWithThreshold(0.03m)).ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_AlertsFiredPerSubscription_WhenMultipleHike()
    {
        var subs = new[]
        {
            new SubscriptionHygieneSummary(Guid.NewGuid(), Guid.NewGuid(), "A",
                AverageAmount: 10m, LastKnownAmount: 12m, Currency: "EUR", OccurrenceCount: 5, Kind: "subscription"),
            new SubscriptionHygieneSummary(Guid.NewGuid(), Guid.NewGuid(), "B",
                AverageAmount: 20m, LastKnownAmount: 25m, Currency: "USD", OccurrenceCount: 4, Kind: "subscription"),
        };

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(subs);

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesOtherSubscriptions_WhenOneAlertThrows()
    {
        var subA = new SubscriptionHygieneSummary(Guid.NewGuid(), Guid.NewGuid(), "A",
            AverageAmount: 10m, LastKnownAmount: 12m, Currency: "EUR", OccurrenceCount: 5, Kind: "subscription");
        var subB = new SubscriptionHygieneSummary(Guid.NewGuid(), Guid.NewGuid(), "B",
            AverageAmount: 10m, LastKnownAmount: 12m, Currency: "EUR", OccurrenceCount: 5, Kind: "subscription");

        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([subA, subB]);

        var callCount = 0;
        _alerts.Setup(a => a.GeneratePriceHikeAlertAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (++callCount == 1) throw new InvalidOperationException("db failure");
                return Task.CompletedTask;
            });

        await MakeJob().ExecuteAsync();

        // Both subscriptions were attempted; second succeeded despite first throwing.
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task ExecuteAsync_NoSubscriptions_RaisesNothing()
    {
        _reader.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SubscriptionHygieneSummary>());

        await MakeJob().ExecuteAsync();

        _alerts.Verify(a => a.GeneratePriceHikeAlertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
