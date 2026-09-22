namespace FinanceSentry.Tests.Integration.CrossModulePorts;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Integration;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>049: the Research-facing adapters translate without filtering by date - that is the handler's job.</summary>
public sealed class EventsResearchAdapterTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Thesis_catalysts_come_from_unbroken_theses_only()
    {
        var live = new InvestmentThesis { UserId = User, Ticker = "MU", Catalysts = [new ThesisCatalyst(new DateOnly(2026, 10, 5), "HBM4 ramp")] };
        var broken = new InvestmentThesis { UserId = User, Ticker = "PLTR", BrokenAt = DateTimeOffset.UtcNow, Catalysts = [new ThesisCatalyst(new DateOnly(2026, 10, 6), "gone")] };
        var theses = new Mock<IThesisRepository>();
        theses.Setup(t => t.ListAsync(User, It.IsAny<CancellationToken>())).ReturnsAsync([live, broken]);

        var rows = await new EventsThesisCatalystAdapter(theses.Object).ListActiveAsync(User);

        var row = rows.Should().ContainSingle().Subject;
        row.ThesisId.Should().Be(live.Id);
        row.Ticker.Should().Be("MU");
        row.Date.Should().Be(new DateOnly(2026, 10, 5));
        row.Event.Should().Be("HBM4 ramp");
    }

    [Fact]
    public async Task Periodic_filings_keep_only_rows_with_a_period_end()
    {
        var edgar = new Mock<ISecEdgarService>();
        edgar.Setup(e => e.GetRecentFilingsAsync("MU", It.Is<IReadOnlyCollection<string>>(f => f.Contains("10-K") && f.Contains("10-Q")), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new EdgarFiling("MU", "10-Q", new DateOnly(2026, 7, 1), new DateOnly(2026, 5, 28), "q", "0001-26-1", "https://sec/1", true),
                new EdgarFiling("MU", "10-K", new DateOnly(2025, 10, 10), null, "k", "0001-25-1", "https://sec/2", true),
            ]);

        var rows = await new EventsPeriodicFilingAdapter(edgar.Object).GetRecentAsync("MU");

        var row = rows.Should().ContainSingle().Subject;
        row.Form.Should().Be("10-Q");
        row.ReportDate.Should().Be(new DateOnly(2026, 5, 28));
    }

    [Fact]
    public async Task Corporate_calendar_passes_the_ticker_set_explicitly()
    {
        GetEarningsCalendarQuery? captured = null;
        var handler = new Mock<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>();
        handler.Setup(h => h.Handle(It.IsAny<GetEarningsCalendarQuery>(), It.IsAny<CancellationToken>()))
            .Callback<GetEarningsCalendarQuery, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync([new EarningsEventDto("MU", "earnings", new DateOnly(2026, 9, 24), true, "yahoo")]);

        var rows = await new EventsCorporateCalendarAdapter(handler.Object)
            .GetForTickersAsync(["MU"], new DateOnly(2026, 9, 22), new DateOnly(2026, 10, 22));

        captured!.Tickers.Should().BeEquivalentTo(["MU"]);
        captured.UserId.Should().BeNull();
        rows.Should().ContainSingle().Which.EventType.Should().Be("earnings");
    }

    [Fact]
    public async Task Corporate_calendar_with_no_tickers_never_calls_the_provider()
    {
        var handler = new Mock<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>();

        var rows = await new EventsCorporateCalendarAdapter(handler.Object)
            .GetForTickersAsync([], new DateOnly(2026, 9, 22), new DateOnly(2026, 10, 22));

        rows.Should().BeEmpty();
        handler.Verify(h => h.Handle(It.IsAny<GetEarningsCalendarQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
