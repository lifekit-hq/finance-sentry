namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// The projections' one reader leaves out the months a coverage hole touches: spend that never reached the app is
/// missing data, not a cheap month, so it feeds neither a median nor a savings path. Every figure here is synthetic.
/// </summary>
public class ProjectionInputsReaderCoverageHoleTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<INetWorthSnapshotRepository> _snapshots = new();
    private readonly Mock<IHonestMonthlyFlowReader> _monthlyFlow = new();
    private readonly Mock<IUserFireAssumptionsReader> _assumptions = new();
    private readonly Mock<ICoverageHoleReader> _holes = new();
    private readonly FakeTimeProvider _clock = new(Now);

    public ProjectionInputsReaderCoverageHoleTests()
    {
        _snapshots.Setup(r => r.GetLatestByUserIdUnscopedAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NetWorthSnapshot { Id = Guid.NewGuid(), UserId = UserId, SnapshotDate = new DateOnly(2026, 9, 20), TotalNetWorth = 90_000m, TakenAt = Now });
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Months());
        _assumptions.Setup(r => r.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((FireAssumptions?)null);
    }

    // Five complete months at 3,000 out and 1,000 saved, two of them thin because the card moved, and the month in progress.
    private static IReadOnlyList<HonestMonthlyFlow> Months() =>
    [
        new("2026-04", 3_000m, 1_000m),
        new("2026-05", 3_000m, 1_000m),
        new("2026-06", 3_000m, 1_000m),
        new("2026-07", 3_000m, 1_000m),
        new("2026-08", 3_000m, 1_000m),
        new("2026-09", 100m, 2_900m),
    ];

    private ProjectionInputsReader Reader() =>
        new(_snapshots.Object, _monthlyFlow.Object, _assumptions.Object, _holes.Object, _clock);

    private void HoleFrom(DateOnly from, DateOnly? to = null) =>
        _holes.Setup(h => h.GetHolesAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CoverageHoleSpan(AccountId, from, to)]);

    [Fact]
    public async Task Without_a_hole_every_complete_month_counts()
    {
        _holes.Setup(h => h.GetHolesAsync(UserId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var inputs = await Reader().ReadAsync(UserId, CancellationToken.None);

        inputs.CompleteMonths.Select(m => m.Month).Should().Equal("2026-04", "2026-05", "2026-06", "2026-07", "2026-08");
    }

    [Fact]
    public async Task A_month_a_hole_touches_is_left_out_whole_not_counted_as_frugal()
    {
        // The hole opens mid-July and is still open: July and August are missing data.
        HoleFrom(new DateOnly(2026, 7, 13));

        var inputs = await Reader().ReadAsync(UserId, CancellationToken.None);

        inputs.CompleteMonths.Select(m => m.Month).Should().Equal("2026-04", "2026-05", "2026-06");
        inputs.SavingsPath.Should().NotBeNull();
    }

    [Fact]
    public async Task The_month_in_progress_is_never_a_complete_month_even_inside_a_hole()
    {
        HoleFrom(new DateOnly(2026, 9, 1));

        var inputs = await Reader().ReadAsync(UserId, CancellationToken.None);

        inputs.CompleteMonths.Select(m => m.Month).Should().Equal("2026-04", "2026-05", "2026-06", "2026-07", "2026-08");
    }

    [Fact]
    public async Task Too_few_months_outside_the_hole_withhold_the_savings_path_instead_of_projecting_from_thin_ones()
    {
        HoleFrom(new DateOnly(2026, 5, 1));

        var inputs = await Reader().ReadAsync(UserId, CancellationToken.None);

        inputs.CompleteMonths.Should().ContainSingle().Which.Month.Should().Be("2026-04");
        inputs.SavingsPath.Should().BeNull();
    }

    [Fact]
    public async Task The_fire_projection_takes_its_median_spend_from_the_months_outside_the_hole()
    {
        _monthlyFlow.Setup(r => r.GetMonthlyFlowAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new HonestMonthlyFlow("2026-04", 3_000m, 1_000m),
                new HonestMonthlyFlow("2026-05", 3_000m, 1_000m),
                new HonestMonthlyFlow("2026-06", 3_000m, 1_000m),
                new HonestMonthlyFlow("2026-07", 200m, 2_800m),
                new HonestMonthlyFlow("2026-08", 150m, 2_850m),
                new HonestMonthlyFlow("2026-09", 100m, 2_900m),
            ]);
        HoleFrom(new DateOnly(2026, 7, 6));
        var handler = new GetFireProjectionQueryHandler(Reader(), new ScenarioReader(new FakeCareerEventRepository(), _clock), _clock);

        var result = await handler.Handle(new GetFireProjectionQuery(UserId), CancellationToken.None);

        // Read as frugal the median spend would be 200 a month; from the three real months it is 3,000.
        result.CompleteMonths.Should().Be(3);
        result.AnnualSpend.Should().Be(36_000m);
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
