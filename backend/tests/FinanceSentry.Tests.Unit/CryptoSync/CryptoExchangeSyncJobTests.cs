using System.Net;
using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Binance;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Jobs;
using FinanceSentry.Modules.CryptoSync.Infrastructure.RevolutX;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Unit.CryptoSync;

public class CryptoExchangeSyncJobTests
{
    private readonly Mock<IExchangeCredentialRepository> _credentialRepo = new(MockBehavior.Loose);
    private readonly Mock<ICommandHandler<SyncExchangeHoldingsCommand, SyncExchangeHoldingsResult>> _syncHandler = new(MockBehavior.Loose);
    private readonly Mock<IAlertGeneratorService> _alerts = new(MockBehavior.Loose);
    private readonly Mock<IUserAlertPreferencesReader> _userPrefs = new(MockBehavior.Loose);
    private readonly IJobFailureStreakStore _failureStreaks = new InMemoryJobFailureStreakStore();
    private readonly Mock<IConnectionHealthShadow> _connectionHealth = new(MockBehavior.Loose);
    private readonly FakeTimeProvider _time = new(Start);

    // Both venues stay in shadow in the committed appsettings; the policy tests opt one in.
    private readonly ConnectionHealthOptions _options = HealthOptions.With();

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(15);

    private BinanceSyncJob CreateBinanceJob(IConnectionHealthShadow? shadow = null) =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks,
            shadow ?? _connectionHealth.Object, new BinanceFailureClassifier(), HealthOptions.Monitor(_options), _time,
            NullLogger<BinanceSyncJob>.Instance);

    private RevolutXSyncJob CreateRevolutXJob(IConnectionHealthShadow? shadow = null) =>
        new(_credentialRepo.Object, _syncHandler.Object, _alerts.Object, _userPrefs.Object, _failureStreaks,
            shadow ?? _connectionHealth.Object, new RevolutXFailureClassifier(), HealthOptions.Monitor(_options), _time,
            NullLogger<RevolutXSyncJob>.Instance);

    private static ExchangeCredential MakeCredential(Guid userId, string provider = CryptoExchangeProvider.Binance) =>
        ExchangeCredential.Create(userId, provider, [1], [2], [3], [4], [5], [6], 1);

    private void GivenActive(string provider, params ExchangeCredential[] credentials) =>
        _credentialRepo
            .Setup(r => r.GetAllActiveUnscopedAsync(provider, It.IsAny<CancellationToken>()))
            .ReturnsAsync(credentials);

    [Fact]
    public async Task ExecuteAsync_IteratesAllActiveCredentials()
    {
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(Guid.NewGuid()), MakeCredential(Guid.NewGuid()));

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncExchangeHoldingsResult(0, DateTime.UtcNow));

        await CreateBinanceJob().ExecuteAsync();

        _syncHandler.Verify(
            h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public async Task ExecuteAsync_EachJobSyncsOnlyItsOwnVenue(string provider)
    {
        var userId = Guid.NewGuid();
        GivenActive(provider, MakeCredential(userId, provider));

        SyncExchangeHoldingsCommand? captured = null;
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .Callback<SyncExchangeHoldingsCommand, CancellationToken>((cmd, _) => captured = cmd)
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow));

        if (provider == CryptoExchangeProvider.Binance)
            await CreateBinanceJob().ExecuteAsync();
        else
            await CreateRevolutXJob().ExecuteAsync();

        captured.Should().Be(new SyncExchangeHoldingsCommand(userId, provider));
        _credentialRepo.Verify(
            r => r.GetAllActiveUnscopedAsync(It.Is<string>(p => p != provider), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_OneCredentialFails_OtherCredentialsStillSynced_AndTheRunSucceeds()
    {
        var failingUser = Guid.NewGuid();
        var successUser = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(failingUser), MakeCredential(successUser));

        _syncHandler
            .Setup(h => h.Handle(
                It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == failingUser),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated sync failure"));

        _syncHandler
            .Setup(h => h.Handle(
                It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == successUser),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncExchangeHoldingsResult(2, DateTime.UtcNow));

        await CreateBinanceJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();

        _syncHandler.Verify(
            h => h.Handle(
                It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == successUser),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EveryUserFails_FailsTheJob_SoTheFailureStreakGaugeCanCount()
    {
        // A swallowed total failure is a Hangfire success, and ConsecutiveFailureGaugeFilter
        // (the Grafana alert's gauge) only ever sees failed runs.
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));

        var rejected = new RevolutXException(
            "Revolut X API error (HTTP 401): Unauthorized",
            new HttpRequestException("Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(rejected);

        var thrown = await CreateRevolutXJob().Invoking(j => j.ExecuteAsync())
            .Should().ThrowAsync<AggregateException>();

        thrown.Which.InnerExceptions.Should().ContainSingle().Which.Should().BeSameAs(rejected);
        JobFailureTransientClassifier.IsTransient(thrown.Which).Should().BeFalse(
            "a rejected key is sticky and must count toward the alert streak");
    }

    [Fact]
    public async Task ExecuteAsync_VenueOutage_FailsTheJob_ButClassifiesAsTransient()
    {
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(Guid.NewGuid(), CryptoExchangeProvider.RevolutX));

        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException(
                "Revolut X API error (HTTP 503): down",
                new HttpRequestException("down", null, System.Net.HttpStatusCode.ServiceUnavailable)));

        var thrown = await CreateRevolutXJob().Invoking(j => j.ExecuteAsync())
            .Should().ThrowAsync<AggregateException>();

        JobFailureTransientClassifier.IsTransient(thrown.Which).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_NoConnectedUsers_Succeeds()
    {
        GivenActive(CryptoExchangeProvider.RevolutX);

        await CreateRevolutXJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Failure_RaisesTheInAppSyncFailureAlertUnderTheVenueSlug()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        _userPrefs
            .Setup(p => p.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RevolutXException(
                "Revolut X rejected the key.",
                new HttpRequestException("401", inner: null, HttpStatusCode.Unauthorized)));

        await CreateRevolutXJob().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<AggregateException>();

        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, CryptoExchangeProvider.RevolutX, null, null, nameof(RevolutXException),
                SyncFailureClass.Credential, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GivenAlertsOn(Guid userId) =>
        _userPrefs
            .Setup(p => p.GetAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));

    private void GivenSyncFails(Exception ex) =>
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(ex);

    private void VerifyAlerts(Guid userId, string provider, SyncFailureClass failureClass, Times times) =>
        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, provider, null, null, It.IsAny<string?>(), failureClass, It.IsAny<CancellationToken>()),
            times);

    private void VerifyAnyAlerts(Guid userId, string provider, Times times) =>
        _alerts.Verify(
            a => a.GenerateSyncFailureAlertAsync(
                userId, provider, null, null, It.IsAny<string?>(), It.IsAny<SyncFailureClass>(), It.IsAny<CancellationToken>()),
            times);

    // The streak gate: a transient failure alerts only on the 3rd consecutive tick; an all-users failure
    // throws an AggregateException every tick, which the job-failure filter handles separately.
    private static async Task RunTicks(Func<Task> tick, int count)
    {
        for (var i = 0; i < count; i++)
        {
            try { await tick(); }
            catch (AggregateException) { }
        }
    }

    [Fact]
    public async Task ExecuteAsync_BinanceHttp500_IsTransient_AlertsOnlyAtThirdConsecutiveTick_WithOutageClass()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 2);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);
        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Outage, Times.Once());

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 2);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_BinanceTradeHistory429_IsTransient_NoAlertOnFirstTick()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new CryptoTradeHistoryException(
            CryptoExchangeProvider.Binance, ["BTC"], new BinanceException("HTTP 429", venueStatusCode: 429)));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_BinanceTradeHistoryRejectedKey_AlertsImmediatelyWithCredentialClass()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new CryptoTradeHistoryException(
            CryptoExchangeProvider.Binance, ["BTC"], new BinanceException("Invalid API-key", binanceErrorCode: -2015, venueStatusCode: 401)));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);

        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Credential, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_RevolutXUnreachable_IsTransient_NoAlertOnFirstTick()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException(
            "Revolut X is unreachable.", new HttpRequestException("Connection refused", new System.Net.Sockets.SocketException())));

        await RunTicks(() => CreateRevolutXJob().ExecuteAsync(), 1);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.RevolutX, Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_RevolutXTimeout_IsTransient_NoAlertOnFirstTick()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException("Revolut X did not respond in time.", new TaskCanceledException("timeout")));

        await RunTicks(() => CreateRevolutXJob().ExecuteAsync(), 1);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.RevolutX, Times.Never());
    }

    [Fact]
    public async Task ExecuteAsync_BinanceRejectedKey_AlertsImmediatelyWithReconnectClass()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Invalid API-key, IP, or permissions for action.", binanceErrorCode: -2015, venueStatusCode: 401));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);

        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Credential, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_RevolutXLocalFailure_AlertsImmediatelyWithUnknownClassNotReconnect()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException("Revolut X returned an unreadable response for /balances."));

        await RunTicks(() => CreateRevolutXJob().ExecuteAsync(), 1);

        VerifyAlerts(userId, CryptoExchangeProvider.RevolutX, SyncFailureClass.Unknown, Times.Once());
    }

    [Theory]
    [InlineData(-1021, 400)]
    [InlineData(null, 418)]
    public async Task ExecuteAsync_BinanceNonCredentialFailure_AlertsImmediatelyWithUnknownClassNotReconnect(
        int? binanceCode, int status)
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Binance refused the call.", binanceErrorCode: binanceCode, venueStatusCode: status));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);

        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Unknown, Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_SuccessBetweenTransientFailures_ResetsTheStreak()
    {
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        _syncHandler
            .SetupSequence(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500))
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500))
            .ThrowsAsync(new BinanceException("HTTP 500", venueStatusCode: 500));

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 5);

        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());
    }

    // ── Connection health, shadow mode (Option B, S1) ───────────────────────

    public static TheoryData<Exception, FailureClass, FailureStrength> ShadowFailureCases => new()
    {
        { new BinanceException("Invalid API-key", binanceErrorCode: -2015, venueStatusCode: 401), FailureClass.Credential, FailureStrength.Definitive },
        { new BinanceException("Unauthorized", venueStatusCode: 401), FailureClass.Credential, FailureStrength.Suspect },
        { new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500), FailureClass.Transient, FailureStrength.Definitive },
        { new CryptoTradeHistoryException(CryptoExchangeProvider.Binance, ["BTC"], new BinanceException("HTTP 429", venueStatusCode: 429)), FailureClass.RateLimited, FailureStrength.Definitive },
        { new BinanceException("Binance refused the call.", binanceErrorCode: -1021, venueStatusCode: 400), FailureClass.Unknown, FailureStrength.Definitive },
        { new ExchangeCredentialUnreadableException(CryptoExchangeProvider.Binance, new System.Security.Cryptography.CryptographicException("tag")), FailureClass.Credential, FailureStrength.Definitive },
        { new InvalidOperationException("boom"), FailureClass.Unknown, FailureStrength.Definitive },
    };

    [Theory]
    [MemberData(nameof(ShadowFailureCases))]
    public async Task ExecuteAsync_Failure_RecordsTheClassifiersReadingWithTheShadow(
        Exception ex, FailureClass expectedClass, FailureStrength expectedStrength)
    {
        var credential = MakeCredential(Guid.NewGuid());
        GivenActive(CryptoExchangeProvider.Binance, credential);
        GivenSyncFails(ex);

        await RunTicks(() => CreateBinanceJob().ExecuteAsync(), 1);

        _connectionHealth.Verify(s => s.RecordFailureAsync(
            It.Is<ConnectionHealthSubject>(subject =>
                subject.Id == credential.Id && subject.Provider == CryptoExchangeProvider.Binance && subject.Kind == nameof(ExchangeCredential)),
            It.IsAny<ConnectionHealth>(),
            It.Is<ProviderFailure>(f => f.Class == expectedClass && f.Strength == expectedStrength),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Success_RecordsSuccessWithTheShadow()
    {
        var credential = MakeCredential(Guid.NewGuid(), CryptoExchangeProvider.RevolutX);
        GivenActive(CryptoExchangeProvider.RevolutX, credential);

        await CreateRevolutXJob().ExecuteAsync();

        _connectionHealth.Verify(s => s.RecordSuccessAsync(
            It.Is<ConnectionHealthSubject>(subject => subject.Id == credential.Id && subject.Provider == CryptoExchangeProvider.RevolutX),
            It.IsAny<ConnectionHealth>(),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WithTheRealShadow_AlertsExactlyAsBefore_WhileRecordingTheWouldBeState()
    {
        // Three quick 500s: today's streak gate alerts at the third tick; the policy, needing 2 h without
        // success as well, would still hold the connection Degraded. That gap is what shadow mode measures.
        var userId = Guid.NewGuid();
        var credential = MakeCredential(userId);
        GivenActive(CryptoExchangeProvider.Binance, credential);
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500));
        var shadow = ShadowRecorders.Create(_time, _options);
        BinanceSyncJob CreateJobWithShadow() => CreateBinanceJob(shadow);

        await RunTicks(() => CreateJobWithShadow().ExecuteAsync(), 2);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());

        await RunTicks(() => CreateJobWithShadow().ExecuteAsync(), 1);
        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Outage, Times.Once());

        credential.Health.State.Should().Be(ConnectionHealthState.Degraded);
        credential.Health.ConsecutiveFailures.Should().Be(3);
        _credentialRepo.Verify(r => r.SaveHealthUnscopedAsync(credential.Id, It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    // ── Active: the policy drives the alert (Option B, S3) ───────────────────

    /// <summary>Puts <paramref name="provider"/> in ActiveProviders.</summary>
    private void GivenPolicyDrives(string provider) => _options.ActiveProviders.Add(provider);

    /// <summary>Runs <paramref name="count"/> 15-minute ticks of the venue's job with the real policy, the first one now.</summary>
    private async Task RunPolicyTicksAsync(string provider, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) _time.Advance(Tick);
            var shadow = ShadowRecorders.Create(_time, _options);
            if (provider == CryptoExchangeProvider.Binance)
                await CreateBinanceJob(shadow).ExecuteAsync();
            else
                await CreateRevolutXJob(shadow).ExecuteAsync();
        }
    }

    private void VerifyEscalated(Guid userId, string provider, Times times) =>
        _alerts.Verify(
            a => a.EscalateSyncFailureAlertAsync(userId, provider, null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            times);

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public async Task SwitchingAVenueInActiveProviders_FlipsWhoRaisesTheAlert(string provider)
    {
        var userId = Guid.NewGuid();
        GivenActive(provider, MakeCredential(userId, provider));
        GivenAlertsOn(userId);
        GivenSyncFails(provider == CryptoExchangeProvider.Binance
            ? new BinanceException("Binance refused the call.", binanceErrorCode: -1021, venueStatusCode: 400)
            : new RevolutXException("Revolut X API error (HTTP 400): bad", new HttpRequestException("bad", null, HttpStatusCode.BadRequest)));

        // Shadow: the legacy streak alerts a non-transient failure at once, and the all-failed run throws.
        await RunTicks(() => RunPolicyTicksAsync(provider, 1), 1);
        VerifyAlerts(userId, provider, SyncFailureClass.Unknown, Times.Once());

        // Active: the policy holds an unknown failure until it has persisted, and the run succeeds.
        GivenPolicyDrives(provider);
        _alerts.Invocations.Clear();
        _time.Advance(Tick);
        await FluentActions.Invoking(() => RunPolicyTicksAsync(provider, 1)).Should().NotThrowAsync();
        VerifyAnyAlerts(userId, provider, Times.Never());
    }

    [Fact]
    public async Task Active_EveryUserFails_TheRunStillSucceeds_SoTheAlertIsTheOnlySignal()
    {
        GivenPolicyDrives(CryptoExchangeProvider.RevolutX);
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(Guid.NewGuid(), CryptoExchangeProvider.RevolutX));
        GivenSyncFails(new RevolutXException(
            "Revolut X API error (HTTP 401): Unauthorized",
            new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized)));

        await CreateRevolutXJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Active_BinanceRejectedKey_EscalatesAtOnce_UnderTheVenueSlug()
    {
        GivenPolicyDrives(CryptoExchangeProvider.Binance);
        var userId = Guid.NewGuid();
        var credential = MakeCredential(userId);
        GivenActive(CryptoExchangeProvider.Binance, credential);
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Invalid API-key, IP, or permissions for action.", binanceErrorCode: -2015, venueStatusCode: 401));

        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 1);

        _alerts.Verify(
            a => a.EscalateSyncFailureAlertAsync(
                userId, CryptoExchangeProvider.Binance, null, null, nameof(BinanceException), It.IsAny<CancellationToken>()),
            Times.Once);
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);

        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 3);
        VerifyEscalated(userId, CryptoExchangeProvider.Binance, Times.Once());
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());
    }

    [Fact]
    public async Task Active_BinanceHttp500_NoAlertBeforeThreeFailuresAndTwoHours_ThenOneOutageAlert()
    {
        GivenPolicyDrives(CryptoExchangeProvider.Binance);
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.Binance, MakeCredential(userId));
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500));

        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 8);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Never());

        _time.Advance(Tick);
        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 1);
        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Outage, Times.Once());

        _time.Advance(Tick);
        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 3);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.Binance, Times.Once());
    }

    [Fact]
    public async Task Active_RevolutX401_SuspectThenConfirmed_EscalatesThirtyMinutesLater()
    {
        GivenPolicyDrives(CryptoExchangeProvider.RevolutX);
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException(
            "Revolut X rejected the key.", new HttpRequestException("401", inner: null, HttpStatusCode.Unauthorized)));

        await RunPolicyTicksAsync(CryptoExchangeProvider.RevolutX, 2);
        VerifyEscalated(userId, CryptoExchangeProvider.RevolutX, Times.Never());

        _time.Advance(Tick);
        await RunPolicyTicksAsync(CryptoExchangeProvider.RevolutX, 1);
        VerifyEscalated(userId, CryptoExchangeProvider.RevolutX, Times.Once());
    }

    [Fact]
    public async Task Active_TransientThenRevoked_TheOutageAlertGivesWayToTheReconnectAlert()
    {
        GivenPolicyDrives(CryptoExchangeProvider.Binance);
        var userId = Guid.NewGuid();
        var credential = MakeCredential(userId);
        GivenActive(CryptoExchangeProvider.Binance, credential);
        GivenAlertsOn(userId);
        GivenSyncFails(new BinanceException("Binance API error: HTTP 500", venueStatusCode: 500));
        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 9);
        VerifyAlerts(userId, CryptoExchangeProvider.Binance, SyncFailureClass.Outage, Times.Once());

        GivenSyncFails(new BinanceException("Invalid API-key", binanceErrorCode: -2015, venueStatusCode: 401));
        _time.Advance(Tick);
        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 1);

        VerifyEscalated(userId, CryptoExchangeProvider.Binance, Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
    }

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public async Task Active_UndecryptableStoredKey_EscalatesAtOnce(string provider)
    {
        GivenPolicyDrives(provider);
        var userId = Guid.NewGuid();
        GivenActive(provider, MakeCredential(userId, provider));
        GivenAlertsOn(userId);
        GivenSyncFails(new ExchangeCredentialUnreadableException(provider, new System.Security.Cryptography.CryptographicException("tag")));

        await RunPolicyTicksAsync(provider, 1);

        VerifyEscalated(userId, provider, Times.Once());
    }

    [Fact]
    public async Task Active_RevolutXUnreadableResponse_CountsAsUnknown_AndAlertsOnceItPersists()
    {
        GivenPolicyDrives(CryptoExchangeProvider.RevolutX);
        var userId = Guid.NewGuid();
        GivenActive(CryptoExchangeProvider.RevolutX, MakeCredential(userId, CryptoExchangeProvider.RevolutX));
        GivenAlertsOn(userId);
        GivenSyncFails(new RevolutXException(
            "Revolut X returned an unreadable response for /balances.", new System.Text.Json.JsonException("bad")));

        await RunPolicyTicksAsync(CryptoExchangeProvider.RevolutX, 8);
        VerifyAnyAlerts(userId, CryptoExchangeProvider.RevolutX, Times.Never());

        _time.Advance(Tick);
        await RunPolicyTicksAsync(CryptoExchangeProvider.RevolutX, 1);
        VerifyAlerts(userId, CryptoExchangeProvider.RevolutX, SyncFailureClass.Unknown, Times.Once());
    }

    [Fact]
    public async Task Active_SuccessAfterTheAlert_ResolvesIt()
    {
        GivenPolicyDrives(CryptoExchangeProvider.Binance);
        var userId = Guid.NewGuid();
        var credential = MakeCredential(userId);
        GivenActive(CryptoExchangeProvider.Binance, credential);
        GivenAlertsOn(userId);
        _syncHandler
            .SetupSequence(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BinanceException("Invalid API-key", binanceErrorCode: -2015, venueStatusCode: 401))
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow))
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow));

        // The rejected key is a definitive loss: the alert goes out, and the following ticks leave the key alone.
        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 3);
        VerifyEscalated(userId, CryptoExchangeProvider.Binance, Times.Once());
        VerifySyncCalls(userId, Times.Once());

        // The daily probe succeeds and resolves the alert.
        _time.Advance(TimeSpan.FromDays(1));
        await RunPolicyTicksAsync(CryptoExchangeProvider.Binance, 1);

        _alerts.Verify(
            a => a.ResolveSyncFailureAlertAsync(userId, CryptoExchangeProvider.Binance, null, It.IsAny<CancellationToken>()),
            Times.Once);
        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
    }

    // ── A confirmed-lost credential is not re-hit every tick ─────────────────

    private static ConnectionHealth ActionRequiredSince(DateTimeOffset lastFailureAt) =>
        new()
        {
            State = ConnectionHealthState.ActionRequired,
            ConsecutiveFailures = 3,
            FirstFailureAt = lastFailureAt.AddHours(-1),
            LastFailureAt = lastFailureAt,
            LastFailureClass = FailureClass.Credential,
            LastFailureCode = "HTTP_401",
        };

    private static ExchangeCredential MakeLostCredential(string provider, DateTimeOffset lastFailureAt)
    {
        var credential = MakeCredential(Guid.NewGuid(), provider);
        credential.ApplyHealth(ActionRequiredSince(lastFailureAt));
        return credential;
    }

    private async Task RunJobAsync(string provider)
    {
        var shadow = ShadowRecorders.Create(_time, _options);
        if (provider == CryptoExchangeProvider.Binance)
            await CreateBinanceJob(shadow).ExecuteAsync();
        else
            await CreateRevolutXJob(shadow).ExecuteAsync();
    }

    private void VerifySyncCalls(Guid userId, Times times) =>
        _syncHandler.Verify(
            h => h.Handle(It.Is<SyncExchangeHoldingsCommand>(c => c.UserId == userId), It.IsAny<CancellationToken>()), times);

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance, false)]
    [InlineData(CryptoExchangeProvider.Binance, true)]
    [InlineData(CryptoExchangeProvider.RevolutX, false)]
    [InlineData(CryptoExchangeProvider.RevolutX, true)]
    public async Task ActionRequired_ScheduledRun_SkipsTheCredential_InBothModes(string provider, bool active)
    {
        if (active) _options.ActiveProviders.Add(provider);
        var lost = MakeLostCredential(provider, Start.AddHours(-1));
        GivenActive(provider, lost);
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("must not be called"));

        await RunJobAsync(provider);

        VerifySyncCalls(lost.UserId, Times.Never());
        _credentialRepo.Verify(
            r => r.SaveHealthUnscopedAsync(It.IsAny<Guid>(), It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ActionRequired_ASkippedCredential_NeitherFailsNorExcusesTheRun()
    {
        var lost = MakeLostCredential(CryptoExchangeProvider.Binance, Start.AddHours(-1));
        var failing = MakeCredential(Guid.NewGuid());
        GivenActive(CryptoExchangeProvider.Binance, lost, failing);
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated sync failure"));

        // The one credential the job did try failed: that is every attempt of the run, so the run fails.
        var thrown = await CreateBinanceJob().Invoking(j => j.ExecuteAsync()).Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().ContainSingle();
        VerifySyncCalls(lost.UserId, Times.Never());
    }

    [Fact]
    public async Task ActionRequired_EveryCredentialSkipped_TheRunSucceeds()
    {
        var lost = MakeLostCredential(CryptoExchangeProvider.RevolutX, Start.AddHours(-1));
        GivenActive(CryptoExchangeProvider.RevolutX, lost);

        await CreateRevolutXJob().Invoking(j => j.ExecuteAsync()).Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public async Task ActionRequired_ProbesOncePerDay_AndASuccessfulProbeClearsItThroughThePolicy(string provider)
    {
        _options.ActiveProviders.Add(provider);
        var lost = MakeLostCredential(provider, Start.AddDays(-1));
        GivenActive(provider, lost);
        _userPrefs
            .Setup(p => p.GetAsync(lost.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(true, 0m, true));
        var rejected = new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
        _syncHandler
            .SetupSequence(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(rejected)
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow));

        // Due: the probe runs, fails, and renews the pause from its own time.
        await RunJobAsync(provider);
        VerifySyncCalls(lost.UserId, Times.Once());
        lost.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
        lost.Health.LastFailureAt.Should().Be(Start);

        // The rest of that day: nothing.
        for (var i = 0; i < 95; i++)
        {
            _time.Advance(Tick);
            await RunJobAsync(provider);
        }
        VerifySyncCalls(lost.UserId, Times.Once());

        // A day after the probe: it runs again, succeeds and clears ActionRequired.
        _time.Advance(Tick);
        await RunJobAsync(provider);
        VerifySyncCalls(lost.UserId, Times.Exactly(2));
        lost.Health.State.Should().Be(ConnectionHealthState.Healthy);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(lost.UserId, provider, null, It.IsAny<CancellationToken>()), Times.Once);

        // Healthy: back to every tick.
        _time.Advance(Tick);
        await RunJobAsync(provider);
        VerifySyncCalls(lost.UserId, Times.Exactly(3));
    }

    [Theory]
    [InlineData(CryptoExchangeProvider.Binance)]
    [InlineData(CryptoExchangeProvider.RevolutX)]
    public async Task ActionRequired_AfterReconnect_TheNextScheduledRunSyncs(string provider)
    {
        _options.ActiveProviders.Add(provider);
        var credential = MakeLostCredential(provider, Start.AddHours(-1));
        credential.Reconnect([7], [8], [9], [10], [11], [12], 1);
        GivenActive(provider, credential);
        _userPrefs
            .Setup(p => p.GetAsync(credential.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAlertPreferences(true, 0m, true));
        _syncHandler
            .Setup(h => h.Handle(It.IsAny<SyncExchangeHoldingsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncExchangeHoldingsResult(1, DateTime.UtcNow));
        credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);

        await RunJobAsync(provider);

        VerifySyncCalls(credential.UserId, Times.Once());
        credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        credential.Health.LastSuccessAt.Should().Be(Start);
        _alerts.Verify(a => a.ResolveSyncFailureAlertAsync(credential.UserId, provider, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
