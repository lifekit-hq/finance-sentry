namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class CatalystFilingScannerTests
{
    private static readonly DateOnly From = new(2026, 11, 13);
    private static readonly DateOnly To = new(2026, 11, 16);
    private static readonly DateTimeOffset AsOf = new(2026, 11, 16, 0, 30, 0, TimeSpan.Zero);
    private static readonly IReadOnlySet<string> Universe = new HashSet<string> { "NVDA", "MU", "AVGO" };

    private readonly ScriptedFullText _fullText = new();
    private readonly Mock<ISecEdgarService> _edgar = new();
    private readonly AutoAdvanceClock _clock = new(AsOf);

    private CatalystFilingScanner Scanner(CatalystOptions? options = null)
    {
        var opts = options ?? new CatalystOptions();
        return new(_fullText, _edgar.Object, RequestPacer.PerSecond(opts.SecRequestsPerSecond, _clock), Options.Create(opts), NullLogger<CatalystFilingScanner>.Instance);
    }

    private static EdgarFiling Submission(string accession, string items, DateOnly filed, string form = "8-K")
        => new("X", form, filed, null, "8-K", accession, $"https://sec.test/{accession}", true, CatalystRules.ParseItems([items]));

    [Fact]
    public async Task FullText_KeepsOnlyTheFourWatchedItems_OnUniverseNames_Inside_TheWindow()
    {
        _fullText.ByItem["2.02"] =
        [
            Filings.Of("NVDA", "A-1", "2.02,9.01", new DateOnly(2026, 11, 13)),
            Filings.Of("ZZZZ", "A-2", "2.02", new DateOnly(2026, 11, 13)),
            Filings.Of("MU", "A-3", "9.01", new DateOnly(2026, 11, 13)),
            Filings.Of("MU", "A-4", "2.03,7.01", new DateOnly(2026, 11, 13)),
            Filings.Of("AVGO", "A-5", "2.02", new DateOnly(2026, 11, 12)),
            Filings.Of("AVGO", "A-6", "2.02", new DateOnly(2026, 11, 16), form: "8-K/A"),
        ];
        _fullText.ByItem["1.01"] = [Filings.Of("MU", "B-1", "1.01", new DateOnly(2026, 11, 14))];

        var scan = await Scanner().ScanAsync(Universe, From, To, AsOf);

        scan.Source.Should().Be(CatalystRules.SourceFullText);
        scan.FallbackReason.Should().BeNull();
        scan.Filings.Select(f => (f.Ticker, f.Accession)).Should().Equal(("MU", "B-1"), ("NVDA", "A-1"));
        _fullText.Calls.Select(c => c.Item).Should().Equal("1.01", "2.02", "5.02", "8.01");
        _fullText.Calls.Should().OnlyContain(c => c.From == From && c.To == To);
        _edgar.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FullText_AFilingFoundUnderTwoItems_IsOneFiling()
    {
        var both = Filings.Of("NVDA", "A-1", "1.01,2.02", new DateOnly(2026, 11, 13));
        _fullText.ByItem["1.01"] = [both];
        _fullText.ByItem["2.02"] = [both];

        var scan = await Scanner().ScanAsync(Universe, From, To, AsOf);

        scan.Filings.Should().ContainSingle();
    }

    [Fact]
    public async Task Items_AreConfigurable()
    {
        _fullText.ByItem["2.02"] = [Filings.Of("NVDA", "A-1", "2.02", new DateOnly(2026, 11, 13))];

        var scan = await Scanner(new CatalystOptions { Items = ["1.01"] }).ScanAsync(Universe, From, To, AsOf);

        scan.Filings.Should().BeEmpty();
        _fullText.Calls.Select(c => c.Item).Should().Equal("1.01");
    }

    [Fact]
    public async Task WhenFullTextFails_TheSubmissionsFeedIsTheFallback_FilteredTheSameWay()
    {
        _fullText.Fail = "EDGAR full-text search answered HTTP 403 for item 1.01.";
        _edgar.Setup(e => e.GetRecentFilingsAsync("NVDA", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([
                Submission("N-1", "2.02,9.01", new DateOnly(2026, 11, 13)),
                Submission("N-2", "9.01", new DateOnly(2026, 11, 13)),
                Submission("N-3", "1.01", new DateOnly(2026, 10, 1)),
            ]);
        _edgar.Setup(e => e.GetRecentFilingsAsync("MU", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([Submission("M-1", "8.01", new DateOnly(2026, 11, 14))]);
        _edgar.Setup(e => e.GetRecentFilingsAsync("AVGO", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([]);

        var scan = await Scanner().ScanAsync(Universe, From, To, AsOf);

        scan.Source.Should().Be(CatalystRules.SourceSubmissions);
        scan.FallbackReason.Should().Contain("HTTP 403");
        scan.TickersRead.Should().Be(3);
        scan.Filings.Select(f => (f.Ticker, f.Accession, f.Source)).Should().Equal(
            ("MU", "M-1", CatalystRules.SourceSubmissions), ("NVDA", "N-1", CatalystRules.SourceSubmissions));
        _edgar.Verify(e => e.GetRecentFilingsAsync(
            It.IsAny<string>(), It.Is<IReadOnlyCollection<string>>(f => f.Single() == "8-K"), It.IsAny<int>(), It.IsAny<CancellationToken>(), true), Times.Exactly(3));
    }

    [Fact]
    public async Task Fallback_IsPacedBelowTheSecCeiling()
    {
        _fullText.Fail = "down";
        _edgar.Setup(e => e.GetRecentFilingsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([]);
        var universe = new HashSet<string>(Enumerable.Range(0, 11).Select(i => $"T{i:00}"));

        await Scanner().ScanAsync(universe, From, To, AsOf);

        _edgar.Invocations.Should().HaveCount(11);
        _clock.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(10 * 200), "11 requests at 5 a second need ten 200 ms gaps");
    }

    [Fact]
    public async Task Fallback_ReadsABoundedRotatingSlice_SoEveryNameIsReachedOnLaterRuns()
    {
        _fullText.Fail = "down";
        var read = new List<string>();
        _edgar.Setup(e => e.GetRecentFilingsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .Callback<string, IReadOnlyCollection<string>?, int, CancellationToken, bool>((t, _, _, _, _) => read.Add(t))
            .ReturnsAsync([]);
        var universe = new HashSet<string>(Enumerable.Range(0, 5).Select(i => $"T{i}"));
        var scanner = Scanner(new CatalystOptions { FallbackMaxTickers = 3 });

        await scanner.ScanAsync(universe, From, To, AsOf);
        var day1 = read.ToList();
        read.Clear();
        await scanner.ScanAsync(universe, From, To, AsOf.AddDays(1));

        day1.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(3);
        read.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(3);
        day1.Should().NotIntersectWith(read);
        day1.Concat(read).Should().BeEquivalentTo(universe);
    }

    [Fact]
    public async Task Fallback_ASingleTickerFailing_IsSkipped_ButNothingReadableAtAll_FailsTheRun()
    {
        _fullText.Fail = "down";
        _edgar.Setup(e => e.GetRecentFilingsAsync("MU", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ThrowsAsync(new EdgarProviderException("boom"));
        _edgar.Setup(e => e.GetRecentFilingsAsync("NVDA", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([Submission("N-1", "2.02", new DateOnly(2026, 11, 13))]);

        var partial = await Scanner().ScanAsync(new HashSet<string> { "MU", "NVDA" }, From, To, AsOf);
        partial.Filings.Should().ContainSingle();

        var allDown = () => Scanner().ScanAsync(new HashSet<string> { "MU" }, From, To, AsOf);
        await allDown.Should().ThrowAsync<CatalystFeedException>().WithMessage("*Neither EDGAR feed*");
    }
}
