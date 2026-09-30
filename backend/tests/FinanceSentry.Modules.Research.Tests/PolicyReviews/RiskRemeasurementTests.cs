namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>
/// #700: risk tolerance, capacity and drawdown tolerance are re-measured on the review cadence, every
/// re-measurement is a NEW policy version (never an edit), and the change is diffable against the prior one.
/// </summary>
public class RiskRemeasurementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly Guid _userId = Guid.NewGuid();
    private readonly InMemoryIpsRepository _repo = new();

    [Fact]
    public void Never_measured_statement_is_due()
    {
        var status = RiskMeasurementSchedule.Evaluate(Ips(drawdown: 25m, measuredAt: null), Now);

        status.IsDue.Should().BeTrue();
        RiskMeasurementSchedule.RequestText(status, "quarterly").Should().Contain("never been measured");
    }

    [Fact]
    public void Missing_drawdown_tolerance_is_due_even_when_measured_today()
    {
        var status = RiskMeasurementSchedule.Evaluate(Ips(drawdown: null, measuredAt: Now), Now);

        status.IsDue.Should().BeTrue();
        status.DrawdownToleranceSet.Should().BeFalse();
        RiskMeasurementSchedule.RequestText(status, "quarterly").Should().Contain("No drawdown tolerance");
    }

    [Theory]
    [InlineData(-80, false)]
    [InlineData(-95, true)]
    public void Measurement_lapses_after_one_quarterly_cadence(int daysAgo, bool due)
    {
        var status = RiskMeasurementSchedule.Evaluate(Ips(drawdown: 25m, measuredAt: Now.AddDays(daysAgo)), Now);

        status.IsDue.Should().Be(due);
        (RiskMeasurementSchedule.RequestText(status, "quarterly") is not null).Should().Be(due);
    }

    [Fact]
    public async Task Recording_a_remeasurement_adds_a_new_version_and_keeps_the_prior_one_untouched()
    {
        _repo.Seed(Ips(drawdown: null, measuredAt: null, tolerance: 3, capacity: 3));

        var result = await Record(25m, tolerance: 4, capacity: 2);

        _repo.Versions.Should().HaveCount(2);
        var prior = _repo.Versions.Single(v => v.Version == 1);
        prior.IsCurrent.Should().BeFalse();
        prior.RiskTolerance.Should().Be(3);
        prior.MaxDrawdownTolerancePct.Should().BeNull();
        result.Ips.Version.Should().Be(2);
        result.Ips.IsCurrent.Should().BeTrue();
        result.Ips.RiskTolerance.Should().Be(4);
        result.Ips.RiskCapacity.Should().Be(2);
        result.Ips.MaxDrawdownTolerancePct.Should().Be(25m);
        result.Ips.RiskMeasuredAt.Should().Be(Now);
    }

    [Fact]
    public async Task Recording_a_remeasurement_carries_every_other_field_forward()
    {
        var before = Ips(drawdown: null, measuredAt: null);
        before.Goals = [new InvestmentGoal("Retire", 500_000m, new DateOnly(2045, 1, 1), "High")];
        before.Exclusions = ["tobacco"];
        before.SellDiscipline = "trim above band";
        _repo.Seed(before);

        var result = await Record(30m, tolerance: 3, capacity: 3);

        result.Ips.Goals.Should().BeEquivalentTo(before.Goals);
        result.Ips.Exclusions.Should().BeEquivalentTo(["tobacco"]);
        result.Ips.SellDiscipline.Should().Be("trim above band");
        result.Ips.ReviewCadence.Should().Be(before.ReviewCadence);
        result.Diff.Changes.Select(c => c.Field).Should().BeEquivalentTo(["maxDrawdownTolerancePct", "riskMeasuredAt"]);
    }

    [Fact]
    public async Task The_diff_reports_each_changed_risk_field_with_before_and_after()
    {
        _repo.Seed(Ips(drawdown: 15m, measuredAt: Now.AddDays(-200), tolerance: 3, capacity: 3));

        var result = await Record(25m, tolerance: 4, capacity: 3);

        result.Diff.FromVersion.Should().Be(1);
        result.Diff.ToVersion.Should().Be(2);
        result.Diff.Changes.Should().Contain(c => c.Field == "riskTolerance" && c.From == "3" && c.To == "4");
        result.Diff.Changes.Should().Contain(c => c.Field == "maxDrawdownTolerancePct" && c.From == "15" && c.To == "25");
        result.Diff.Changes.Should().NotContain(c => c.Field == "riskCapacity");
    }

    [Fact]
    public async Task Reconfirming_unchanged_numbers_still_records_a_fresh_measurement()
    {
        _repo.Seed(Ips(drawdown: 25m, measuredAt: Now.AddDays(-200), tolerance: 3, capacity: 3));

        var result = await Record(25m, tolerance: 3, capacity: 3);

        result.Ips.Version.Should().Be(2);
        result.Ips.RiskMeasuredAt.Should().Be(Now);
        result.Diff.Changes.Should().ContainSingle().Which.Field.Should().Be("riskMeasuredAt");
        RiskMeasurementSchedule.Evaluate(_repo.Versions.Single(v => v.IsCurrent), Now).IsDue.Should().BeFalse();
    }

    [Fact]
    public async Task An_unrelated_policy_save_does_not_pass_for_a_remeasurement()
    {
        var measured = Now.AddDays(-200);
        _repo.Seed(Ips(drawdown: 25m, measuredAt: measured, tolerance: 3, capacity: 3));

        var saved = await new SaveIpsCommandHandler(_repo, new FixedTimeProvider(Now)).Handle(
            new SaveIpsCommand(
                _userId, [], 10, null, 3, 3, 25m, [], null, null, "a new sell rule", null, [], "quarterly"),
            default);

        saved.Version.Should().Be(2);
        saved.RiskMeasuredAt.Should().Be(measured, "no risk figure changed, so the measurement date carries forward");
    }

    [Fact]
    public async Task A_changed_risk_figure_in_an_ordinary_save_stamps_the_measurement()
    {
        _repo.Seed(Ips(drawdown: 25m, measuredAt: Now.AddDays(-200), tolerance: 3, capacity: 3));

        var saved = await new SaveIpsCommandHandler(_repo, new FixedTimeProvider(Now)).Handle(
            new SaveIpsCommand(_userId, [], 10, null, 3, 3, 20m, [], null, null, null, null, [], "quarterly"),
            default);

        saved.RiskMeasuredAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(0, 3, 10)]
    [InlineData(6, 3, 10)]
    [InlineData(3, 0, 10)]
    [InlineData(3, 6, 10)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 3, 101)]
    public async Task Out_of_range_answers_are_rejected_and_nothing_is_saved(int tolerance, int capacity, int drawdown)
    {
        _repo.Seed(Ips(drawdown: null, measuredAt: null));

        var act = () => Record(drawdown, tolerance, capacity);

        await act.Should().ThrowAsync<IpsValidationException>();
        _repo.Versions.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_a_policy_statement_there_is_nothing_to_remeasure()
    {
        var act = () => Record(25m, 3, 3);

        await act.Should().ThrowAsync<IpsValidationException>().WithMessage("*save_ips*");
    }

    [Fact]
    public async Task Diff_defaults_to_the_current_version_against_the_one_before_it()
    {
        _repo.Seed(Ips(drawdown: null, measuredAt: null));
        await Record(25m, 3, 3);

        var diff = await new GetIpsDiffQueryHandler(_repo).Handle(new GetIpsDiffQuery(_userId), default);

        diff!.FromVersion.Should().Be(1);
        diff.ToVersion.Should().Be(2);
        diff.Changes.Should().Contain(c => c.Field == "maxDrawdownTolerancePct");
    }

    [Fact]
    public async Task Diff_with_a_single_version_or_an_unknown_version_is_null()
    {
        _repo.Seed(Ips(drawdown: null, measuredAt: null));
        var handler = new GetIpsDiffQueryHandler(_repo);

        (await handler.Handle(new GetIpsDiffQuery(_userId), default)).Should().BeNull();
        (await handler.Handle(new GetIpsDiffQuery(_userId, 7, 1), default)).Should().BeNull();
    }

    [Fact]
    public void Diff_ignores_decimal_scale_so_a_database_round_trip_is_not_a_change()
    {
        var from = Ips(drawdown: 25m, measuredAt: null);
        var to = Ips(drawdown: 25.00m, measuredAt: null);

        IpsDiff.Compare(from, to).Should().BeEmpty();
    }

    [Fact]
    public async Task Risk_tolerance_reader_exposes_the_current_drawdown_in_whole_percent()
    {
        var dto = new IpsDto(
            Guid.NewGuid(), 2, true, [], 10, null, 3, 3, 25m, Now, [], RebalancingRule.Default, null, null, 90, [], "annual",
            null, Now, Now);
        var get = new Mock<IQueryHandler<GetIpsQuery, IpsDto?>>();
        get.Setup(g => g.Handle(new GetIpsQuery(_userId), It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var result = await new RiskToleranceReader(get.Object).GetCurrentAsync(_userId, default);

        result!.MaxDrawdownTolerancePct.Should().Be(25m);
    }

    [Fact]
    public async Task Review_status_reports_the_remeasurement_fields()
    {
        _repo.Seed(Ips(drawdown: null, measuredAt: null));
        var handler = new GetPolicyReviewStatusQueryHandler(
            _repo, Mock.Of<IPolicyReviewRepository>(), new FixedTimeProvider(Now));

        var status = await handler.Handle(new GetPolicyReviewStatusQuery(_userId), default);

        status.RiskRemeasurementDue.Should().BeTrue();
        status.DrawdownToleranceSet.Should().BeFalse();
        status.RiskMeasuredAt.Should().BeNull();
    }

    private Task<RiskRemeasurementDto> Record(decimal drawdown, int tolerance, int capacity)
    {
        var save = new SaveIpsCommandHandler(_repo, new FixedTimeProvider(Now));
        return new RecordRiskRemeasurementCommandHandler(_repo, save)
            .Handle(new RecordRiskRemeasurementCommand(_userId, tolerance, capacity, drawdown), default);
    }

    private InvestmentPolicyStatement Ips(decimal? drawdown, DateTimeOffset? measuredAt, int tolerance = 3, int? capacity = 3) => new()
    {
        UserId = _userId,
        Version = 1,
        IsCurrent = true,
        ReviewCadence = "quarterly",
        RiskTolerance = tolerance,
        RiskCapacity = capacity,
        MaxDrawdownTolerancePct = drawdown,
        RiskMeasuredAt = measuredAt,
        CreatedAt = Now.AddYears(-1),
    };

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class InMemoryIpsRepository : IIpsRepository
    {
        public List<InvestmentPolicyStatement> Versions { get; } = [];

        public void Seed(InvestmentPolicyStatement ips) => Versions.Add(ips);

        public Task<InvestmentPolicyStatement?> GetCurrentAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Versions.FirstOrDefault(v => v.UserId == userId && v.IsCurrent));

        public Task<IReadOnlyList<InvestmentPolicyStatement>> ListVersionsAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentPolicyStatement>>(
                Versions.Where(v => v.UserId == userId).OrderByDescending(v => v.Version).ToList());

        public Task<int> GetMaxVersionAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Versions.Where(v => v.UserId == userId).Select(v => v.Version).DefaultIfEmpty(0).Max());

        public Task AddVersionAsync(InvestmentPolicyStatement ips, CancellationToken ct = default)
        {
            foreach (var v in Versions.Where(v => v.UserId == ips.UserId))
                v.IsCurrent = false;
            Versions.Add(ips);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetUserIdsWithCurrentIpsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Versions.Where(v => v.IsCurrent).Select(v => v.UserId).Distinct().ToList());
    }
}
