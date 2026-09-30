namespace FinanceSentry.Modules.Research.Tests.PolicyReviews;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.PolicyReviews;
using FluentAssertions;
using Xunit;

/// <summary>Cadence parsing and due / not-due / missed evaluation for the scheduled policy review (#696).</summary>
public class PolicyReviewScheduleTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("monthly", 1)]
    [InlineData("quarterly", 3)]
    [InlineData(" Quarterly ", 3)]
    [InlineData("semi-annual", 6)]
    [InlineData("Semiannual", 6)]
    [InlineData("annual", 12)]
    [InlineData("yearly", 12)]
    public void Recognised_cadences_map_to_months(string cadence, int months)
    {
        PolicyReviewCadence.TryGetMonths(cadence, out var actual).Should().BeTrue();
        actual.Should().Be(months);
    }

    [Theory]
    [InlineData("whenever")]
    [InlineData("")]
    [InlineData(null)]
    public void Unrecognised_cadence_falls_back_to_annual(string? cadence)
    {
        PolicyReviewCadence.TryGetMonths(cadence, out var months).Should().BeFalse();
        months.Should().Be(12);
    }

    [Fact]
    public void Never_reviewed_statement_is_first_due_one_cadence_after_it_was_written()
    {
        var ips = Ips(createdAt: Jan1, cadence: "quarterly");

        var status = PolicyReviewSchedule.Evaluate([ips], Jan1.AddMonths(2));

        status.IsDue.Should().BeFalse();
        status.DueAt.Should().Be(Jan1.AddMonths(3));
        status.LastReviewedAt.Should().BeNull();
        status.DaysOverdue.Should().Be(0);
        status.IsMissed.Should().BeFalse();
    }

    [Fact]
    public void Review_is_due_once_the_cadence_has_elapsed_since_the_last_review()
    {
        var ips = Ips(createdAt: Jan1.AddYears(-1), lastReviewedAt: Jan1, cadence: "quarterly");

        var status = PolicyReviewSchedule.Evaluate([ips], Jan1.AddMonths(3));

        status.IsDue.Should().BeTrue();
        status.DueAt.Should().Be(Jan1.AddMonths(3));
        status.LastReviewedAt.Should().Be(Jan1);
        status.IsMissed.Should().BeFalse();
    }

    [Fact]
    public void Review_is_not_due_a_moment_before_the_cadence_elapses()
    {
        var ips = Ips(createdAt: Jan1.AddYears(-1), lastReviewedAt: Jan1, cadence: "annual");

        var status = PolicyReviewSchedule.Evaluate([ips], Jan1.AddYears(1).AddSeconds(-1));

        status.IsDue.Should().BeFalse();
    }

    [Fact]
    public void Review_opened_within_the_grace_period_is_late_but_not_missed()
    {
        var ips = Ips(createdAt: Jan1.AddYears(-1), lastReviewedAt: Jan1, cadence: "monthly");

        var status = PolicyReviewSchedule.Evaluate([ips], Jan1.AddMonths(1).AddDays(1));

        status.IsDue.Should().BeTrue();
        status.DaysOverdue.Should().Be(1);
        status.IsMissed.Should().BeFalse();
    }

    [Fact]
    public void Review_still_unopened_past_the_grace_period_is_missed()
    {
        var ips = Ips(createdAt: Jan1.AddYears(-1), lastReviewedAt: Jan1, cadence: "monthly");

        var status = PolicyReviewSchedule.Evaluate([ips], Jan1.AddMonths(1).AddDays(10));

        status.IsDue.Should().BeTrue();
        status.DaysOverdue.Should().Be(10);
        status.IsMissed.Should().BeTrue();
    }

    [Fact]
    public void Saving_a_new_version_does_not_restart_the_review_clock()
    {
        var reviewedV1 = Ips(version: 1, isCurrent: false, createdAt: Jan1.AddYears(-1), lastReviewedAt: Jan1);
        var currentV2 = Ips(version: 2, createdAt: Jan1.AddMonths(2), cadence: "quarterly");

        var status = PolicyReviewSchedule.Evaluate([currentV2, reviewedV1], Jan1.AddMonths(3));

        status.Cadence.Should().Be("quarterly", "the cadence comes from the current version");
        status.DueAt.Should().Be(Jan1.AddMonths(3), "the anchor is the last review on any version");
        status.IsDue.Should().BeTrue();
    }

    [Fact]
    public void Never_reviewed_history_is_anchored_on_the_first_version()
    {
        var v1 = Ips(version: 1, isCurrent: false, createdAt: Jan1);
        var v2 = Ips(version: 2, createdAt: Jan1.AddMonths(6), cadence: "annual");

        var status = PolicyReviewSchedule.Evaluate([v2, v1], Jan1.AddMonths(12));

        status.DueAt.Should().Be(Jan1.AddYears(1));
        status.IsDue.Should().BeTrue();
    }

    [Fact]
    public void Unrecognised_cadence_is_flagged_and_reviewed_annually()
    {
        var ips = Ips(createdAt: Jan1, cadence: "when it feels right");

        var status = PolicyReviewSchedule.Evaluate([ips], Jan1);

        status.CadenceRecognised.Should().BeFalse();
        status.CadenceMonths.Should().Be(12);
        status.DueAt.Should().Be(Jan1.AddYears(1));
    }

    [Fact]
    public void No_versions_is_rejected()
    {
        var act = () => PolicyReviewSchedule.Evaluate([], Jan1);

        act.Should().Throw<ArgumentException>();
    }

    internal static InvestmentPolicyStatement Ips(
        int version = 1,
        bool isCurrent = true,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastReviewedAt = null,
        string cadence = "annual") => new()
        {
            Version = version,
            IsCurrent = isCurrent,
            CreatedAt = createdAt ?? Jan1,
            UpdatedAt = createdAt ?? Jan1,
            LastReviewedAt = lastReviewedAt,
            ReviewCadence = cadence,
        };
}
