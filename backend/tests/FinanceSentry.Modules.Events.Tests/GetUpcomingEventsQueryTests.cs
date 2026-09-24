namespace FinanceSentry.Modules.Events.Tests;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Events.Application.Queries;
using FinanceSentry.Modules.Events.Domain;
using FinanceSentry.Modules.Events.Domain.Exceptions;
using FinanceSentry.Modules.Events.Domain.Ports;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>Feature 049 US1: the calendar is the union of the sources, each isolated.</summary>
public sealed class GetUpcomingEventsQueryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 9, 22);
    private static readonly DateOnly To = new(2026, 10, 22);

    private readonly Mock<IBrokerageHoldingsReader> _brokerage = new();
    private readonly Mock<IWatchlistReader> _watchlist = new();
    private readonly Mock<IUpcomingCorporateEventReader> _corporate = new();
    private readonly Mock<IMacroEventReader> _macro = new();
    private readonly Mock<IThesisCatalystReader> _theses = new();
    private readonly Mock<IPeriodicFilingReader> _filings = new();

    public GetUpcomingEventsQueryTests()
    {
        _brokerage.Setup(b => b.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new BrokerageHoldingSummary("MU", "STK", 10, 1000, DateTime.UtcNow, "ibkr"),
                new BrokerageHoldingSummary("BTC", "CRYPTO", 1, 1000, DateTime.UtcNow, "binance"),
            ]);
        _watchlist.Setup(w => w.ListTickersAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(["PLTR", "mu"]);
        _corporate.Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CorporateCalendarEntry("MU", "earnings", new DateOnly(2026, 9, 24), true, "yahoo"),
                new CorporateCalendarEntry("PLTR", "ex_dividend", new DateOnly(2026, 10, 1), false, "yahoo"),
                new CorporateCalendarEntry("PLTR", "dividend", new DateOnly(2026, 10, 15), false, "yahoo"),
            ]);
        _macro.Setup(m => m.QueryAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MacroCalendarEntry(Guid.NewGuid(), new DateOnly(2026, 10, 15), new TimeOnly(8, 30), "CPI (Sep)", "US", "high", "bls")]);
        _theses.Setup(t => t.ListActiveAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ThesisCatalystEntry(Guid.NewGuid(), "MU", new DateOnly(2026, 10, 5), "HBM4 ramp update"),
                new ThesisCatalystEntry(Guid.NewGuid(), "MU", new DateOnly(2027, 1, 5), "outside the window"),
            ]);
        _filings.Setup(f => f.GetRecentAsync("MU", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PeriodicFiling("10-Q", new DateOnly(2026, 7, 1), new DateOnly(2026, 6, 30))]);
        _filings.Setup(f => f.GetRecentAsync("PLTR", It.IsAny<CancellationToken>())).ReturnsAsync([]);
    }

    private static DateOnly EndOfMonth(DateOnly date)
        => new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));

    private GetUpcomingEventsQueryHandler Handler() => new(
        _brokerage.Object, _watchlist.Object, _corporate.Object, _macro.Object, _theses.Object, _filings.Object,
        NullLogger<GetUpcomingEventsQueryHandler>.Instance);

    [Fact]
    public async Task Unions_every_source_sorted_by_date_and_reports_them_ok()
    {
        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, null), CancellationToken.None);

        result.Items.Select(i => (i.Kind, i.Subject, i.Date)).Should().Equal(
            (EventKind.Earnings, "MU", new DateOnly(2026, 9, 24)),
            (EventKind.ExDividend, "PLTR", new DateOnly(2026, 10, 1)),
            (EventKind.ThesisCatalyst, "MU", new DateOnly(2026, 10, 5)),
            (EventKind.Macro, "US", new DateOnly(2026, 10, 15)));
        result.Sources.Select(s => (s.Source, s.Status)).Should().BeEquivalentTo([
            (EventSource.Corporate, EventSourceStatus.Ok),
            (EventSource.Macro, EventSourceStatus.Ok),
            (EventSource.Theses, EventSourceStatus.Ok),
            (EventSource.Filings, EventSourceStatus.Ok),
        ]);
        result.Items.Should().NotContain(i => i.Title.Contains("dividend", StringComparison.Ordinal) && i.Kind != EventKind.ExDividend,
            "dividend-payment dates are not events");
    }

    [Fact]
    public async Task Ticker_universe_is_equity_holdings_plus_watchlist_deduped_case_insensitively()
    {
        IReadOnlyCollection<string>? seen = null;
        _corporate.Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), From, To, It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<string>, DateOnly, DateOnly, CancellationToken>((t, _, _, _) => seen = t)
            .ReturnsAsync([]);

        await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, [EventKind.Earnings]), CancellationToken.None);

        seen.Should().BeEquivalentTo(["MU", "PLTR"]);
    }

    [Fact]
    public async Task Filing_due_is_derived_per_ticker_and_flagged_as_estimate()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reportDate = EndOfMonth(today.AddMonths(-2));
        var expectedDue = EndOfMonth(reportDate.AddMonths(3)).AddDays(FilingDueCalculator.QuarterlyDeadlineDays);
        _filings.Setup(f => f.GetRecentAsync("MU", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PeriodicFiling("10-Q", reportDate.AddDays(1), reportDate)]);

        var result = await Handler().Handle(
            new GetUpcomingEventsQuery(UserId, today, today.AddDays(200), [EventKind.FilingDue]), CancellationToken.None);

        var due = result.Items.Should().ContainSingle().Subject;
        due.Kind.Should().Be(EventKind.FilingDue);
        due.Subject.Should().Be("MU");
        due.Date.Should().Be(expectedDue);
        due.IsEstimate.Should().BeTrue();
        due.Title.Should().Be("10-Q due: MU");
    }

    [Fact]
    public async Task A_throwing_source_is_reported_unavailable_and_the_others_still_return()
    {
        _corporate.Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), From, To, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("yahoo down"));

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, null), CancellationToken.None);

        result.Sources.Should().Contain(s => s.Source == EventSource.Corporate && s.Status == EventSourceStatus.Unavailable);
        result.Sources.Should().Contain(s => s.Source == EventSource.Macro && s.Status == EventSourceStatus.Ok);
        result.Items.Should().Contain(i => i.Kind == EventKind.Macro);
        result.Items.Should().Contain(i => i.Kind == EventKind.ThesisCatalyst);
        result.Items.Should().NotContain(i => i.Kind == EventKind.Earnings);
    }

    [Fact]
    public async Task Filings_unavailable_when_every_requested_ticker_fails()
    {
        _filings.Setup(f => f.GetRecentAsync("MU", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FilingReadFailedException("EDGAR down"));
        _filings.Setup(f => f.GetRecentAsync("PLTR", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FilingReadFailedException("EDGAR down"));

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, ["filing_due"]), CancellationToken.None);

        result.Sources.Should().Contain(s => s.Source == EventSource.Filings && s.Status == EventSourceStatus.Unavailable);
        result.Items.Should().NotContain(i => i.Kind == EventKind.FilingDue);
    }

    [Fact]
    public async Task Filings_stays_ok_when_only_some_tickers_fail()
    {
        _filings.Setup(f => f.GetRecentAsync("PLTR", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FilingReadFailedException("EDGAR down"));

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, ["filing_due"]), CancellationToken.None);

        result.Sources.Should().Contain(s => s.Source == EventSource.Filings && s.Status == EventSourceStatus.Ok);
    }

    [Fact]
    public async Task Kinds_filter_skips_the_sources_it_does_not_need()
    {
        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, ["macro"]), CancellationToken.None);

        result.Items.Should().OnlyContain(i => i.Kind == EventKind.Macro);
        result.Sources.Should().ContainSingle(s => s.Source == EventSource.Macro);
        _corporate.Verify(c => c.GetForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        _theses.Verify(t => t.ListActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _filings.Verify(f => f.GetRecentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _brokerage.Verify(b => b.GetHoldingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Default_window_is_today_plus_ninety_days()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _macro.Setup(m => m.QueryAsync(today, today.AddDays(90), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, null, null, ["macro"]), CancellationToken.None);

        result.From.Should().Be(today);
        result.To.Should().Be(today.AddDays(90));
        _macro.Verify(m => m.QueryAsync(today, today.AddDays(90), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("2026-09-22", "2026-09-21")]
    [InlineData("2026-01-01", "2027-01-03")]
    public async Task Invalid_window_is_rejected(string from, string to)
    {
        var act = () => Handler().Handle(
            new GetUpcomingEventsQuery(UserId, DateOnly.Parse(from), DateOnly.Parse(to), null), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EventsWindowInvalidException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.ErrorCode.Should().Be("EVENTS_WINDOW_INVALID");
    }

    [Fact]
    public async Task Unknown_kind_among_valid_ones_rejects_the_whole_request_and_names_it()
    {
        var act = () => Handler().Handle(
            new GetUpcomingEventsQuery(UserId, From, To, ["macro", "earning"]), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EventsKindsInvalidException>();
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.ErrorCode.Should().Be("EVENTS_KINDS_INVALID");
        ex.Which.Message.Should().Contain("earning");
        _macro.Verify(m => m.QueryAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Wholly_unknown_kinds_are_rejected()
    {
        var act = () => Handler().Handle(
            new GetUpcomingEventsQuery(UserId, From, To, ["filings", "dividend"]), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<EventsKindsInvalidException>();
        ex.Which.ErrorCode.Should().Be("EVENTS_KINDS_INVALID");
        ex.Which.Message.Should().Contain("filings").And.Contain("dividend");
    }

    [Fact]
    public async Task Duplicate_rows_from_a_source_collapse_to_one()
    {
        _corporate.Setup(c => c.GetForTickersAsync(It.IsAny<IReadOnlyCollection<string>>(), From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CorporateCalendarEntry("MU", "earnings", new DateOnly(2026, 9, 24), true, "yahoo"),
                new CorporateCalendarEntry("mu", "earnings", new DateOnly(2026, 9, 24), true, "yahoo"),
            ]);

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, ["earnings"]), CancellationToken.None);

        result.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Two_macro_rows_on_one_date_both_survive()
    {
        var day = new DateOnly(2026, 10, 15);
        _macro.Setup(m => m.QueryAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MacroCalendarEntry(Guid.NewGuid(), day, new TimeOnly(14, 0), "FOMC rate decision + SEP", "US", "high", "fed"),
                new MacroCalendarEntry(Guid.NewGuid(), day, new TimeOnly(8, 30), "CPI (Sep)", "US", "high", "bls"),
            ]);

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, ["macro"]), CancellationToken.None);

        result.Items.Select(i => i.Title).Should().Equal("CPI (Sep)", "FOMC rate decision + SEP");
    }

    [Fact]
    public async Task Two_catalysts_of_one_thesis_on_one_date_both_survive()
    {
        var thesisId = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 15);
        _theses.Setup(t => t.ListActiveAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ThesisCatalystEntry(thesisId, "MU", day, "Q3 earnings"),
                new ThesisCatalystEntry(thesisId, "MU", day, "HBM4 ramp update"),
            ]);

        var result = await Handler().Handle(new GetUpcomingEventsQuery(UserId, From, To, ["thesis_catalyst"]), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(i => i.ReferenceId == thesisId && i.Date == day);
        result.Items.Select(i => i.Detail).Should().BeEquivalentTo(["Q3 earnings", "HBM4 ramp update"]);
    }
}
