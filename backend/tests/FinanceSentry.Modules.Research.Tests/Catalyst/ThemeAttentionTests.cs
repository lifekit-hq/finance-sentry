namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    private ThemeAttentionRefresher Refresher(CatalystOptions options) => new(
        _h.Reference, _gdelt, new ThemeAttentionRepository(_h.Coverage.Db), Microsoft.Extensions.Options.Options.Create(options),
        Microsoft.Extensions.Options.Options.Create(new ReferenceUniverseOptions()), _h.Clock, NullLogger<ThemeAttentionRefresher>.Instance);

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
    public async Task Refresh_ARerun_ReplacesTheDay_NeverDuplicatesIt()
    {
        await _h.ListAsync("theme:memory", "MU");
        var day = new DateOnly(2026, 11, 15);
        _gdelt.ByPhrase["HBM memory"] = Series(day, 10);
        _gdelt.ByPhrase["DRAM prices"] = Series(day, 5);
        await Refresher(Options()).RefreshAsync();

        _gdelt.ByPhrase["HBM memory"] = Series(day, 99);
        await Refresher(Options()).RefreshAsync();

        var stored = await StoredAsync();
        stored.Where(d => d.Phrase == "HBM memory").Should().ContainSingle().Which.Articles.Should().Be(99);
        stored.Should().HaveCount(2);
    }

    [Fact]
    public async Task Refresh_AThrottledPhrase_FailsTheJob_ButTheOthersAreStored()
    {
        await _h.ListAsync("theme:memory", "MU");
        _gdelt.Failing.Add("HBM memory");
        _gdelt.ByPhrase["DRAM prices"] = Series(new DateOnly(2026, 11, 15), 5);

        var act = () => Refresher(Options()).RefreshAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*1 of 2*");
        (await StoredAsync()).Should().ContainSingle().Which.Phrase.Should().Be("DRAM prices");
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
