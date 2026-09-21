namespace FinanceSentry.Modules.Research.Tests.Companion;

using System.Linq;
using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>
/// Thesis-source staleness, "orphaned" half (paired with <see cref="Jobs.ThesisSourceRetirementJobTests"/>
/// for the "text no longer matches" half). Retirement has to happen here, synchronously, because
/// <c>news_sources.ThesisId</c> is <c>ON DELETE SET NULL</c> — by the time any later sweep could look,
/// the FK has already erased the only link back to the deleted thesis.
/// </summary>
public sealed class DeleteThesisCommandTests
{
    private readonly FakeNewsSourceRepository _sources = new();
    private readonly FakeThesisRepository _theses = new();

    private DeleteThesisCommandHandler Handler => new(_theses, _sources);

    [Fact]
    public async Task Deleting_a_thesis_retires_its_registered_source()
    {
        var userId = Guid.NewGuid();
        var thesis = new InvestmentThesis { UserId = userId, Ticker = "TSM", ThesisText = "Export controls." };
        _theses.Theses.Add(thesis);
        var source = new NewsSource
        {
            Name = "Google News: TSM geopolitics",
            Kind = NewsSourceKind.Rss,
            Url = "https://news.google.com/rss/search?q=1",
            ThesisId = thesis.Id,
        };
        _sources.Sources.Add(source);

        var result = await Handler.Handle(new DeleteThesisCommand(userId, thesis.Id), default);

        result.Should().BeTrue();
        var retired = _sources.Sources.Single();
        retired.Enabled.Should().BeFalse();
        retired.RetiredAt.Should().NotBeNull();
        retired.RetiredReason.Should().Contain("TSM");
        _theses.Theses.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_thesis_with_no_registered_source_still_deletes_it()
    {
        var userId = Guid.NewGuid();
        var thesis = new InvestmentThesis { UserId = userId, Ticker = "AAPL", ThesisText = "Services mix shift." };
        _theses.Theses.Add(thesis);

        var result = await Handler.Handle(new DeleteThesisCommand(userId, thesis.Id), default);

        result.Should().BeTrue();
        _sources.Sources.Should().BeEmpty();
        _theses.Theses.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_thesis_that_does_not_exist_returns_false_and_touches_no_source()
    {
        var userId = Guid.NewGuid();
        var otherUsersSource = new NewsSource
        {
            Name = "unrelated",
            Kind = NewsSourceKind.Rss,
            Url = "https://news.google.com/rss/search?q=2",
            ThesisId = Guid.NewGuid(),
        };
        _sources.Sources.Add(otherUsersSource);

        var result = await Handler.Handle(new DeleteThesisCommand(userId, Guid.NewGuid()), default);

        result.Should().BeFalse();
        _sources.Sources.Single().RetiredReason.Should().BeNull();
    }

    [Fact]
    public async Task Deleting_a_thesis_does_not_retire_a_market_wide_source_or_another_thesis_source()
    {
        var userId = Guid.NewGuid();
        var thesis = new InvestmentThesis { UserId = userId, Ticker = "TSM", ThesisText = "Export controls." };
        var otherThesis = new InvestmentThesis { UserId = userId, Ticker = "NVDA", ThesisText = "Export controls." };
        _theses.Theses.Add(thesis);
        _theses.Theses.Add(otherThesis);
        var marketWide = new NewsSource { Name = "Default", Kind = NewsSourceKind.Rss, Url = "https://news.google.com/rss/search?q=3", ThesisId = null };
        var otherSource = new NewsSource { Name = "NVDA feed", Kind = NewsSourceKind.Rss, Url = "https://news.google.com/rss/search?q=4", ThesisId = otherThesis.Id };
        _sources.Sources.Add(marketWide);
        _sources.Sources.Add(otherSource);

        await Handler.Handle(new DeleteThesisCommand(userId, thesis.Id), default);

        _sources.Sources.Single(s => s.Id == marketWide.Id).Enabled.Should().BeTrue();
        _sources.Sources.Single(s => s.Id == otherSource.Id).Enabled.Should().BeTrue();
    }

    private sealed class FakeThesisRepository : IThesisRepository
    {
        public List<InvestmentThesis> Theses { get; } = [];

        public Task<IReadOnlyList<InvestmentThesis>> ListAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentThesis>>(Theses.Where(t => t.UserId == userId).ToList());

        public Task<IReadOnlyList<Guid>> GetUserIdsWithThesesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Theses.Select(t => t.UserId).Distinct().ToList());

        public Task<InvestmentThesis?> FindAsync(Guid userId, Guid id, CancellationToken ct = default)
            => Task.FromResult(Theses.FirstOrDefault(t => t.UserId == userId && t.Id == id));

        public Task<IReadOnlyList<InvestmentThesis>> FindByTickerAsync(Guid userId, string ticker, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InvestmentThesis>>(
                Theses.Where(t => t.UserId == userId && t.Ticker == ticker).ToList());

        public Task UpsertAsync(InvestmentThesis thesis, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
        {
            var removed = Theses.RemoveAll(t => t.UserId == userId && t.Id == id);
            return Task.FromResult(removed > 0);
        }
    }
}
