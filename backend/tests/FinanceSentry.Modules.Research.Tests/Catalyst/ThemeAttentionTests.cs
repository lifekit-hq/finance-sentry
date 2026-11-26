namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>The GDELT attention measure: stored per theme phrase, read as a fact.</summary>
public sealed class ThemeAttentionTests
{
    private static readonly DateTimeOffset Now = CatalystHarness.Now;

    private readonly CatalystHarness _h = new();
    private readonly ScriptedGdelt _gdelt = new();

    private CatalystOptions Options(bool attention = true) => new()
    {
        AttentionEnabled = attention,
        ThemePhrases = new Dictionary<string, List<string>>
        {
            ["memory"] = ["HBM memory", "DRAM prices"],
            ["power"] = ["nuclear power plant"],
        },
    };

    private ThemeAttentionRefresher Refresher(CatalystOptions options, TimeProvider? clock = null) => new(
        _h.Reference, _gdelt, new ThemeAttentionRepository(_h.Coverage.Db), Microsoft.Extensions.Options.Options.Create(options),
        Microsoft.Extensions.Options.Options.Create(new ReferenceUniverseOptions()), clock ?? _h.Clock, NullLogger<ThemeAttentionRefresher>.Instance);

    private ThemeAttentionReader Reader(CatalystOptions options) => new(
        new ThemeAttentionRepository(_h.Coverage.Db), _h.Reference, Microsoft.Extensions.Options.Options.Create(options), _h.Clock);

    private static IReadOnlyList<GdeltDay> Series(DateOnly last, params long[] oldestFirst)
        => [.. oldestFirst.Select((n, i) => new GdeltDay(last.AddDays(-(oldestFirst.Length - 1 - i)), n, 1_000_000))];

    private Task<List<ThemeAttentionDay>> StoredAsync()
        => _h.Coverage.Db.ThemeAttentionDays.AsNoTracking().OrderBy(d => d.Theme).ThenBy(d => d.Phrase).ThenBy(d => d.Day).ToListAsync();

    [Fact]
    public async Task Refresh_StoresArticleVolume_PerPhraseOfEachStageThreeTheme()
    {
        await _h.ListAsync("theme:memory", "MU");
        await _h.ListAsync("theme:power", "VST");
        await _h.ListAsync("theme:energy", "XOM");
        var last = new DateOnly(2026, 11, 15);
        _gdelt.ByPhrase["HBM memory"] = Series(last, 10, 12, 30);
        _gdelt.ByPhrase["DRAM prices"] = Series(last, 5);
        _gdelt.ByPhrase["nuclear power plant"] = Series(last, 8, 9);

        var summary = await Refresher(Options()).RefreshAsync();

        summary.Themes.Should().Be(3);
        summary.Phrases.Should().Be(3);
        summary.DaysWritten.Should().Be(6);
        summary.Unmeasured.Should().Be(1, "theme:energy has no phrase configured");
        var stored = await StoredAsync();
        stored.Select(d => (d.Theme, d.Phrase, d.Day, d.Articles, d.Source)).Should().Contain(("theme:memory", "HBM memory", last, 30L, "gdelt"));
        stored.Should().OnlyContain(d => d.TotalArticles == 1_000_000 && d.MeasuredAt == Now);
        _gdelt.Asked.Should().BeEquivalentTo("HBM memory", "DRAM prices", "nuclear power plant");
    }

    [Fact]
    public async Task Refresh_ThemesDeclaredInTheReferenceConfig_AreMeasuredBeforeTheFirstWeeklyRefresh()
    {
        _gdelt.ByPhrase["HBM memory"] = Series(new DateOnly(2026, 11, 15), 1);
        var refresher = new ThemeAttentionRefresher(
            _h.Reference, _gdelt, new ThemeAttentionRepository(_h.Coverage.Db),
            Microsoft.Extensions.Options.Options.Create(new CatalystOptions { ThemePhrases = new() { ["memory"] = ["HBM memory"] } }),
            Microsoft.Extensions.Options.Options.Create(new ReferenceUniverseOptions { Themes = new() { ["memory"] = ["MU"] } }),
            _h.Clock, NullLogger<ThemeAttentionRefresher>.Instance);

        var summary = await refresher.RefreshAsync();

        summary.DaysWritten.Should().Be(1);
    }

    [Fact]
    public async Task Refresh_TheNextDaysRun_ReplacesTheDay_NeverDuplicatesIt()
    {
        await _h.ListAsync("theme:memory", "MU");
        var day = new DateOnly(2026, 11, 15);
        _gdelt.ByPhrase["HBM memory"] = Series(day, 10);
        _gdelt.ByPhrase["DRAM prices"] = Series(day, 5);
        await Refresher(Options()).RefreshAsync();

        _gdelt.ByPhrase["HBM memory"] = Series(day, 99);
        await Refresher(Options(), new AutoAdvanceClock(Now.AddDays(1))).RefreshAsync();

        var stored = await StoredAsync();
        stored.Where(d => d.Phrase == "HBM memory").Should().ContainSingle().Which.Articles.Should().Be(99);
        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task Refresh_AThrottledPhrase_StoresTheOthers_ThenFailsTheJobInTheFetchStep()
    {
        await _h.ListAsync("theme:memory", "MU");
        _gdelt.Failing.Add("HBM memory");
        _gdelt.ByPhrase["DRAM prices"] = Series(new DateOnly(2026, 11, 15), 5);

        var act = () => Refresher(Options()).RefreshAsync();

        (await act.Should().ThrowAsync<ThemeAttentionRefreshException>().WithMessage("*1 of 2*")).Which.Step.Should().Be("fetch-volume");
        (await StoredAsync()).Should().ContainSingle().Which.Phrase.Should().Be("DRAM prices");
    }

    [Fact]
    public async Task Refresh_EveryPhraseThrottled_FailsInTheFetchStep()
    {
        await _h.ListAsync("theme:memory", "MU");
        _gdelt.Failing.Add("HBM memory");
        _gdelt.Failing.Add("DRAM prices");

        var act = () => Refresher(Options()).RefreshAsync();

        (await act.Should().ThrowAsync<ThemeAttentionRefreshException>().WithMessage("*2 of 2*")).Which.Step.Should().Be("fetch-volume");
        (await StoredAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_APhraseThrottledOnce_RecoversOnRetry_AndTheRunCompletes()
    {
        await _h.ListAsync("theme:memory", "MU");
        const string Timeline = """{"timeline":[{"series":"Article Count","data":[{"date":"20261112T000000Z","value":7}]}]}""";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var http = new ScriptedHttp(url => seen.Add(url)
            ? (System.Net.HttpStatusCode.TooManyRequests, "Please limit requests to one every 5 seconds")
            : (System.Net.HttpStatusCode.OK, Timeline));
        var clock = new AutoAdvanceClock(DateTimeOffset.UnixEpoch);
        var gdelt = new FinanceSentry.Modules.Research.Infrastructure.Sources.Catalyst.GdeltTimelineClient(
            http, new RequestPacer(TimeSpan.FromSeconds(10), clock), Microsoft.Extensions.Options.Options.Create(Options()), clock,
            NullLogger<FinanceSentry.Modules.Research.Infrastructure.Sources.Catalyst.GdeltTimelineClient>.Instance);
        var refresher = new ThemeAttentionRefresher(
            _h.Reference, gdelt, new ThemeAttentionRepository(_h.Coverage.Db), Microsoft.Extensions.Options.Options.Create(Options()),
            Microsoft.Extensions.Options.Options.Create(new ReferenceUniverseOptions()), _h.Clock, NullLogger<ThemeAttentionRefresher>.Instance);

        var summary = await refresher.RefreshAsync();

        summary.Failed.Should().Be(0);
        summary.Phrases.Should().Be(2);
        (await StoredAsync()).Select(d => d.Phrase).Should().BeEquivalentTo("HBM memory", "DRAM prices");
        http.Requested.Should().HaveCount(4, "each phrase was throttled once and answered on the retry");
    }

    [Fact]
    public async Task Refresh_WithNoPhraseToAsk_RecordsASkippedResult_NotAFailure()
    {
        await _h.ListAsync("theme:memory", "MU");

        var summary = await Refresher(new CatalystOptions()).RefreshAsync();

        summary.Ran.Should().BeTrue();
        summary.Phrases.Should().Be(0);
        summary.Note.Should().Contain("nothing to measure");
        _gdelt.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Job_WhenTheRefreshFails_LogsTheJobNameTheStepAndTheException()
    {
        await _h.ListAsync("theme:memory", "MU");
        _gdelt.Failing.Add("HBM memory");
        _gdelt.Failing.Add("DRAM prices");
        var log = new RecordingLogger<ThemeAttentionRefreshJob>();
        var job = new ThemeAttentionRefreshJob(Refresher(Options()), log);

        var act = () => job.ExecuteAsync();

        await act.Should().ThrowAsync<ThemeAttentionRefreshException>();
        var entry = log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Subject;
        entry.Message.Should().Contain("ThemeAttentionRefreshJob").And.Contain("fetch-volume").And.Contain("could not read 2 of 2");
        entry.Exception.Should().BeOfType<ThemeAttentionRefreshException>();
    }

    // Issue 1554: GDELT answers only a minority of this host's requests, so a run rarely reads every phrase. The retry used to
    // re-ask every phrase and lose a different share each time, so the job failed every night. A retry now asks only the
    // phrases not yet measured that day, and the job ends Succeeded once each has been read once.
    [Fact]
    public async Task Job_ARetryAfterAPartialRun_AsksOnlyThePhrasesStillMissing_AndSucceeds()
    {
        await _h.ListAsync("theme:memory", "MU");
        var last = new DateOnly(2026, 11, 15);
        _gdelt.ByPhrase["HBM memory"] = Series(last, 7);
        _gdelt.ByPhrase["DRAM prices"] = Series(last, 3);
        _gdelt.Failing.Add("DRAM prices");
        var log = new RecordingLogger<ThemeAttentionRefreshJob>();

        var first = () => new ThemeAttentionRefreshJob(Refresher(Options()), log).ExecuteAsync();
        await first.Should().ThrowAsync<ThemeAttentionRefreshException>();

        // The retry: GDELT now throttles the phrase it answered before and answers the one it throttled.
        _gdelt.Failing.Clear();
        _gdelt.Failing.Add("HBM memory");
        _gdelt.Asked.Clear();

        await new ThemeAttentionRefreshJob(Refresher(Options()), log).ExecuteAsync();

        _gdelt.Asked.Should().Equal("DRAM prices");
        var stored = await StoredAsync();
        stored.Select(d => d.Phrase).Should().BeEquivalentTo("DRAM prices", "HBM memory");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("finished"));
    }

    [Fact]
    public async Task Refresh_APhraseMeasuredOnAnEarlierDay_IsAskedAgain()
    {
        await _h.ListAsync("theme:memory", "MU");
        var last = new DateOnly(2026, 11, 15);
        _h.Coverage.Db.ThemeAttentionDays.Add(new ThemeAttentionDay
        {
            Theme = "theme:memory", Phrase = "HBM memory", Day = last.AddDays(-1), Articles = 4, TotalArticles = 1_000_000,
            Source = "gdelt", MeasuredAt = Now.AddDays(-1),
        });
        await _h.Coverage.Db.SaveChangesAsync();
        _gdelt.ByPhrase["HBM memory"] = Series(last, 4, 6);
        _gdelt.ByPhrase["DRAM prices"] = Series(last, 3);

        var summary = await Refresher(Options()).RefreshAsync();

        summary.AlreadyMeasured.Should().Be(0);
        _gdelt.Asked.Should().BeEquivalentTo("HBM memory", "DRAM prices");
    }

    [Fact]
    public void Job_IsRetriedSeveralTimes_SoAThrottledPhraseGetsFreshPasses()
    {
        var retry = typeof(ThemeAttentionRefreshJob).GetMethod(nameof(ThemeAttentionRefreshJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(Hangfire.AutomaticRetryAttribute), false).Cast<Hangfire.AutomaticRetryAttribute>().Single();

        retry.Attempts.Should().Be(ThemeAttentionRefreshJob.RetryAttempts).And.BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task Refresh_AStoreFailure_NamesTheStoreStep()
    {
        await _h.ListAsync("theme:memory", "MU");
        _gdelt.ByPhrase["HBM memory"] = Series(new DateOnly(2026, 11, 15), 5);
        var refresher = new ThemeAttentionRefresher(
            _h.Reference, _gdelt, new ThrowingRepository(), Microsoft.Extensions.Options.Options.Create(Options()),
            Microsoft.Extensions.Options.Options.Create(new ReferenceUniverseOptions()), _h.Clock, NullLogger<ThemeAttentionRefresher>.Instance);

        var act = () => refresher.RefreshAsync();

        (await act.Should().ThrowAsync<ThemeAttentionRefreshException>()).Which.Should().Match<ThemeAttentionRefreshException>(
            e => e.Step == "store-volume" && e.InnerException is TimeoutException);
    }

    private sealed class ThrowingRepository : IThemeAttentionRepository
    {
        public Task<int> UpsertDaysAsync(IReadOnlyCollection<ThemeAttentionDay> days, CancellationToken ct = default)
            => throw new TimeoutException("db down");

        public Task<IReadOnlyList<ThemeAttentionDay>> ListDaysAsync(string? theme, DateOnly since, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ThemeAttentionDay>>([]);

        public Task<int> PruneAsync(DateOnly before, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    [Fact]
    public async Task Refresh_TheOffSwitches_AskGdeltNothing()
    {
        await _h.ListAsync("theme:memory", "MU");

        (await Refresher(Options(attention: false)).RefreshAsync()).Ran.Should().BeFalse();
        (await Refresher(new CatalystOptions { Enabled = false, ThemePhrases = Options().ThemePhrases }).RefreshAsync()).Ran.Should().BeFalse();

        _gdelt.Asked.Should().BeEmpty();
    }

    [Fact]
    public void Reading_UsesTheNewestCompleteDay_AgainstTheMeanOfTheDaysBeforeIt()
    {
        var today = new DateOnly(2026, 11, 16);
        var days = Series(today, 10, 10, 10, 10, 10, 10, 40, 999).Select(d => new ThemeAttentionDay
        {
            Theme = "theme:memory", Phrase = "HBM memory", Day = d.Day, Articles = d.Articles, MeasuredAt = Now,
        }).ToList();

        var reading = AttentionMath.Reading(days, today, baselineDays: 13, minBaselineDays: 5)!;

        reading.AsOf.Should().Be(new DateOnly(2026, 11, 15), "today's count is partial and never the reading");
        reading.Articles.Should().Be(40);
        reading.BaselineDays.Should().Be(6);
        reading.BaselineDailyArticles.Should().Be(10m);
        reading.Ratio.Should().Be(4m);
    }

    [Fact]
    public void Reading_WithTooFewBaselineDays_WithholdsTheRatio_AndWithNoCompleteDayIsNull()
    {
        var today = new DateOnly(2026, 11, 16);
        ThemeAttentionDay Day(int back, long n) => new()
        {
            Theme = "theme:memory", Phrase = "p", Day = today.AddDays(-back), Articles = n, MeasuredAt = Now,
        };

        var thin = AttentionMath.Reading([Day(1, 40), Day(2, 10), Day(3, 10)], today, 13, 5)!;
        thin.Ratio.Should().BeNull();
        thin.BaselineDailyArticles.Should().BeNull();
        thin.Articles.Should().Be(40);

        AttentionMath.Reading([Day(0, 5)], today, 13, 5).Should().BeNull();
        AttentionMath.Reading([], today, 13, 5).Should().BeNull();
    }

    [Fact]
    public async Task Reader_AndTheMcpQuery_ExposeTheStoredMeasure_ByThemeIdOrKey()
    {
        await _h.ListAsync("theme:memory", "MU");
        await _h.ListAsync("theme:power", "VST");
        var last = new DateOnly(2026, 11, 15);
        _gdelt.ByPhrase["HBM memory"] = Series(last, 10, 10, 10, 10, 10, 10, 30);
        _gdelt.ByPhrase["DRAM prices"] = Series(last, 4, 4, 4, 4, 4, 4, 4);
        _gdelt.ByPhrase["nuclear power plant"] = Series(last, 2);
        await Refresher(Options()).RefreshAsync();

        var all = await Reader(Options()).GetReadingsAsync(null);
        var memory = await Reader(Options()).GetReadingsAsync("memory");
        var byKey = await Reader(Options()).GetReadingsAsync("theme:MEMORY");

        all.Should().HaveCount(3);
        memory.Select(r => r.Phrase).Should().Equal("DRAM prices", "HBM memory");
        byKey.Should().BeEquivalentTo(memory);
        memory.Single(r => r.Phrase == "HBM memory").Ratio.Should().Be(3m);

        var view = await new GetThemeAttentionQueryHandler(Reader(Options())).Handle(new GetThemeAttentionQuery("memory"), default);
        view.Count.Should().Be(2);
        view.Note.Should().BeNull();
        (await new GetThemeAttentionQueryHandler(Reader(Options())).Handle(new GetThemeAttentionQuery("energy"), default)).Note.Should().Contain("No attention reading");
    }

    [Fact]
    public async Task ForATicker_TheFactCarriesTheReadingsOfItsThemes_AndSaysWhyWhenEmpty()
    {
        await _h.ListAsync("theme:memory", "MU");
        await _h.ListAsync("sp500", "MU", "AAPL");
        var last = new DateOnly(2026, 11, 15);
        _gdelt.ByPhrase["HBM memory"] = Series(last, 10, 10, 10, 10, 10, 10, 30);
        _gdelt.ByPhrase["DRAM prices"] = Series(last, 4);
        await Refresher(Options()).RefreshAsync();

        var mu = await Reader(Options()).GetForTickerAsync("MU");
        var aapl = await Reader(Options()).GetForTickerAsync("AAPL");

        mu.Readings.Should().HaveCount(2).And.OnlyContain(r => r.Theme == "theme:memory");
        mu.Note.Should().BeNull();
        aapl.Readings.Should().BeEmpty();
        aapl.Note.Should().Contain("no reference theme");

        await _h.ListAsync("theme:power", "VST");
        (await Reader(Options()).GetForTickerAsync("VST")).Note.Should().Contain("No attention reading is stored");
    }
}
