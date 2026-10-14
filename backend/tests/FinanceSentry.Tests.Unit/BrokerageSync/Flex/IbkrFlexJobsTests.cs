namespace FinanceSentry.Tests.Unit.BrokerageSync.Flex;

using FinanceSentry.Core.Connections;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Application.Services;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.Flex;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Jobs;
using FinanceSentry.Tests.Unit.Connections;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

/// <summary>
/// fs-435 S5 PR2 — both the daily incremental sync job and the one-shot backfill job
/// must no-op cleanly (no sync call, no exception) when no user has an active Flex
/// credential, since supplying one is a manual step the human has not necessarily taken.
/// </summary>
public class IbkrFlexJobsTests
{
    [Fact]
    public async Task IncrementalSyncJob_NoActiveCredentials_NoOps_AndCallsSyncServiceNever()
    {
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>(MockBehavior.Strict);
        credentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>(MockBehavior.Strict);

        var job = new IbkrFlexIncrementalSyncJob(
            credentialRepo.Object, syncService.Object, Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(),
            Mock.Of<IConnectionHealthShadow>(), new IbkrFlexFailureClassifier(),
            HealthOptions.Monitor(new ConnectionHealthOptions()), TimeProvider.System,
            NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        syncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IncrementalSyncJob_SyncFailure_RecordsLastError_AndContinuesWithNextUser()
    {
        var failingUser = Guid.NewGuid();
        var okUser = Guid.NewGuid();
        var failing = new IBKRFlexCredential(failingUser, "999999", [1], [2], [3], 1);
        var ok = new IBKRFlexCredential(okUser, "999999", [1], [2], [3], 1);
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>();
        credentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[failing, ok]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>();
        syncService
            .Setup(s => s.SyncAsync(failingUser, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("An error occurred while saving the entity changes.",
                new InvalidOperationException("persist blew up")));
        syncService
            .Setup(s => s.SyncAsync(okUser, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        var job = new IbkrFlexIncrementalSyncJob(
            credentialRepo.Object, syncService.Object, Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(),
            Mock.Of<IConnectionHealthShadow>(), new IbkrFlexFailureClassifier(),
            HealthOptions.Monitor(new ConnectionHealthOptions()), TimeProvider.System,
            NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        failing.LastError.Should().Be("persist blew up");
        ok.LastError.Should().BeNull();
        credentialRepo.Verify(r => r.SaveLastErrorUnscopedAsync(failing, It.IsAny<CancellationToken>()), Times.Once);
        credentialRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        syncService.Verify(
            s => s.SyncAsync(okUser, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task BackfillJob_WindowFailure_RecordsLastError_AndContinuesWithRemainingWindows()
    {
        var userId = Guid.NewGuid();
        var credential = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>();
        credentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[credential]);
        var windows = IbkrFlexBackfillJob.BuildYearlyWindows();
        var syncService = new Mock<IIbkrFlexTradeSyncService>();
        syncService
            .Setup(s => s.SyncAsync(userId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));
        syncService
            .Setup(s => s.SyncAsync(userId, windows[0], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("window blew up"));

        var job = new IbkrFlexBackfillJob(
            credentialRepo.Object, syncService.Object, NullLogger<IbkrFlexBackfillJob>.Instance);

        await job.ExecuteAsync();

        credential.LastError.Should().Be("window blew up");
        credentialRepo.Verify(r => r.SaveLastErrorUnscopedAsync(credential, It.IsAny<CancellationToken>()), Times.Once);
        syncService.Verify(
            s => s.SyncAsync(userId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(windows.Count));
    }

    [Fact]
    public async Task BackfillJob_NoActiveCredentials_NoOps_AndCallsSyncServiceNever()
    {
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>(MockBehavior.Strict);
        credentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>(MockBehavior.Strict);

        var job = new IbkrFlexBackfillJob(
            credentialRepo.Object, syncService.Object, NullLogger<IbkrFlexBackfillJob>.Instance);

        await job.ExecuteAsync();

        syncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IncrementalSyncJob_ActiveCredential_SyncsOnce_WithShortLookbackWindow()
    {
        var userId = Guid.NewGuid();
        var credential = new IBKRFlexCredential(userId, "999999", [1], [2], [3], 1);
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>(MockBehavior.Strict);
        credentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[credential]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>(MockBehavior.Strict);
        syncService
            .Setup(s => s.SyncAsync(userId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        var job = new IbkrFlexIncrementalSyncJob(
            credentialRepo.Object, syncService.Object, Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(),
            Mock.Of<IConnectionHealthShadow>(), new IbkrFlexFailureClassifier(),
            HealthOptions.Monitor(new ConnectionHealthOptions()), TimeProvider.System,
            NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        syncService.Verify(
            s => s.SyncAsync(userId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void BuildYearlyWindows_WalksCalendarYearsFrom2022ToToday()
    {
        var asOf = new DateOnly(2026, 3, 15);

        var windows = IbkrFlexBackfillJob.BuildYearlyWindows(asOf);

        windows.Should().HaveCount(5); // 2022, 2023, 2024, 2025, 2026
        windows[0].FromDate.Should().Be(new DateOnly(2022, 1, 1));
        windows[0].ToDate.Should().Be(new DateOnly(2022, 12, 31));
        windows[3].FromDate.Should().Be(new DateOnly(2025, 1, 1));
        windows[3].ToDate.Should().Be(new DateOnly(2025, 12, 31));
        windows[^1].FromDate.Should().Be(new DateOnly(2026, 1, 1));
        windows[^1].ToDate.Should().Be(asOf, "the final window must stop at today, not run past it into 12-31");
    }

    [Fact]
    public void BuildYearlyWindows_EachWindowStaysWithinTheFlexApi365DayCap()
    {
        var windows = IbkrFlexBackfillJob.BuildYearlyWindows(new DateOnly(2026, 9, 26));

        windows.Should().OnlyContain(w => w.ToDate.DayNumber - w.FromDate.DayNumber <= 365);
    }

    [Fact]
    public async Task IncrementalSyncJob_RecordsEachAttemptWithTheConnectionHealthShadow()
    {
        var failingUser = Guid.NewGuid();
        var okUser = Guid.NewGuid();
        var failing = new IBKRFlexCredential(failingUser, "999999", [1], [2], [3], 1);
        var ok = new IBKRFlexCredential(okUser, "999999", [1], [2], [3], 1);
        var credentialRepo = new Mock<IIBKRFlexCredentialRepository>();
        credentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[failing, ok]);
        var syncService = new Mock<IIbkrFlexTradeSyncService>();
        syncService
            .Setup(s => s.SyncAsync(failingUser, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IbkrFlexException("1015", "Token is invalid."));
        syncService
            .Setup(s => s.SyncAsync(okUser, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));
        var shadow = new Mock<IConnectionHealthShadow>();

        var job = new IbkrFlexIncrementalSyncJob(
            credentialRepo.Object, syncService.Object, Mock.Of<IAlertGeneratorService>(), Mock.Of<IUserAlertPreferencesReader>(),
            shadow.Object, new IbkrFlexFailureClassifier(), HealthOptions.Monitor(new ConnectionHealthOptions()),
            TimeProvider.System, NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        shadow.Verify(s => s.RecordFailureAsync(
            It.Is<ConnectionHealthSubject>(subject =>
                subject.Id == failing.Id && subject.Provider == "ibkr-flex" && subject.Kind == nameof(IBKRFlexCredential)),
            It.IsAny<ConnectionHealth>(),
            It.Is<ProviderFailure>(f => f.Class == FailureClass.Credential && f.Strength == FailureStrength.Definitive),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        shadow.Verify(s => s.RecordSuccessAsync(
            It.Is<ConnectionHealthSubject>(subject => subject.Id == ok.Id),
            It.IsAny<ConnectionHealth>(),
            It.IsAny<Func<ConnectionHealth, CancellationToken, Task>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        // The existing LastError bookkeeping is unchanged.
        failing.LastError.Should().Be("Token is invalid.");
    }

    // ── Connection health (Option B, S3) ─────────────────────────────────────

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 6, 0, 0, TimeSpan.Zero);

    private sealed class PolicyHarness
    {
        public readonly Guid UserId = Guid.NewGuid();
        public readonly IBKRFlexCredential Credential;
        public readonly Mock<IIBKRFlexCredentialRepository> CredentialRepo = new();
        public readonly Mock<IIbkrFlexTradeSyncService> SyncService = new();
        public readonly Mock<IAlertGeneratorService> Alerts = new();
        public readonly Mock<IUserAlertPreferencesReader> Preferences = new();
        public readonly FakeTimeProvider Time = new(Start);

        /// <summary>Flex stays in shadow in the committed appsettings; a test opts it in.</summary>
        public readonly ConnectionHealthOptions Options = new();

        public PolicyHarness()
        {
            Credential = new IBKRFlexCredential(UserId, "999999", [1], [2], [3], 1);
            CredentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[Credential]);
            Preferences.Setup(p => p.GetAsync(UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UserAlertPreferences(false, 0m, SyncFailureAlerts: true));
        }

        public PolicyHarness Active()
        {
            Options.ActiveProviders.Add("ibkr-flex");
            return this;
        }

        public void GivenSyncFails(Exception ex) =>
            SyncService
                .Setup(s => s.SyncAsync(UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);

        public Task RunAsync() =>
            new IbkrFlexIncrementalSyncJob(
                CredentialRepo.Object, SyncService.Object, Alerts.Object, Preferences.Object,
                ShadowRecorders.Create(Time, Options), new IbkrFlexFailureClassifier(), HealthOptions.Monitor(Options), Time,
                NullLogger<IbkrFlexIncrementalSyncJob>.Instance).ExecuteAsync();

        public async Task RunDailyAsync(int days)
        {
            for (var day = 0; day < days; day++)
            {
                if (day > 0) Time.Advance(TimeSpan.FromDays(1));
                await RunAsync();
            }
        }

        public void VerifyRaised(SyncFailureClass failureClass, Times times) =>
            Alerts.Verify(a => a.GenerateSyncFailureAlertAsync(
                UserId, "ibkr-flex", null, null, It.IsAny<string?>(), failureClass, It.IsAny<CancellationToken>()), times);

        public void VerifyEscalated(Times times) =>
            Alerts.Verify(a => a.EscalateSyncFailureAlertAsync(
                UserId, "ibkr-flex", null, null, It.IsAny<string?>(), It.IsAny<CancellationToken>()), times);

        public void VerifyNoAlertCalls() => Alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Shadow_FailingForDays_RaisesNoAlert_AsBefore_WhileThePolicyRecords()
    {
        var harness = new PolicyHarness();
        harness.GivenSyncFails(new IbkrFlexException("1015", "Token is invalid."));

        await harness.RunDailyAsync(4);

        harness.VerifyNoAlertCalls();
        harness.Credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
    }

    [Fact]
    public async Task Active_InvalidToken_EscalatesAtOnce_WithTheReconnectAlert()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1015", "Token is invalid."));

        await harness.RunAsync();

        harness.VerifyEscalated(Times.Once());
        harness.Credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);
    }

    [Fact]
    public async Task Active_TransientFailure_AlertsOnlyOnTheThirdDailyRun_WithTheOutageClass()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1018", "Too many requests."));

        await harness.RunDailyAsync(2);
        harness.VerifyNoAlertCalls();

        harness.Time.Advance(TimeSpan.FromDays(1));
        await harness.RunAsync();
        harness.VerifyRaised(SyncFailureClass.Outage, Times.Once());

        harness.Time.Advance(TimeSpan.FromDays(1));
        await harness.RunAsync();
        harness.VerifyRaised(SyncFailureClass.Outage, Times.Once());
    }

    [Fact]
    public async Task Active_SuccessAfterTheAlert_ResolvesIt()
    {
        var harness = new PolicyHarness().Active();
        harness.SyncService
            .SetupSequence(s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IbkrFlexException("1012", "Token has expired."))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        await harness.RunDailyAsync(2);

        harness.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(harness.UserId, "ibkr-flex", null, It.IsAny<CancellationToken>()), Times.Once);
        harness.Credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatementNotAvailable_IsNoFailure_NoErrorNoCountNoAlert(bool active)
    {
        // The ~06:00 run routinely meets IBKR's "Statement is not available." answer.
        var harness = new PolicyHarness();
        if (active) harness.Active();
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunDailyAsync(10);

        harness.Credential.LastError.Should().BeNull();
        harness.Credential.Health.Should().Be(new ConnectionHealth(), "it is neither a failure nor a success of the connection");
        harness.CredentialRepo.Verify(r => r.SaveLastErrorUnscopedAsync(It.IsAny<IBKRFlexCredential>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.CredentialRepo.Verify(r => r.SaveHealthUnscopedAsync(It.IsAny<Guid>(), It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.VerifyNoAlertCalls();
    }
}
