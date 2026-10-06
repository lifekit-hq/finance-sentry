namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Failure posture of the monthly FIRE brief job (#433 S7). Mirrors FamilyClearingStatementJobTests:
/// one user's brief failing must not cost the others theirs, but a run in which every user failed must
/// reach Hangfire as a failure; a user without enough history is skipped, not failed.
/// </summary>
public sealed class FireBriefJobTests
{
    private static readonly Guid UserA = Guid.Parse("33333333-0000-0000-0000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("33333333-0000-0000-0000-00000000000b");

    private readonly Mock<IBankingTotalsReader> _users = new();
    private readonly Mock<IQueryHandler<GetFireProjectionQuery, FireProjectionResponse>> _projection = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static FireProjectionResponse Projection(FireProjectionStatus status) =>
        new(status, Target: 1_200_000m, CurrentNetWorth: 300_000m, MonthlySavings: 2_000m,
            AnnualSpend: 48_000m, SafeWithdrawalRate: 0.04m, RealAnnualReturn: 0.05m,
            ProjectedDate: new DateOnly(2041, 3, 1), MonthsToFire: 173m, HasStaleSleeves: false);

    private FireBriefJob Job() =>
        new(_users.Object, _projection.Object, _alerts.Object, NullLogger<FireBriefJob>.Instance);

    private void ActiveUsers(params Guid[] userIds) =>
        _users
            .Setup(u => u.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

    private void ProjectionFor(Guid userId, FireProjectionResponse response) =>
        _projection
            .Setup(q => q.Handle(
                It.Is<GetFireProjectionQuery>(query => query.UserId == userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private void ProjectionThrowsFor(Guid userId, Exception error) =>
        _projection
            .Setup(q => q.Handle(
                It.Is<GetFireProjectionQuery>(query => query.UserId == userId), It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

    [Fact]
    public async Task RaisesOneBriefPerActiveUser()
    {
        ActiveUsers(UserA, UserB);
        ProjectionFor(UserA, Projection(FireProjectionStatus.Projected));
        ProjectionFor(UserB, Projection(FireProjectionStatus.NotSaving));

        await Job().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateFireBriefAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task InsufficientHistory_RaisesNoBrief_AndIsNotAFailure()
    {
        ActiveUsers(UserA);
        ProjectionFor(UserA, Projection(FireProjectionStatus.InsufficientHistory));

        await Job().ExecuteAsync();

        _alerts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OneUserFailing_StillBriefsTheOthers_AndSucceeds()
    {
        ActiveUsers(UserA, UserB);
        ProjectionThrowsFor(UserA, new InvalidOperationException("flow unavailable"));
        ProjectionFor(UserB, Projection(FireProjectionStatus.Projected));

        await Job().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateFireBriefAlertAsync(
                UserB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EveryUserFailing_FailsTheJob_SoTheFailureStreakIsObservable()
    {
        ActiveUsers(UserA, UserB);
        ProjectionThrowsFor(UserA, new InvalidOperationException("flow unavailable"));
        ProjectionThrowsFor(UserB, new TimeoutException("flow timed out"));

        var act = () => Job().ExecuteAsync();

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().HaveCount(2);
    }

    [Fact]
    public async Task NoActiveUsers_IsNotAFailure()
    {
        ActiveUsers();

        await Job().ExecuteAsync();

        _alerts.VerifyNoOtherCalls();
    }
}
