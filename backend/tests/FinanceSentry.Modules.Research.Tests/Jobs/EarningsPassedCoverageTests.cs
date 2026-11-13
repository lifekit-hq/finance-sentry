namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>Earnings that just passed on a coverage name are the event the coverage review acts on.</summary>
public sealed class EarningsPassedCoverageTests : CoverageDetectorFixture
{
    private static readonly DateTime NowUtc = new(2026, 11, 16, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 16);

    private readonly Mock<IEarningsCalendarService> _calendar = new();

    private EarningsPassedJob Job(params string[] coverage)
        => new(Banking.Object, Crypto.Object, Universe(coverage), _calendar.Object, Raiser(), NullLogger<EarningsPassedJob>.Instance);

    [Fact]
    public async Task A_passed_date_on_a_coverage_only_name_is_noted_and_raises_no_review_notice()
    {
        _calendar.Setup(c => c.GetForTickersAsync(
                It.Is<IReadOnlyCollection<string>>(t => t.Contains("MU")), Today.AddDays(-5), Today.AddDays(-1),
                EarningsEventType.Earnings, default, false))
            .ReturnsAsync([new EarningsEvent("MU", EarningsEventType.Earnings, new DateOnly(2026, 11, 12), false, "yahoo")]);

        await Job("MU").ExecuteAsync(NowUtc);

        Hits.Should().ContainSingle().Which.Should().Be(
            new CoverageHit("MU", ReviewDueSource.Earnings, "MU:2026-11-12", "earnings date 2026-11-12 has passed"));
        Reviews.Should().BeEmpty("a coverage name with no thesis or candidate has nothing to review");
    }
}
