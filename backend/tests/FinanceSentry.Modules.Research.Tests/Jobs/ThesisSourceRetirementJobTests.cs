namespace FinanceSentry.Modules.Research.Tests.Jobs;

using System.Linq;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Thesis-source staleness, "text no longer matches" half (paired with <see cref="DeleteThesisCommandTests"/>
/// for the "orphaned" half). A thesis edited so its text drops the geopolitics term that earned a source
/// must retire that source — otherwise it keeps polling and feeding the detector under a thesis that no
/// longer says what it said.
/// </summary>
public sealed class ThesisSourceRetirementJobTests : IDisposable
{
    private readonly ResearchDbContext _db = CompanionTestContext.Create();
    private readonly FakeNewsSourceRepository _sources = new();

    private ThesisSourceRetirementJob Job => new(_db, _sources, NullLogger<ThesisSourceRetirementJob>.Instance);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Source_whose_thesis_text_no_longer_matches_is_retired()
    {
        var thesisId = await SeedThesisAsync("TSM", "Services mix shift drives margin expansion through 2028.");
        var source = SeedSource(thesisId, SeededUrl(thesisId, "Sanctions risk."));

        await Job.ExecuteAsync();

        var retired = _sources.Sources.Single(s => s.Id == source.Id);
        retired.Enabled.Should().BeFalse();
        retired.RetiredAt.Should().NotBeNull();
        retired.RetiredReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Source_whose_thesis_still_matches_is_untouched_across_repeated_runs()
    {
        const string text = "Sanctions risk on Taiwan Semi given cross-strait tension.";
        var thesisId = await SeedThesisAsync("TSM", text);
        var source = SeedSource(thesisId, SeededUrl(thesisId, text));

        await Job.ExecuteAsync();
        await Job.ExecuteAsync();

        var untouched = _sources.Sources.Single(s => s.Id == source.Id);
        untouched.Enabled.Should().BeTrue();
        untouched.RetiredAt.Should().BeNull();
        untouched.RetiredReason.Should().BeNull();
    }

    [Fact]
    public async Task Source_that_is_merely_failing_is_left_to_the_health_tracker_not_retired()
    {
        const string text = "Sanctions risk on Taiwan Semi given cross-strait tension.";
        var thesisId = await SeedThesisAsync("TSM", text);
        var source = SeedSource(thesisId, SeededUrl(thesisId, text));
        source.Enabled = false;
        source.ConsecutiveFailures = 12;
        source.LastFailureReason = "feed timeout";

        await Job.ExecuteAsync();

        var untouched = _sources.Sources.Single(s => s.Id == source.Id);
        untouched.Enabled.Should().BeFalse("the health tracker, not this job, owns that state");
        untouched.ConsecutiveFailures.Should().Be(12);
        untouched.RetiredReason.Should().BeNull("thesis text still matches — this is a health failure, not thesis staleness");
    }

    [Fact]
    public async Task Already_retired_source_is_not_re_touched_on_a_later_run()
    {
        var thesisId = await SeedThesisAsync("TSM", "Services mix shift drives margin expansion through 2028.");
        SeedSource(thesisId, SeededUrl(thesisId, "Sanctions risk."));

        await Job.ExecuteAsync();
        var firstRetiredAt = _sources.Sources.Single().RetiredAt;

        await Job.ExecuteAsync();

        _sources.Sources.Single().RetiredAt.Should().Be(firstRetiredAt);
    }

    [Fact]
    public async Task Market_wide_source_with_no_thesis_is_ignored()
    {
        var source = new NewsSource
        {
            Name = "Default feed",
            Kind = NewsSourceKind.Rss,
            Url = "https://news.google.com/rss/search?q=5",
            ThesisId = null,
        };
        _sources.Sources.Add(source);

        await Job.ExecuteAsync();

        var untouched = _sources.Sources.Single();
        untouched.Enabled.Should().BeTrue();
        untouched.RetiredReason.Should().BeNull();
    }

    [Fact]
    public async Task Source_whose_thesis_terms_changed_is_retired()
    {
        var thesisId = await SeedThesisAsync("TSM", "Tariff risk on Taiwan Semi.");
        var source = SeedSource(thesisId, SeededUrl(thesisId, "Sanctions risk on Taiwan Semi."));

        await Job.ExecuteAsync();

        var retired = _sources.Sources.Single(s => s.Id == source.Id);
        retired.Enabled.Should().BeFalse();
        retired.RetiredReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task User_registered_thesis_source_is_not_judged_against_geopolitics_terms()
    {
        var thesisId = await SeedThesisAsync("AAPL", "Services mix shift drives margin expansion.");
        var source = SeedSource(thesisId, "https://investor.apple.com/rss/news.xml");

        await Job.ExecuteAsync();

        var untouched = _sources.Sources.Single(s => s.Id == source.Id);
        untouched.Enabled.Should().BeTrue();
        untouched.RetiredReason.Should().BeNull();
    }

    private static string SeededUrl(Guid thesisId, string matchingText)
        => GeopoliticsTermMatcher.SourceUrlFor(thesisId, "TSM", matchingText)!;

    private NewsSource SeedSource(Guid thesisId, string url)
    {
        var source = new NewsSource
        {
            Name = "Google News: TSM geopolitics",
            Kind = NewsSourceKind.Rss,
            Url = url,
            ThesisId = thesisId,
        };
        _sources.Sources.Add(source);
        return source;
    }

    private async Task<Guid> SeedThesisAsync(string ticker, string thesisText)
    {
        var thesis = new InvestmentThesis { UserId = Guid.NewGuid(), Ticker = ticker, ThesisText = thesisText };
        _db.Theses.Add(thesis);
        await _db.SaveChangesAsync();
        return thesis.Id;
    }
}
