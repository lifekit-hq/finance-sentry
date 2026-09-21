namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Domain;
using FluentAssertions;
using Xunit;

/// <summary>
/// Re-registration semantics for <see cref="RegisterThesisSourceCommand"/> (feature 030, FR-007;
/// spec 046). Re-registering is the operator's "try this again" — it has to clear the failure history
/// or a retired source is re-retired by its first failure (issue #318).
/// </summary>
public sealed class RegisterThesisSourceCommandTests
{
    private const string Url = "https://www.trendforce.com/presscenter/news";

    private readonly FakeNewsSourceRepository _repo = new();

    [Fact]
    public async Task Re_registering_a_retired_source_clears_its_failure_history()
    {
        var existing = new NewsSource
        {
            Name = "TrendForce Press Center",
            Kind = NewsSourceKind.Page,
            Url = Url,
            Enabled = false,
            ConsecutiveFailures = 17,
            LastFailureReason = "article list not found",
        };
        _repo.Sources.Add(existing);

        var result = await HandleAsync(thesisId: Guid.NewGuid());

        result.SourceId.Should().Be(existing.Id, "re-registering is idempotent by URL");
        var after = _repo.Sources.Single();
        after.Enabled.Should().BeTrue();
        after.ConsecutiveFailures.Should().Be(0);
        after.LastFailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Re_registering_a_staleness_retired_source_clears_its_retirement()
    {
        _repo.Sources.Add(new NewsSource
        {
            Name = "TrendForce Press Center",
            Kind = NewsSourceKind.Page,
            Url = Url,
            Enabled = false,
            RetiredAt = DateTimeOffset.UtcNow,
            RetiredReason = "Owning thesis TSM was deleted",
        });

        await HandleAsync(thesisId: Guid.NewGuid());

        var after = _repo.Sources.Single();
        after.Enabled.Should().BeTrue();
        after.RetiredAt.Should().BeNull();
        after.RetiredReason.Should().BeNull();
    }

    [Fact]
    public async Task Re_registering_updates_the_thesis_binding_and_keywords()
    {
        var thesisId = Guid.NewGuid();
        _repo.Sources.Add(new NewsSource
        {
            Name = "old name",
            Kind = NewsSourceKind.Page,
            Url = Url,
            Keywords = ["stale"],
        });

        await HandleAsync(thesisId);

        var after = _repo.Sources.Single();
        after.Name.Should().Be("TrendForce Press Center");
        after.ThesisId.Should().Be(thesisId);
        after.Keywords.Should().Equal("DRAM", "HBM");
    }

    [Fact]
    public async Task Registering_a_new_url_adds_an_enabled_source()
    {
        var result = await HandleAsync(thesisId: null);

        result.Enabled.Should().BeTrue();
        _repo.Sources.Should().ContainSingle(s => s.Url == Url && s.ThesisId == null);
    }

    private Task<API.Responses.RegisteredSourceDto> HandleAsync(Guid? thesisId) =>
        new RegisterThesisSourceCommandHandler(_repo).Handle(
            new RegisterThesisSourceCommand(
                thesisId, "TrendForce Press Center", Url, "Page", ["DRAM", "HBM"]),
            default);
}
