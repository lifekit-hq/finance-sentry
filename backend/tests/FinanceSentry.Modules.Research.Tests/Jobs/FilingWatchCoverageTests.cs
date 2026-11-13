namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>The filing detector reads coverage names for the coverage review alone: held names first, and no FilingLanded alert.</summary>
public sealed class FilingWatchCoverageTests : CoverageDetectorFixture
{
    private static readonly DateTime NowUtc = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 16);

    private readonly Mock<ISecEdgarService> _edgar = new();
    private readonly List<string> _fetchOrder = [];

    private FilingWatchJob Job(params string[] coverage)
        => new(Banking.Object, Universe(coverage), _edgar.Object, Alerts.Object, Raiser(), NullLogger<FilingWatchJob>.Instance);

    private void Filings(string ticker, params string[] accessions)
        => _edgar.Setup(e => e.GetRecentFilingsAsync(ticker, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), default))
            .Callback(() => _fetchOrder.Add(ticker))
            .ReturnsAsync([.. accessions.Select(a => new EdgarFiling(ticker, "8-K", Today, null, "8-K", a, $"https://sec.test/{a}", true))]);

    [Fact]
    public async Task A_filing_on_a_coverage_only_name_is_noted_for_the_review_without_a_filing_alert()
    {
        Filings("MU", "0001");

        await Job("MU").ExecuteAsync(NowUtc);

        Hits.Should().ContainSingle().Which.Should().Be(new CoverageHit("MU", ReviewDueSource.Filing, "0001", "8-K filed 2026-11-16"));
        ReviewerCalls.Should().Be(1);
        Alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Held_names_are_read_before_the_rest_of_coverage()
    {
        Hold("NVDA");
        Filings("AAPL");
        Filings("NVDA");
        Filings("MU");

        await Job("AAPL", "NVDA", "MU").ExecuteAsync(NowUtc);

        _fetchOrder.Should().Equal("NVDA", "AAPL", "MU");
    }

    [Fact]
    public async Task A_held_coverage_name_keeps_its_filing_alert_and_is_also_noted()
    {
        Hold("NVDA");
        Filings("NVDA", "0002");

        await Job("NVDA").ExecuteAsync(NowUtc);

        Alerts.Verify(a => a.GenerateFilingLandedAlertAsync(
            UserId, "NVDA", "8-K", Today, "0002", It.IsAny<string>(), default), Times.Once);
        Hits.Should().ContainSingle().Which.Ticker.Should().Be("NVDA");
    }

    [Fact]
    public async Task A_filing_on_a_name_outside_coverage_is_never_noted()
    {
        Hold("NVDA");
        Filings("NVDA", "0003");

        await Job("MU").ExecuteAsync(NowUtc);

        Hits.Should().BeEmpty();
        ReviewerCalls.Should().Be(0, "nothing was noted, so the reviewer is not called");
    }

    [Fact]
    public async Task Two_filings_in_one_pass_are_two_hits_and_one_review_call()
    {
        Filings("MU", "0004", "0005");

        await Job("MU").ExecuteAsync(NowUtc);

        Hits.Select(h => h.Reference).Should().Equal("0004", "0005");
        ReviewerCalls.Should().Be(1);
    }
}
