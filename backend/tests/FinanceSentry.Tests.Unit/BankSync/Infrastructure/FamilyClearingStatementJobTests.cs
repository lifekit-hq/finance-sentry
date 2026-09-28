namespace FinanceSentry.Tests.Unit.BankSync.Infrastructure;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Failure posture of the monthly family-clearing statement job (#434 S5). Mirrors
/// BookPerformanceBriefJobTests: one user's statement failing must not cost the others theirs,
/// but a run in which every user failed must reach Hangfire as a failure.
/// </summary>
public sealed class FamilyClearingStatementJobTests
{
    private static readonly Guid UserA = Guid.Parse("44444444-0000-0000-0000-00000000000a");
    private static readonly Guid UserB = Guid.Parse("44444444-0000-0000-0000-00000000000b");

    private readonly Mock<IBankingTotalsReader> _users = new();
    private readonly Mock<IQueryHandler<GetFamilyClearingStatementQuery, FamilyClearingStatement>> _statementQuery = new();
    private readonly Mock<IAlertGeneratorService> _alerts = new();

    private static FamilyClearingStatement Statement() =>
        new("2026-08", [], SupportTotalUsd: 0m, ReceivedTotalUsd: 0m, ExcludedRoutingLegs: 0);

    private FamilyClearingStatementJob Job() =>
        new(_users.Object, _statementQuery.Object, _alerts.Object,
            NullLogger<FamilyClearingStatementJob>.Instance);

    private void ActiveUsers(params Guid[] userIds) =>
        _users
            .Setup(u => u.GetActiveUserIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userIds);

    private void StatementFor(Guid userId, FamilyClearingStatement statement) =>
        _statementQuery
            .Setup(q => q.Handle(
                It.Is<GetFamilyClearingStatementQuery>(query => query.UserId == userId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(statement);

    private void StatementThrowsFor(Guid userId, Exception error) =>
        _statementQuery
            .Setup(q => q.Handle(
                It.Is<GetFamilyClearingStatementQuery>(query => query.UserId == userId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(error);

    [Fact]
    public async Task GeneratesOneStatementAlertPerActiveUser()
    {
        ActiveUsers(UserA, UserB);
        StatementFor(UserA, Statement());
        StatementFor(UserB, Statement());

        await Job().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateFamilyStatementAlertAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OneUserFailing_StillStatementsTheOthers_AndSucceeds()
    {
        ActiveUsers(UserA, UserB);
        StatementThrowsFor(UserA, new InvalidOperationException("classification unavailable"));
        StatementFor(UserB, Statement());

        await Job().ExecuteAsync();

        _alerts.Verify(
            a => a.GenerateFamilyStatementAlertAsync(
                UserB, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EveryUserFailing_FailsTheJob_SoTheFailureStreakIsObservable()
    {
        ActiveUsers(UserA, UserB);
        StatementThrowsFor(UserA, new InvalidOperationException("classification unavailable"));
        StatementThrowsFor(UserB, new TimeoutException("classification timed out"));

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
