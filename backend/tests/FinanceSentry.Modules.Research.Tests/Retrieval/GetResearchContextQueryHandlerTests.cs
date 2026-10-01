namespace FinanceSentry.Modules.Research.Tests.Retrieval;

using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Xunit;

public class GetResearchContextQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static ResearchRetrievalHit Hit(ResearchDocumentSourceType sourceType, string title, double score)
    {
        var document = RetrievalTestContext.CreateDocument(title, $"Body of {title}.", sourceType: sourceType);
        var chunk = new ResearchChunk
        {
            DocumentId = document.Id,
            Ordinal = 0,
            Text = document.Text,
            ContentHash = ResearchChunker.ComputeContentHash(document.Text),
        };
        return new ResearchRetrievalHit(document, chunk, score, score, score);
    }

    private static GetResearchContextQueryHandler CreateHandler(
        FakeRetriever retriever, FakeThesisRepository theses)
        => new(retriever, theses, RetrievalTestContext.CreateOptions());

    private static GetResearchContextQuery Query(
        Guid? thesisId = null, string? ticker = null, string? question = null, int? maxChunks = null)
        => new(UserId, thesisId, ticker, question, null, maxChunks, []);

    [Fact]
    public async Task Handle_GroupsEvidenceBySourceType_InStableOrder()
    {
        var retriever = new FakeRetriever
        {
            Result = new ResearchRetrievalResult(
                [
                    Hit(ResearchDocumentSourceType.NewsArticle, "News A", 0.9),
                    Hit(ResearchDocumentSourceType.DecisionNote, "Note A", 0.8),
                    Hit(ResearchDocumentSourceType.InvestmentThesis, "Thesis A", 0.7),
                    Hit(ResearchDocumentSourceType.Postmortem, "Postmortem A", 0.6),
                ],
                4),
        };
        var thesis = new InvestmentThesis { UserId = UserId, Ticker = "MU", ThesisText = "DRAM recovery." };
        var theses = new FakeThesisRepository();
        theses.Theses.Add(thesis);
        var handler = CreateHandler(retriever, theses);

        var packet = await handler.Handle(Query(thesisId: thesis.Id), CancellationToken.None);

        packet.SubjectType.Should().Be("Thesis");
        packet.Thesis.Should().NotBeNull();
        packet.Thesis!.Ticker.Should().Be("MU");
        packet.Groups.Select(g => g.Name).Should().Equal("thesis", "decision_notes", "recent_news", "postmortems");
        packet.Groups.Should().OnlyContain(g => g.Items.Count > 0);
        packet.Groups.SelectMany(g => g.Items).Should().OnlyContain(i => i.Snippet.Length > 0);
    }

    [Fact]
    public async Task Handle_ReportsOmittedCount_WhenCorpusExceedsBudget()
    {
        var retriever = new FakeRetriever
        {
            Result = new ResearchRetrievalResult(
                [Hit(ResearchDocumentSourceType.NewsArticle, "News A", 0.9)], 9),
        };
        var handler = CreateHandler(retriever, new FakeThesisRepository());

        var packet = await handler.Handle(Query(ticker: "MU", maxChunks: 1), CancellationToken.None);

        packet.OmittedCount.Should().Be(8);
        retriever.LastRequest!.Limit.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ClampsMaxChunks_ToConfiguredCap()
    {
        var retriever = new FakeRetriever();
        var handler = CreateHandler(retriever, new FakeThesisRepository());

        await handler.Handle(Query(ticker: "MU", maxChunks: 500), CancellationToken.None);

        retriever.LastRequest!.Limit.Should().Be(30);
    }

    [Fact]
    public async Task Handle_TickerWithoutThesis_ReturnsNullThesis_AndGlobalEvidence()
    {
        var retriever = new FakeRetriever
        {
            Result = new ResearchRetrievalResult(
                [Hit(ResearchDocumentSourceType.NewsArticle, "MU news", 0.9)], 1),
        };
        var handler = CreateHandler(retriever, new FakeThesisRepository());

        var packet = await handler.Handle(Query(ticker: "mu"), CancellationToken.None);

        packet.SubjectType.Should().Be("Ticker");
        packet.Thesis.Should().BeNull("no thesis context exists for the ticker");
        packet.Ticker.Should().Be("MU");
        packet.Groups.Should().ContainSingle(g => g.Name == "recent_news");
        retriever.LastRequest!.Tickers.Should().Equal("MU");
    }

    [Fact]
    public async Task Handle_UsesThesisTicker_ToScopeRetrieval()
    {
        var thesis = new InvestmentThesis { UserId = UserId, Ticker = "MU", ThesisText = "DRAM recovery thesis." };
        var theses = new FakeThesisRepository();
        theses.Theses.Add(thesis);
        var retriever = new FakeRetriever();
        var handler = CreateHandler(retriever, theses);

        await handler.Handle(Query(thesisId: thesis.Id), CancellationToken.None);

        retriever.LastRequest!.Tickers.Should().Equal("MU");
        retriever.LastRequest.Query.Should().Contain("DRAM recovery");
    }

    [Fact]
    public async Task Handle_PrefersFocusingQuestion_OverThesisText()
    {
        var thesis = new InvestmentThesis { UserId = UserId, Ticker = "MU", ThesisText = "DRAM recovery thesis." };
        var theses = new FakeThesisRepository();
        theses.Theses.Add(thesis);
        var retriever = new FakeRetriever();
        var handler = CreateHandler(retriever, theses);

        await handler.Handle(
            Query(thesisId: thesis.Id, question: "what breaks this thesis?"), CancellationToken.None);

        retriever.LastRequest!.Query.Should().Be("what breaks this thesis?");
    }
}

internal sealed class FakeRetriever : IResearchRetriever
{
    public ResearchRetrievalResult Result { get; set; } = new([], 0);

    public ResearchRetrievalRequest? LastRequest { get; private set; }

    public Task<ResearchRetrievalResult> SearchAsync(
        ResearchRetrievalRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeThesisRepository : IThesisRepository
{
    public List<InvestmentThesis> Theses { get; } = [];

    public Task<IReadOnlyList<InvestmentThesis>> ListAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InvestmentThesis>>(Theses.Where(t => t.UserId == userId).ToList());

    public Task<IReadOnlyList<InvestmentThesis>> ListUnscopedAsync(Guid userId, CancellationToken ct = default)
        => ListAsync(userId, ct);

    public Task<IReadOnlyList<Guid>> GetUserIdsWithThesesUnscopedAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(Theses.Select(t => t.UserId).Distinct().ToList());

    public Task<InvestmentThesis?> FindAsync(Guid userId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Theses.FirstOrDefault(t => t.UserId == userId && t.Id == id));

    public Task<IReadOnlyList<InvestmentThesis>> FindByTickerAsync(
        Guid userId, string ticker, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InvestmentThesis>>(Theses
            .Where(t => t.UserId == userId && t.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase))
            .ToList());

    public Task UpsertAsync(InvestmentThesis thesis, CancellationToken ct = default)
    {
        Theses.RemoveAll(t => t.Id == thesis.Id);
        Theses.Add(thesis);
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Theses.RemoveAll(t => t.UserId == userId && t.Id == id) > 0);
}
