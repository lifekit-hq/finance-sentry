using FinanceSentry.Mcp.Tools;
using FinanceSentry.Modules.Radar.Application.Services;
using FinanceSentry.Modules.Radar.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetBookPerformanceToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Mock<IBookPerformanceService> _performance = new();

    private GetBookPerformanceTool CreateSut() =>
        new(_performance.Object, new FakeIdentityResolver { ResolvedUserId = UserId });

    [Fact]
    public async Task ExecuteAsync_DelegatesToService_WithAllPeriodsWhenNoneRequested()
    {
        var expected = new BookPerformanceResult(
            [new PeriodTwr(BookPerformancePeriod.OneWeek, Today.AddDays(-7), 0.03m, 0.01m, 0.02m, "outperform")],
            Today);

        _performance.Setup(s => s.GetAsync(
                UserId,
                It.Is<IReadOnlyList<BookPerformancePeriod>>(l => l.Count == 4),
                default))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(periods: null);

        result.Periods.Should().HaveCount(1);
        result.Periods[0].Verdict.Should().Be("outperform");
    }

    [Fact]
    public async Task ExecuteAsync_PassesRequestedPeriods_WhenProvided()
    {
        var requested = new List<BookPerformancePeriod> { BookPerformancePeriod.OneMonth };
        var expected = new BookPerformanceResult(
            [new PeriodTwr(BookPerformancePeriod.OneMonth, Today.AddMonths(-1), 0.05m, 0.04m, 0.01m, "outperform")],
            Today);

        _performance.Setup(s => s.GetAsync(
                UserId,
                It.Is<IReadOnlyList<BookPerformancePeriod>>(l => l.Count == 1 && l[0] == BookPerformancePeriod.OneMonth),
                default))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(periods: requested);

        result.Periods.Should().HaveCount(1);
        result.Periods[0].Period.Should().Be(BookPerformancePeriod.OneMonth);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsEmpty_WhenIdentityUnresolvable()
    {
        var sut = new GetBookPerformanceTool(
            _performance.Object,
            new FakeIdentityResolver { ResolvedUserId = null });

        var result = await sut.ExecuteAsync();

        result.Periods.Should().BeEmpty();
        _performance.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_UsesAuthenticatedIdentity()
    {
        var expected = BookPerformanceResult.Empty(Today);

        _performance.Setup(s => s.GetAsync(UserId, It.IsAny<IReadOnlyList<BookPerformancePeriod>>(), default))
            .ReturnsAsync(expected);

        var result = await CreateSut().ExecuteAsync(periods: null);

        result.Should().Be(expected);
        _performance.Verify(
            s => s.GetAsync(UserId, It.IsAny<IReadOnlyList<BookPerformancePeriod>>(), default),
            Times.Once);
    }
}
