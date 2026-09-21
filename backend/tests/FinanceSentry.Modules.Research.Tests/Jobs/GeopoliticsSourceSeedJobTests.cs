namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Tests.Companion;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// N2 (ledger-heartbeat design): geopolitics/policy coverage derived from thesis text, registered as a
/// Google News RSS query source through the existing <c>register_thesis_source</c> path (Q3 option A)
/// — no new <see cref="NewsSourceKind"/>. A thesis whose text carries no geopolitics/policy term gets
/// no source, which is what keeps this at ~0.2 fires/day.
/// </summary>
public sealed class GeopoliticsSourceSeedJobTests : IDisposable
{
    private readonly ResearchDbContext _db = CompanionTestContext.Create();
    private readonly FakeNewsSourceRepository _sources = new();

    private GeopoliticsSourceSeedJob Job => new(
        _db,
        new RegisterThesisSourceCommandHandler(_sources),
        NullLogger<GeopoliticsSourceSeedJob>.Instance);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Thesis_mentioning_a_geopolitics_term_gets_a_google_news_rss_source_registered()
    {
        var thesisId = await SeedThesisAsync("TSM", "Taiwan Semi is exposed to US-China export controls on advanced nodes.");

        await Job.ExecuteAsync();

        var source = _sources.Sources.Should().ContainSingle().Subject;
        source.Kind.Should().Be(NewsSourceKind.Rss);
        source.ThesisId.Should().Be(thesisId);
        source.Url.Should().StartWith("https://news.google.com/rss/search?q=");
        source.Keywords.Should().Contain("export control");
    }

    [Fact]
    public async Task Thesis_with_no_geopolitics_term_gets_no_source()
    {
        await SeedThesisAsync("AAPL", "Services mix shift drives margin expansion through 2028.");

        await Job.ExecuteAsync();

        _sources.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task Rerunning_the_job_does_not_duplicate_the_source()
    {
        await SeedThesisAsync("TSM", "Sanctions risk on Taiwan Semi given cross-strait tension.");

        await Job.ExecuteAsync();
        await Job.ExecuteAsync();

        _sources.Sources.Should().ContainSingle("register_thesis_source is idempotent by URL");
    }

    [Fact]
    public async Task Multiple_theses_each_get_their_own_source()
    {
        await SeedThesisAsync("TSM", "Export controls threaten advanced-node access.");
        await SeedThesisAsync("MOS", "Sanctions on Russian energy exports affect this thesis.");

        await Job.ExecuteAsync();

        _sources.Sources.Should().HaveCount(2);
    }

    [Fact]
    public async Task Matched_terms_are_capped_so_the_query_stays_bounded()
    {
        await SeedThesisAsync(
            "TSM",
            "Sanctions, tariffs, export controls, a ceasefire and an SEC ruling on regulation all matter here.");

        await Job.ExecuteAsync();

        var source = _sources.Sources.Single();
        source.Keywords.Should().HaveCountLessThanOrEqualTo(3);
    }

    private async Task<Guid> SeedThesisAsync(string ticker, string thesisText)
    {
        var thesis = new InvestmentThesis { UserId = Guid.NewGuid(), Ticker = ticker, ThesisText = thesisText };
        _db.Theses.Add(thesis);
        await _db.SaveChangesAsync();
        return thesis.Id;
    }
}
