namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// Earnings inside the look-ahead window on a coverage name is an event for the coverage review; it raises no
/// EarningsAhead alert unless the name is in the alert universe, and no review notice.
/// </summary>
public sealed class EarningsAheadCoverageTests : CoverageDetectorFixture
{
    private static readonly DateTime NowUtc = new(2026, 11, 16, 6, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly DateOnly Reports = new(2026, 11, 18);

    private readonly Mock<IEarningsCalendarService> _calendar = new();
    private IReadOnlyCollection<string>? _asked;

    public EarningsAheadCoverageTests() => Calendar();

    private void Calendar(params EarningsEvent[] ahead)
    {
        _calendar.Setup(c => c.GetForTickersAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), null, default, false))
            .Callback<IReadOnlyCollection<string>, DateOnly, DateOnly, string?, CancellationToken, bool>(
                (tickers, from, _, _, _, _) =>
                {
                    if (from == Today)
                    {
                        _asked = tickers;
                    }
                })
            .ReturnsAsync((IReadOnlyCollection<string> _, DateOnly from, DateOnly _, string? _, CancellationToken _, bool _) =>
                from == Today ? ahead : []);
    }

    private EarningsAheadJob Job(params string[] coverage)
        => new(Banking.Object, Universe(coverage), _calendar.Object, Alerts.Object, Raiser(), NullLogger<EarningsAheadJob>.Instance);

    [Fact]
    public async Task A_coverage_only_name_is_looked_up_and_noted_without_an_earnings_alert_or_review_notice()
    {
        Calendar(new EarningsEvent("MU", EarningsEventType.Earnings, Reports, false, "yahoo"));

        await Job("MU").ExecuteAsync(NowUtc);

        _asked.Should().BeEquivalentTo(["MU"]);
        Hits.Should().ContainSingle().Which.Should().Be(
            new CoverageHit("MU", ReviewDueSource.Earnings, "ahead:MU:2026-11-18", "earnings 2026-11-18 ahead"));
        Alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<bool>(), default), Times.Never);
        Reviews.Should().BeEmpty();
    }

    [Fact]
    public async Task A_held_coverage_name_keeps_its_alert_and_is_also_noted()
    {
        Hold("NVDA");
        Calendar(new EarningsEvent("NVDA", EarningsEventType.Earnings, Reports, false, "yahoo"));

        await Job("NVDA", "MU").ExecuteAsync(NowUtc);

        _asked.Should().BeEquivalentTo(["NVDA", "MU"]);
        Alerts.Verify(a => a.GenerateEarningsAheadAlertAsync(
            UserId, "NVDA", EarningsAheadEventType.Earnings, Reports, false, default), Times.Once);
        Hits.Should().ContainSingle().Which.Ticker.Should().Be("NVDA");
    }

    [Fact]
    public async Task An_ex_dividend_date_on_a_coverage_name_is_not_an_event()
    {
        Calendar(new EarningsEvent("MU", EarningsEventType.ExDividend, Reports, false, "yahoo"));

        await Job("MU").ExecuteAsync(NowUtc);

        Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_coverage_the_lookup_is_exactly_the_alert_universe()
    {
        Hold("NVDA");

        await Job().ExecuteAsync(NowUtc);

        _asked.Should().BeEquivalentTo(["NVDA"]);
    }

    [Fact]
    public async Task An_empty_alert_universe_still_reads_coverage_and_skips_the_alert_resolve_pass()
    {
        Calendar(new EarningsEvent("MU", EarningsEventType.Earnings, Reports, false, "yahoo"));

        await Job("MU").ExecuteAsync(NowUtc);

        _calendar.Verify(c => c.GetForTickersAsync(
            It.IsAny<IReadOnlyCollection<string>>(), Today.AddDays(-5), Today.AddDays(-1), null, default, false), Times.Never);
    }
}
