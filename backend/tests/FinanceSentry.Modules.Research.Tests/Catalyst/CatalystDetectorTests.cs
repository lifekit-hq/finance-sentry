namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Discovery funnel stage 7: the daily 8-K scan, end to end over a real in-memory store.</summary>
public sealed class CatalystDetectorTests
{
    private static readonly DateOnly Friday = new(2026, 11, 13);

    private readonly CatalystHarness _h = new();

    private async Task<CatalystHarness> WithUniverseAsync()
    {
        await _h.ListAsync("sp500", "NVDA", "MU", "AVGO", "AAPL");
        await _h.ListAsync("theme:memory", "MU", "SNDK");
        return _h;
    }

    [Fact]
    public async Task ANameOutsideCoverage_IsNominatedIntoTheCandidatePath_TaggedAsACatalystNomination()
    {
        await WithUniverseAsync();
        _h.Filed("2.02", Filings.Of("AVGO", "0001-26-000001", "2.02,9.01", Friday));

        var summary = await _h.Detector.RunAsync();

        summary.Nominated.Should().Be(1);
        var command = _h.Nominations.Should().ContainSingle().Subject;
        command.Ticker.Should().Be("AVGO");
        command.UserId.Should().Be(CatalystHarness.UserId);
        command.Source.Should().Be(CandidateSource.Scan, "the existing candidate path, not a new one");
        command.NominationReasons.Should().Equal("catalyst: 8-K item 2.02 (results of operations)");
        command.DecisionNote.Should().BeNull();

        var row = (await _h.EventsAsync()).Should().ContainSingle().Subject;
        row.Ticker.Should().Be("AVGO");
        row.Disposition.Should().Be(CatalystDisposition.Nominated);
        row.Items.Should().Be("2.02");
        row.Tag.Should().Be(CatalystRules.Tag("2.02"));
        row.FilingDate.Should().Be(Friday);
        row.Summary.Should().Be("8-K item 2.02 filed 2026-11-13");
        row.Source.Should().Be(CatalystRules.SourceFullText);
        row.DetectedAt.Should().Be(CatalystHarness.Now);
    }

    [Fact]
    public async Task NoCoverageMemberIsAdded_ByTheScan_ItselfOnlyTheStageOneRulesDecideThat()
    {
        await WithUniverseAsync();
        _h.Filed("1.01", Filings.Of("AVGO", "0001-26-000001", "1.01", Friday));

        await _h.Detector.RunAsync();

        _h.Coverage.Db.CoverageMembers.Should().BeEmpty();
        _h.Coverage.Db.CoverageJournal.Should().BeEmpty();
    }

    [Fact]
    public async Task OnlyTheWatchedItems_OnUniverseNames_AreActedOn()
    {
        await WithUniverseAsync();
        _h.Filed("2.02",
            Filings.Of("AVGO", "A-1", "2.02", Friday),
            Filings.Of("OUTSIDE", "A-2", "2.02", Friday),
            Filings.Of("AAPL", "A-3", "9.01,7.01", Friday));

        await _h.Detector.RunAsync();

        _h.Nominations.Select(n => n.Ticker).Should().Equal("AVGO");
        (await _h.EventsAsync()).Select(e => e.Ticker).Should().Equal("AVGO");
    }

    [Fact]
    public async Task TheUniverse_IsTheConfiguredListKinds_NotEverySectorList()
    {
        await _h.ListAsync("sp500", "AVGO");
        await _h.ListAsync("sector:Information Technology", "MICRO");
        await _h.ListAsync("theme:power", "VST");
        _h.Filed("2.02",
            Filings.Of("AVGO", "A-1", "2.02", Friday),
            Filings.Of("MICRO", "A-2", "2.02", Friday),
            Filings.Of("VST", "A-3", "2.02", Friday));

        await _h.Detector.RunAsync();

        _h.Nominations.Select(n => n.Ticker).Should().BeEquivalentTo("AVGO", "VST");
    }

    [Fact]
    public async Task ARepeatRun_NominatesNothingTwice_AndWritesNoSecondRow()
    {
        await WithUniverseAsync();
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));
        _h.Filed("1.01", Filings.Of("MU", "A-2", "1.01", Friday));

        await _h.Detector.RunAsync();
        var second = await _h.Detector.RunAsync();

        _h.Nominations.Should().HaveCount(2);
        second.Nominated.Should().Be(0);
        (await _h.EventsAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task ANewFiling_OnAnAlreadyHandledName_IsAFreshCatalyst()
    {
        await WithUniverseAsync();
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));
        await _h.Detector.RunAsync();

        _h.Filed("1.01", Filings.Of("AVGO", "A-9", "1.01", CatalystHarness.Today));
        await _h.Detector.RunAsync();

        _h.Nominations.Should().HaveCount(2);
        _h.Nominations[1].NominationReasons.Should().Equal("catalyst: 8-K item 1.01 (material definitive agreement)");
        (await _h.EventsAsync()).Select(e => e.Accession).Should().Equal("A-1", "A-9");
    }

    [Fact]
    public async Task TwoFilingsOnOneName_AreOneNomination_BothRecorded()
    {
        await WithUniverseAsync();
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));
        _h.Filed("8.01", Filings.Of("AVGO", "A-2", "8.01", Friday));

        var summary = await _h.Detector.RunAsync();

        summary.Nominated.Should().Be(1);
        _h.Nominations.Should().ContainSingle();
        (await _h.EventsAsync()).Should().HaveCount(2).And.OnlyContain(e => e.Disposition == CatalystDisposition.Nominated);
    }

    [Fact]
    public async Task APerRunCap_BoundsNominations_AndTheRestWaitForTheNextRun()
    {
        var h = new CatalystHarness(new CatalystOptions { MaxNominationsPerRun = 2 });
        await h.ListAsync("sp500", "AAA", "BBB", "CCC", "DDD");
        h.Filed("2.02",
            Filings.Of("AAA", "A-1", "2.02", Friday),
            Filings.Of("BBB", "A-2", "2.02", Friday),
            Filings.Of("CCC", "A-3", "2.02", Friday),
            Filings.Of("DDD", "A-4", "2.02", Friday));

        var first = await h.Detector.RunAsync();
        first.Nominated.Should().Be(2);
        first.Deferred.Should().Be(2);
        h.Nominations.Select(n => n.Ticker).Should().Equal("AAA", "BBB");

        var second = await h.Detector.RunAsync();
        second.Nominated.Should().Be(2);
        second.Deferred.Should().Be(0);
        h.Nominations.Select(n => n.Ticker).Should().Equal("AAA", "BBB", "CCC", "DDD");
        (await h.EventsAsync()).Should().HaveCount(4);
    }

    [Fact]
    public async Task WhenTheCapBinds_TheMostWantedItemWins()
    {
        var h = new CatalystHarness(new CatalystOptions { MaxNominationsPerRun = 1 });
        await h.ListAsync("sp500", "AAA", "BBB");
        h.Filed("8.01", Filings.Of("AAA", "A-1", "8.01", Friday));
        h.Filed("1.01", Filings.Of("BBB", "A-2", "1.01", Friday));

        await h.Detector.RunAsync();

        h.Nominations.Select(n => n.Ticker).Should().Equal("BBB");
    }

    [Fact]
    public async Task ACoveredName_GoesToTheStageFourEventReview_NotToANomination()
    {
        await WithUniverseAsync();
        await _h.Coverage.MemberAsync("MU");
        _h.Filed("2.02", Filings.Of("MU", "0001-26-000007", "2.02", Friday));

        var summary = await _h.Detector.RunAsync();

        summary.Covered.Should().Be(1);
        _h.Nominations.Should().BeEmpty();
        var hit = _h.Hits.Should().ContainSingle().Subject;
        hit.Ticker.Should().Be("MU");
        hit.Source.Should().Be("filing", "the same event identity the hourly filing watch uses, so the two never double-count");
        hit.Reference.Should().Be("0001-26-000007");
        hit.What.Should().Be("8-K item 2.02 filed 2026-11-13");
        var row = (await _h.EventsAsync()).Should().ContainSingle().Subject;
        row.Disposition.Should().Be(CatalystDisposition.Covered);
        row.Tag.Should().BeNull();
    }

    [Fact]
    public async Task ARemovedName_IsNotReAdded_ByAFilingThatIsNoNewerThanTheRemoval()
    {
        await WithUniverseAsync();
        await _h.RemovedAsync("AVGO", new DateTimeOffset(2026, 11, 14, 1, 10, 0, TimeSpan.Zero));
        _h.Filed("2.02",
            Filings.Of("AVGO", "OLD-1", "2.02", new DateOnly(2026, 11, 13)),
            Filings.Of("AVGO", "OLD-2", "1.01", new DateOnly(2026, 11, 14)));

        var summary = await _h.Detector.RunAsync();

        summary.SkippedRemoved.Should().Be(2);
        _h.Nominations.Should().BeEmpty();
        (await _h.EventsAsync()).Should().OnlyContain(e => e.Disposition == CatalystDisposition.SkippedRemoved);
    }

    [Fact]
    public async Task ARemovedName_ComesBack_OnAFilingDatedAfterTheRemoval()
    {
        await WithUniverseAsync();
        await _h.RemovedAsync("AVGO", new DateTimeOffset(2026, 11, 14, 1, 10, 0, TimeSpan.Zero));
        _h.Filed("2.02", Filings.Of("AVGO", "NEW-1", "2.02", CatalystHarness.Today));

        var summary = await _h.Detector.RunAsync();

        summary.Nominated.Should().Be(1);
        _h.Nominations.Should().ContainSingle().Which.Ticker.Should().Be("AVGO");
    }

    [Fact]
    public async Task TheOffSwitch_StopsTheScan_BeforeAnyRequest()
    {
        var h = new CatalystHarness(new CatalystOptions { Enabled = false });
        await h.ListAsync("sp500", "AVGO");
        h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));

        var summary = await h.Detector.RunAsync();

        summary.Ran.Should().BeFalse();
        h.FullText.Calls.Should().BeEmpty();
        h.Nominations.Should().BeEmpty();
        (await h.EventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task AnEmptyReferenceUniverse_IsASkip_NotAQuietDay()
    {
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));

        var summary = await _h.Detector.RunAsync();

        summary.Universe.Should().Be(0);
        _h.FullText.Calls.Should().BeEmpty();
        _h.Nominations.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenFullTextIsDown_TheSubmissionsFallbackStillNominates()
    {
        await WithUniverseAsync();
        _h.FullText.Fail = "EDGAR full-text search answered HTTP 403 for item 1.01.";
        _h.Edgar.Setup(e => e.GetRecentFilingsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([]);
        _h.Edgar.Setup(e => e.GetRecentFilingsAsync("AVGO", It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), true))
            .ReturnsAsync([new FinanceSentry.Modules.Research.Domain.EdgarFiling(
                "AVGO", "8-K", Friday, null, "8-K", "FB-1", "https://sec.test/FB-1", true, ["5.02"])]);

        var summary = await _h.Detector.RunAsync();

        summary.Source.Should().Be(CatalystRules.SourceSubmissions);
        summary.FallbackReason.Should().Contain("HTTP 403");
        _h.Nominations.Should().ContainSingle().Which.NominationReasons.Should().Equal("catalyst: 8-K item 5.02 (officer or director change)");
        (await _h.EventsAsync()).Should().ContainSingle().Which.Source.Should().Be(CatalystRules.SourceSubmissions);
    }

    [Fact]
    public async Task ANominationThatFailsToScore_IsNotRecorded_SoTheNextRunRetriesIt()
    {
        await WithUniverseAsync();
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));
        _h.Scorer.Setup(s => s.Handle(It.IsAny<FinanceSentry.Modules.Research.Application.Commands.ScoreCandidateCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("market data down"));

        var failed = await _h.Detector.RunAsync();
        failed.Errors.Should().Be(1);
        (await _h.EventsAsync()).Should().BeEmpty();

        _h.Scorer.Setup(s => s.Handle(It.IsAny<FinanceSentry.Modules.Research.Application.Commands.ScoreCandidateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FinanceSentry.Modules.Research.Application.Commands.ScoreCandidateResult(Guid.NewGuid(), "AVGO", true, null!));
        var retried = await _h.Detector.RunAsync();

        retried.Nominated.Should().Be(1);
        (await _h.EventsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task EveryUserIsHandledSeparately_AFilingIsOnceEverPerUser()
    {
        await WithUniverseAsync();
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        _h.Coverage.Ips.Setup(i => i.GetUserIdsWithCurrentIpsUnscopedAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([CatalystHarness.UserId, other]);
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));

        await _h.Detector.RunAsync();
        await _h.Detector.RunAsync();

        _h.Nominations.Select(n => n.UserId).Should().BeEquivalentTo([CatalystHarness.UserId, other]);
        (await _h.EventsAsync()).Select(e => e.UserId).Should().BeEquivalentTo([CatalystHarness.UserId, other]);
    }

    [Fact]
    public async Task EventsOlderThanTheRetention_ArePruned()
    {
        await WithUniverseAsync();
        _h.Coverage.Db.CatalystEvents.Add(new CatalystEvent
        {
            UserId = CatalystHarness.UserId, Ticker = "OLD", Accession = "O-1", Form = "8-K", Items = "2.02", Summary = "s", Source = "fulltext",
            FilingDate = Friday.AddDays(-200), DetectedAt = CatalystHarness.Now.AddDays(-200), Disposition = CatalystDisposition.Nominated, Detail = "d",
            DocumentUrl = string.Empty,
        });
        await _h.Coverage.Db.SaveChangesAsync();
        _h.Coverage.Db.ChangeTracker.Clear();

        await _h.Detector.RunAsync();

        (await _h.EventsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheCatalystRows_AreReadableThroughTheDigestQuery()
    {
        await WithUniverseAsync();
        _h.Filed("2.02", Filings.Of("AVGO", "A-1", "2.02", Friday));
        await _h.Detector.RunAsync();

        var handler = new FinanceSentry.Modules.Research.Application.Queries.ListCatalystEventsQueryHandler(
            new FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories.CatalystRepository(_h.Coverage.Db), _h.Clock,
            Microsoft.Extensions.Options.Options.Create(_h.Options));

        var digest = await handler.Handle(new FinanceSentry.Modules.Research.Application.Queries.ListCatalystEventsQuery(CatalystHarness.UserId), default);

        digest.Total.Should().Be(1);
        var view = digest.Events.Should().ContainSingle().Subject;
        view.Ticker.Should().Be("AVGO");
        view.Disposition.Should().Be("Nominated");
        view.Tag.Should().Be(CatalystRules.Tag("2.02"));
        digest.Truncated.Should().BeFalse();
    }
}
