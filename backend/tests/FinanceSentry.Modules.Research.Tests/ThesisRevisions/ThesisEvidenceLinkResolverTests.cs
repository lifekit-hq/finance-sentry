namespace FinanceSentry.Modules.Research.Tests.ThesisRevisions;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Revisions;
using FinanceSentry.Modules.Research.Tests.Retrieval;
using FluentAssertions;
using Xunit;

/// <summary>The owner's confirm card shows what a proposal rests on: a stored article as its title and link, the rest as labels.</summary>
public sealed class ThesisEvidenceLinkResolverTests
{
    private static ThesisRevision Revision(ThesisEvidenceRefs? evidence)
        => new() { UserId = Guid.NewGuid(), Ticker = "CBRS", Evidence = evidence };

    [Fact]
    public async Task Resolve_StoredArticle_CarriesItsTitleAndLink_AndOtherRefsCarryLabels()
    {
        await using var db = RetrievalTestContext.CreateDb();
        var article = new NewsArticle { Title = "Cerebras files for IPO", Url = "https://example.com/cbrs" };
        db.News.Add(article);
        await db.SaveChangesAsync();
        var revision = Revision(new ThesisEvidenceRefs(
            NewsArticleIds: [article.Id.ToString()], FilingAccessions: ["0001-26-1"], EventIds: ["ev-7"], FundamentalsAsOf: "2026-09-30"));

        var links = (await new ThesisEvidenceLinkResolver(db).ResolveAsync([revision]))[revision.Id];

        links.Should().Equal(
            [
                new ThesisEvidenceLinkDto("News", "Cerebras files for IPO", "https://example.com/cbrs"),
                new ThesisEvidenceLinkDto("Filing", "Filing 0001-26-1", null),
                new ThesisEvidenceLinkDto("Event", "Event ev-7", null),
                new ThesisEvidenceLinkDto("Fundamentals", "Fundamentals as of 2026-09-30", null),
            ]);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public async Task Resolve_ArticleWithANonWebUrl_KeepsTheTitleButNoLink(string url)
    {
        await using var db = RetrievalTestContext.CreateDb();
        var article = new NewsArticle { Title = "Odd link", Url = url };
        db.News.Add(article);
        await db.SaveChangesAsync();
        var revision = Revision(new ThesisEvidenceRefs(NewsArticleIds: [article.Id.ToString()]));

        var links = (await new ThesisEvidenceLinkResolver(db).ResolveAsync([revision]))[revision.Id];

        links.Should().ContainSingle().Which.Should().BeEquivalentTo(new ThesisEvidenceLinkDto("News", "Odd link", null));
    }

    [Fact]
    public async Task Resolve_UnknownOrMalformedArticleId_ReadsAsNoLongerStored()
    {
        await using var db = RetrievalTestContext.CreateDb();
        var revision = Revision(new ThesisEvidenceRefs(NewsArticleIds: [Guid.NewGuid().ToString(), "garbage"]));

        var links = (await new ThesisEvidenceLinkResolver(db).ResolveAsync([revision]))[revision.Id];

        links.Select(l => l.Label).Should().OnlyContain(l => l == ThesisEvidenceLinkResolver.MissingArticleLabel).And.HaveCount(2);
    }

    [Fact]
    public async Task Resolve_RevisionWithoutEvidence_HasNoEntry()
    {
        await using var db = RetrievalTestContext.CreateDb();

        var resolved = await new ThesisEvidenceLinkResolver(db).ResolveAsync([Revision(null), Revision(new ThesisEvidenceRefs())]);

        resolved.Should().BeEmpty();
    }

    [Fact]
    public async Task Queue_FillsEvidenceLinksOnEveryRow()
    {
        await using var db = RetrievalTestContext.CreateDb();
        var repo = new InMemoryThesisRevisionRepository();
        var user = Guid.NewGuid();
        repo.Rows.Add(new ThesisRevision
        {
            UserId = user,
            Ticker = "CBRS",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(5),
            Evidence = new ThesisEvidenceRefs(FilingAccessions: ["0001-26-1"]),
        });

        var result = await new GetThesisRevisionsQueryHandler(repo, new ThesisEvidenceLinkResolver(db))
            .Handle(new GetThesisRevisionsQuery(user), CancellationToken.None);

        result.Pending.Should().ContainSingle().Which.EvidenceLinks.Should().ContainSingle().Which.Kind.Should().Be("Filing");
    }
}
