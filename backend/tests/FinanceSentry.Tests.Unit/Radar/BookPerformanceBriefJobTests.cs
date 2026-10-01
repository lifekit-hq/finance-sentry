namespace FinanceSentry.Tests.Unit.Radar;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Ports;
using FinanceSentry.Modules.Radar.Domain.Repositories;
using FinanceSentry.Modules.Radar.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Failure posture of the weekly brief job (feature 414, US2). One user's brief failing must not
/// cost the others theirs, but a run in which every user failed produced nothing at all and has to
/// reach Hangfire as a failure — otherwise <c>ConsecutiveFailureAlertFilter</c> never sees a streak
/// and a total outage is indistinguishable from a quiet week.
/// </summary>
public sealed class BookPerformanceBriefJobTests
{
    private static readonly Guid UserA = Guid.Parse("33333333-0000-0000-0000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("33333333-0000-0000-0000-00000000000b");

    private readonly Mock<IBankingTotalsReader> _users = new();
    private readonly Mock<IBookPerformanceService> _performance = new();
    private readonly Mock<IRadarSignalRepository> _signals = new();
    private readonly Mock<ITrackRecordSource> _trackRecord = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static BookPerformanceResult Scoreboard() =>
        new(
            [new PeriodTwr(BookPerformancePeriod.OneWeek, new DateOnly(2026, 8, 24), 0.03m, 0.01m, 0.02m, "outperform")],
            new DateOnly(2026, 8, 31));

    private BookPerformanceBriefJob Job()
    {
        _signals
            .Setup(s => s.ListForUserUnscopedAsync(It.IsAny<Guid>(), It.IsAny<SignalFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _trackRecord
            .Setup(t => t.GetDeltaAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrackRecordDelta?)null);

        return new BookPerformanceBriefJob(
            _users.Object,
            _performance.Object,
            _signals.Object,
            _trackRecord.Object,
            _alerts.Object,
            NullLogger<BookPerformanceBriefJob>.Instance);
    }

    private void ActiveUsers(params Guid[] userIds) =>
        _users
            .Setup(u => u.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

    private void PerformanceFor(Guid userId, BookPerformanceResult result) =>
        _performance
            .Setup(p => p.GetAsync(userId, It.IsAny<IReadOnlyList<BookPerformancePeriod>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void PerformanceThrowsFor(Guid userId, Exception error) =>
        _performance
            .Setup(p => p.GetAsync(userId, It.IsAny<IReadOnlyList<BookPerformancePeriod>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

    [Fact]
    public async Task GeneratesOneBriefPerActiveUser()
    {
        ActiveUsers(UserA, UserB);
        PerformanceFor(UserA, Scoreboard());
        PerformanceFor(UserB, Scoreboard());

        await Job().ExecuteAsync();

        _alerts.Verify(
            a => a.GeneratePerformanceBriefAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OneUserFailing_StillBriefsTheOthers_AndSucceeds()
    {
        ActiveUsers(UserA, UserB);
        PerformanceThrowsFor(UserA, new InvalidOperationException("price history unavailable"));
        PerformanceFor(UserB, Scoreboard());

        await Job().ExecuteAsync();

        _alerts.Verify(
            a => a.GeneratePerformanceBriefAlertAsync(
                UserB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EveryUserFailing_FailsTheJob_SoTheFailureStreakIsObservable()
    {
        ActiveUsers(UserA, UserB);
        PerformanceThrowsFor(UserA, new InvalidOperationException("price history unavailable"));
        PerformanceThrowsFor(UserB, new TimeoutException("alerts store timed out"));

        var act = () => Job().ExecuteAsync();

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().HaveCount(2);
    }

    [Fact]
    public async Task TotalFailureLedByATransientError_IsStillClassifiedAsAStickyFailure()
    {
        ActiveUsers(UserA, UserB);
        PerformanceThrowsFor(UserA, new TimeoutException("price feed timed out"));
        PerformanceThrowsFor(UserB, new InvalidOperationException("price history unavailable"));

        var act = () => Job().ExecuteAsync();

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        // The first user's failure being transient must not excuse the whole run: otherwise the
        // streak never increments and the AC-required Telegram alert never fires.
        JobFailureTransientClassifier.IsTransient(thrown.Which).Should().BeFalse();
    }

    [Fact]
    public async Task NoActiveUsers_IsNotAFailure()
    {
        ActiveUsers();

        await Job().ExecuteAsync();

        _alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UsersWithoutEnoughHistory_AreSkippedWithoutFailingTheJob()
    {
        ActiveUsers(UserA);
        PerformanceFor(UserA, BookPerformanceResult.Empty(new DateOnly(2026, 8, 31)));

        await Job().ExecuteAsync();

        _alerts.VerifyNoOtherCalls();
    }
}
