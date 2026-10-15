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
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
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
            HealthOptions.Monitor(new ConnectionHealthOptions()), ShadowRecorders.SourceOptions(), Mock.Of<IBackgroundJobClient>(),
            TimeProvider.System, NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

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
            HealthOptions.Monitor(new ConnectionHealthOptions()), ShadowRecorders.SourceOptions(), Mock.Of<IBackgroundJobClient>(),
            TimeProvider.System, NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

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
    public async Task IncrementalSyncJob_ActiveCredential_SyncsOnce_WithAWeekEndingYesterday()
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
            HealthOptions.Monitor(new ConnectionHealthOptions()), ShadowRecorders.SourceOptions(), Mock.Of<IBackgroundJobClient>(),
            new FakeTimeProvider(Start), NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

        await job.ExecuteAsync();

        // IBKR answers a window ending on a day it has not closed yet with 1003, so today is never asked for.
        syncService.Verify(
            s => s.SyncAsync(
                userId,
                new FlexStatementWindow(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 30)),
                It.IsAny<CancellationToken>()),
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
        windows[^1].ToDate.Should().Be(new DateOnly(2026, 3, 14), "the final window stops at yesterday: IBKR has no statement for today");
    }

    [Fact]
    public void BuildYearlyWindows_OnNewYearsDay_EndsWithLastYearsFullWindow()
    {
        var windows = IbkrFlexBackfillJob.BuildYearlyWindows(new DateOnly(2027, 1, 1));

        windows[^1].Should().Be(new FlexStatementWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
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
            ShadowRecorders.SourceOptions(), Mock.Of<IBackgroundJobClient>(), TimeProvider.System,
            NullLogger<IbkrFlexIncrementalSyncJob>.Instance);

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
        public readonly Mock<IBackgroundJobClient> Jobs = new();
        public readonly List<(Job Job, IState State)> Scheduled = [];

        /// <summary>Flex stays in shadow in the committed appsettings; a test opts it in.</summary>
        public readonly ConnectionHealthOptions Options = new();

        /// <summary>The settings the BrokerageSync module registers for Flex, so the job is exercised as deployed.</summary>
        public readonly ConnectionHealthSourceOptions Source = new()
        {
            NotReadyDeadline = TimeSpan.FromHours(48),
            NotReadyRetryAfter = TimeSpan.FromHours(2),
            EarlyRechecks = [TimeSpan.FromHours(1), TimeSpan.FromHours(4)],
        };

        public PolicyHarness()
        {
            Jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Callback<Job, IState>((job, state) => Scheduled.Add((job, state)))
                .Returns("1");
            Credential = new IBKRFlexCredential(UserId, "999999", [1], [2], [3], 1);
            CredentialRepo.Setup(r => r.GetAllActiveUnscopedAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<IBKRFlexCredential>)[Credential]);
            CredentialRepo.Setup(r => r.GetByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Credential);
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

        private IbkrFlexIncrementalSyncJob CreateJob()
        {
            var sources = new Dictionary<string, ConnectionHealthSourceOptions> { ["ibkr-flex"] = Source };
            return new IbkrFlexIncrementalSyncJob(
                CredentialRepo.Object, SyncService.Object, Alerts.Object, Preferences.Object,
                ShadowRecorders.Create(Time, Options, sources: sources), new IbkrFlexFailureClassifier(),
                HealthOptions.Monitor(Options), ShadowRecorders.SourceOptions(sources), Jobs.Object, Time,
                NullLogger<IbkrFlexIncrementalSyncJob>.Instance);
        }

        public Task RunAsync() => CreateJob().ExecuteAsync();

        public Task RetryAsync(int attempt) => CreateJob().RetryUserAsync(UserId, attempt);

        public Task RecheckAsync(int recheck) => CreateJob().RecheckUserAsync(UserId, recheck);

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

    [Fact]
    public async Task StatementNotAvailableThroughYesterday_AsksOnceMoreThroughTheDayBefore_AndRecordsASuccess()
    {
        var harness = new PolicyHarness();
        var throughYesterday = new FlexStatementWindow(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 30));
        var throughDayBefore = new FlexStatementWindow(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 29));
        harness.SyncService
            .Setup(s => s.SyncAsync(harness.UserId, throughYesterday, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IbkrFlexException("1003", "Statement is not available."));
        harness.SyncService
            .Setup(s => s.SyncAsync(harness.UserId, throughDayBefore, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        await harness.RunAsync();

        harness.SyncService.Verify(s => s.SyncAsync(harness.UserId, throughDayBefore, It.IsAny<CancellationToken>()), Times.Once);
        harness.Credential.LastError.Should().BeNull();
        harness.Credential.Health.ConsecutiveFailures.Should().Be(0);
        harness.Credential.Health.LastSuccessAt.Should().Be(Start);
    }

    [Fact]
    public async Task OtherFlexErrors_AreNotRetriedWithAnEarlierWindow()
    {
        var harness = new PolicyHarness();
        harness.GivenSyncFails(new IbkrFlexException("1015", "Token is invalid."));

        await harness.RunAsync();

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StatementNotAvailable_PastTheStepBack_IsRecordedAsNotReady_NotCountedWithinTheDeadline()
    {
        // While the window ended today this answer met every weekday run and was recorded nowhere. It is the
        // provider still preparing the day: the last success is younger than the source's deadline, so no failure
        // is counted, but LastError still says what happened.
        var harness = new PolicyHarness().Active();
        harness.Credential.ApplyHealth(new ConnectionHealth { LastSuccessAt = Start.AddHours(-20) });
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunAsync();

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        harness.Credential.LastError.Should().Be("Statement is not available.");
        harness.Credential.Health.ConsecutiveFailures.Should().Be(0);
        harness.Credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        harness.VerifyNoAlertCalls();
    }

    [Fact]
    public async Task StatementNotAvailable_PastTheDeadline_CountsLikeATransientFailure_AndAlertsAsAnOutage()
    {
        var harness = new PolicyHarness().Active();
        harness.Credential.ApplyHealth(new ConnectionHealth { LastSuccessAt = Start.AddHours(-49) });
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunDailyAsync(3);

        harness.Credential.Health.ConsecutiveFailures.Should().Be(3);
        harness.Credential.Health.LastFailureClass.Should().Be(FailureClass.NotReady);
        harness.Credential.Health.LastFailureCode.Should().Be("1003");
        harness.VerifyRaised(SyncFailureClass.Outage, Times.Once());
    }

    [Fact]
    public async Task StatementNotAvailable_OnAFreshCredentialThatNeverSucceeded_IsNotCountedUntilTheDeadlineAfterConnecting()
    {
        var harness = new PolicyHarness().Active();
        harness.Time.SetUtcNow(DateTimeOffset.UtcNow);
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunDailyAsync(2);

        harness.Credential.Health.ConsecutiveFailures.Should().Be(0);
        harness.VerifyNoAlertCalls();

        harness.Time.Advance(TimeSpan.FromDays(1));
        await harness.RunAsync();

        harness.Credential.Health.ConsecutiveFailures.Should().Be(1);
        harness.Credential.Health.LastFailureClass.Should().Be(FailureClass.NotReady);
        harness.VerifyNoAlertCalls();
    }

    [Fact]
    public async Task StatementNotAvailable_InShadow_RecordsNotReadyAndRaisesNoAlert()
    {
        var harness = new PolicyHarness();
        harness.Credential.ApplyHealth(new ConnectionHealth { LastSuccessAt = Start.AddHours(-49) });
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunDailyAsync(3);

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Exactly(6));
        harness.Credential.Health.LastFailureClass.Should().Be(FailureClass.NotReady);
        harness.VerifyNoAlertCalls();
    }

    // ── Not ready: ask again sooner than the next daily run ──────────────────

    [Fact]
    public async Task NotReady_SchedulesAnotherLookTwoHoursOn_ForThatUser()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunAsync();

        var (job, state) = harness.Scheduled.Should().ContainSingle().Subject;
        job.Type.Should().Be(typeof(IbkrFlexIncrementalSyncJob));
        job.Method.Name.Should().Be(nameof(IbkrFlexIncrementalSyncJob.RetryUserAsync));
        job.Args[0].Should().Be(harness.UserId);
        job.Args[1].Should().Be(1);
        state.Should().BeOfType<ScheduledState>().Which.EnqueueAt
            .Should().BeCloseTo(DateTime.UtcNow + TimeSpan.FromHours(2), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task NotReady_RetryChain_EndsWhereTheNextDailyRunTakesOver()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        // 2 h x 11 = 22 h is still before the next daily run; the 12th look would land after it.
        await harness.RetryAsync(attempt: 10);
        harness.Scheduled.Should().ContainSingle().Which.Job.Args[1].Should().Be(11);

        harness.Scheduled.Clear();
        await harness.RetryAsync(attempt: 11);
        harness.Scheduled.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1015")] // credential, definitive
    [InlineData("1018")] // rate limited
    public async Task OtherFailureClasses_ScheduleNoSoonerLook(string flexCode)
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException(flexCode, "refused"));

        await harness.RunAsync();

        harness.Scheduled.Should().BeEmpty();
    }

    // ── Early re-checks of a once-a-day source ───────────────────────────────

    [Fact]
    public async Task TransientFailure_SchedulesOneEarlyRecheck_AnHourOn()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1009", "Server busy."));

        await harness.RunAsync();

        var (job, state) = harness.Scheduled.Should().ContainSingle().Subject;
        job.Method.Name.Should().Be(nameof(IbkrFlexIncrementalSyncJob.RecheckUserAsync));
        job.Args[0].Should().Be(harness.UserId);
        job.Args[1].Should().Be(1);
        state.Should().BeOfType<ScheduledState>().Which.EnqueueAt
            .Should().BeCloseTo(DateTime.UtcNow + TimeSpan.FromHours(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SuccessfulRun_SchedulesNoRecheck()
    {
        var harness = new PolicyHarness().Active();
        harness.SyncService
            .Setup(s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        await harness.RunAsync();

        harness.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task SuspectCredentialFailure_SchedulesAnEarlyRecheck_SoItIsConfirmedTheSameDay()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1020", "Request failed."));

        await harness.RunAsync();

        harness.Scheduled.Should().ContainSingle().Which.Job.Method.Name
            .Should().Be(nameof(IbkrFlexIncrementalSyncJob.RecheckUserAsync));
    }

    [Fact]
    public async Task Recheck_Chain_EndsWithTheConfiguredList()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1009", "Server busy."));
        await harness.RunAsync();
        harness.Scheduled.Clear();

        await harness.RecheckAsync(recheck: 1);
        var (job, state) = harness.Scheduled.Should().ContainSingle().Subject;
        job.Args[1].Should().Be(2);
        state.Should().BeOfType<ScheduledState>().Which.EnqueueAt
            .Should().BeCloseTo(DateTime.UtcNow + TimeSpan.FromHours(4), TimeSpan.FromMinutes(1));

        harness.Scheduled.Clear();
        await harness.RecheckAsync(recheck: 2);
        harness.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task Failure_WithNoEarlyRecheckSetting_SchedulesNothing()
    {
        var harness = new PolicyHarness().Active();
        harness.Source.EarlyRechecks = [];
        harness.GivenSyncFails(new IbkrFlexException("1009", "Server busy."));

        await harness.RunAsync();

        harness.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task Recheck_ForARecoveredConnection_DoesNotCallIbkr()
    {
        var harness = new PolicyHarness().Active();

        await harness.RecheckAsync(recheck: 1);

        harness.SyncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recheck_BeforeNotBefore_DoesNotCallIbkr()
    {
        var harness = new PolicyHarness().Active();
        harness.Credential.ApplyHealth(new ConnectionHealth
        {
            State = ConnectionHealthState.Degraded,
            ConsecutiveFailures = 1,
            FirstFailureAt = Start,
            LastFailureAt = Start,
            LastFailureClass = FailureClass.RateLimited,
            NotBefore = Start.AddHours(3),
        });

        await harness.RecheckAsync(recheck: 1);

        harness.SyncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Recheck_WhilePaused_DoesNotCallIbkr()
    {
        var harness = new PolicyHarness().Active();
        harness.Credential.ApplyHealth(ActionRequiredSince(Start.AddHours(-1)));

        await harness.RecheckAsync(recheck: 1);

        harness.SyncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NotReadyRetry_FailingTransiently_StartsNoRecheckChain()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1009", "Server busy."));

        await harness.RetryAsync(attempt: 1);

        harness.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task NotReady_WithNoRetrySetting_WaitsForTheNextDailyRun()
    {
        var harness = new PolicyHarness().Active();
        harness.Source.NotReadyRetryAfter = null;
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        await harness.RunAsync();

        harness.Scheduled.Should().BeEmpty();
    }

    [Fact]
    public async Task NotReady_SchedulerFailing_DoesNotFailTheRun()
    {
        var harness = new PolicyHarness().Active();
        harness.Jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Throws(new InvalidOperationException("storage down"));
        harness.GivenSyncFails(new IbkrFlexException("1003", "Statement is not available."));

        var act = () => harness.RunAsync();

        await act.Should().NotThrowAsync();
        harness.Credential.LastError.Should().Be("Statement is not available.");
    }

    [Fact]
    public async Task RetryUser_SyncsThatUsersCredentialOnce_AndRecordsASuccess()
    {
        var harness = new PolicyHarness().Active();
        harness.SyncService
            .Setup(s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        await harness.RetryAsync(attempt: 1);

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Credential.Health.LastSuccessAt.Should().Be(Start);
    }

    [Fact]
    public async Task RetryUser_ForAnActionRequiredConnectionInsideTheDay_DoesNotCallIbkr()
    {
        var harness = new PolicyHarness().Active();
        harness.Credential.ApplyHealth(ActionRequiredSince(Start.AddHours(-2)));

        await harness.RetryAsync(attempt: 1);

        harness.SyncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RetryUser_ForACredentialThatIsGone_DoesNothing()
    {
        var harness = new PolicyHarness().Active();
        harness.CredentialRepo
            .Setup(r => r.GetByUserIdUnscopedAsync(harness.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IBKRFlexCredential?)null);

        await harness.RetryAsync(attempt: 1);

        harness.SyncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RateLimited_1018_IsRecordedAsRateLimited_AndAsksNoSoonerThanTheDailyRun()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1018", "Too many requests."));

        await harness.RunAsync();

        harness.Credential.Health.LastFailureClass.Should().Be(FailureClass.RateLimited);
        harness.Credential.Health.NotBefore.Should().BeNull();
        harness.Scheduled.Should().BeEmpty();
    }

    // ── A confirmed-lost credential is not re-hit every run ──────────────────

    private static ConnectionHealth ActionRequiredSince(DateTimeOffset lastFailureAt) =>
        new()
        {
            State = ConnectionHealthState.ActionRequired,
            ConsecutiveFailures = 1,
            FirstFailureAt = lastFailureAt,
            LastFailureAt = lastFailureAt,
            LastFailureClass = FailureClass.Credential,
            LastFailureCode = "FLEX_1015",
        };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncrementalSyncJob_ActionRequired_SkipsTheCredentialInsideTheDay(bool active)
    {
        var harness = new PolicyHarness();
        if (active) harness.Active();
        harness.Credential.ApplyHealth(ActionRequiredSince(Start.AddHours(-2)));
        harness.GivenSyncFails(new IbkrFlexException("1015", "Token is invalid."));

        await harness.RunAsync();

        harness.SyncService.Verify(
            s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        harness.VerifyNoAlertCalls();
        harness.CredentialRepo.Verify(r => r.SaveHealthUnscopedAsync(It.IsAny<Guid>(), It.IsAny<ConnectionHealth>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IncrementalSyncJob_ActionRequired_ProbesOnEveryDailyRun_NotOnlyEveryOtherDay()
    {
        // Each run stamps its failure a few seconds after its cron time, so a strict "24 h since the last
        // failure" would let the next daily run start just short of it and skip.
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1015", "Token is invalid."));
        await harness.RunAsync();
        harness.Time.Advance(TimeSpan.FromSeconds(20));
        harness.Credential.ApplyHealth(harness.Credential.Health with { LastFailureAt = harness.Time.GetUtcNow() });

        harness.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(20) - TimeSpan.FromSeconds(8));
        await harness.RunAsync();
        harness.Time.Advance(TimeSpan.FromDays(1));
        await harness.RunAsync();

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        harness.VerifyEscalated(Times.Once());
    }

    [Fact]
    public async Task IncrementalSyncJob_ActionRequired_ASixHourReRunDoesNotProbeAgain()
    {
        var harness = new PolicyHarness().Active();
        harness.GivenSyncFails(new IbkrFlexException("1013", "Invalid IP address."));
        await harness.RunAsync();

        harness.Time.Advance(TimeSpan.FromHours(6));
        await harness.RunAsync();

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IncrementalSyncJob_ActionRequired_SuccessfulProbeRecoversWithoutAReconnect()
    {
        // A definitive loss fixed on IBKR's side (an IP restriction lifted) heals on the daily probe.
        var harness = new PolicyHarness().Active();
        harness.SyncService
            .SetupSequence(s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IbkrFlexException("1013", "Invalid IP address."))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));

        await harness.RunDailyAsync(2);

        harness.Credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        harness.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(harness.UserId, "ibkr-flex", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IncrementalSyncJob_ActionRequired_AfterReplace_TheNextRunSyncs()
    {
        var harness = new PolicyHarness().Active();
        harness.Credential.ApplyHealth(ActionRequiredSince(Start.AddHours(-2)));
        harness.Credential.Replace("888888", [4], [5], [6], 1);
        harness.SyncService
            .Setup(s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IbkrFlexTradeSyncResult(0, 0));
        harness.Credential.Health.State.Should().Be(ConnectionHealthState.ActionRequired);

        await harness.RunAsync();

        harness.SyncService.Verify(
            s => s.SyncAsync(harness.UserId, It.IsAny<FlexStatementWindow?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Credential.Health.State.Should().Be(ConnectionHealthState.Healthy);
        harness.Credential.Health.LastSuccessAt.Should().Be(Start);
        harness.Alerts.Verify(a => a.ResolveSyncFailureAlertAsync(harness.UserId, "ibkr-flex", null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
