namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The Owner query filter on <see cref="ResearchDbContext"/>: a context acting for one person sees only that
/// person's rows (plus the shared, ownerless research corpus), a context with no person in scope sees none, and
/// the jobs and cross-module readers (which run with no person in scope) opt out explicitly, so they neither see
/// nothing nor re-insert rows they could not find. Real Postgres is required, matching the other filter suites.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResearchOwnerQueryFilterTests : IAsyncLifetime
{
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    // Null acts as a background job: no person in scope.
    private ResearchDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser));

    private static InvestmentThesis NewThesis(Guid userId, string ticker = "AAPL") =>
        new() { UserId = userId, Ticker = ticker, ThesisText = $"{ticker} compounds." };

    private static InvestmentPolicyStatement NewIps(Guid userId) =>
        new() { UserId = userId, Version = 1, IsCurrent = true, ReviewCadence = "quarterly" };

    private static ThesisEvent NewEvent(Guid userId, Guid subjectId, bool pricesPending = false) => new()
    {
        UserId = userId,
        SubjectType = ThesisSubjectType.Thesis,
        SubjectId = subjectId,
        Ticker = "AAPL",
        EventType = ThesisEventType.Created,
        PricesPending = pricesPending,
    };

    private static ResearchDocument NewDocument(Guid? userId) => new()
    {
        SourceType = ResearchDocumentSourceType.NewsArticle,
        SourceId = Guid.NewGuid().ToString(),
        UserId = userId,
        Title = "Doc",
        Text = "Body",
        ContentHash = Guid.NewGuid().ToString("N"),
    };

    private async Task SeedAsync(params object[] entities)
    {
        // Inserts are not filtered, so a no-person context writes any user's rows.
        await using var seed = CreateContext();
        seed.AddRange(entities);
        await seed.SaveChangesAsync();
    }

    [DockerRequiredFact]
    public void Every_per_user_entity_declares_the_Owner_filter()
    {
        using var ctx = CreateContext();

        var perUser = ctx.Model.GetEntityTypes().Where(e => e.FindProperty("UserId") is not null).ToList();

        perUser.Should().HaveCount(9);
        perUser.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [DockerRequiredFact]
    public async Task Each_person_sees_only_their_own_rows_and_no_person_sees_none()
    {
        var a = NewThesis(_userA);
        var b = NewThesis(_userB);
        await SeedAsync(a, b, NewIps(_userA), NewIps(_userB),
            new WatchlistItem { UserId = _userA, Ticker = "AAPL" }, new WatchlistItem { UserId = _userB, Ticker = "MSFT" });

        await using (var asA = CreateContext(_userA))
        {
            (await asA.Theses.Select(x => x.Id).ToListAsync()).Should().Equal(a.Id);
            (await asA.PolicyStatements.Select(x => x.UserId).ToListAsync()).Should().Equal(_userA);
            (await asA.WatchlistItems.Select(x => x.Ticker).ToListAsync()).Should().Equal("AAPL");
        }

        await using (var asB = CreateContext(_userB))
        {
            (await asB.Theses.Select(x => x.Id).ToListAsync()).Should().Equal(b.Id);
            (await asB.Theses.AnyAsync(x => x.UserId == _userA)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.Theses.AnyAsync()).Should().BeFalse("no person in scope matches no row");
        (await asNoOne.PolicyStatements.AnyAsync()).Should().BeFalse();
        (await asNoOne.WatchlistItems.AnyAsync()).Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Shared_corpus_documents_are_readable_by_everyone_and_owned_ones_only_by_their_owner()
    {
        var shared = NewDocument(userId: null);
        var ownedByA = NewDocument(_userA);
        await SeedAsync(shared, ownedByA);

        await using (var asA = CreateContext(_userA))
            (await asA.ResearchDocuments.Select(d => d.Id).ToListAsync()).Should().BeEquivalentTo([shared.Id, ownedByA.Id]);

        await using (var asB = CreateContext(_userB))
            (await asB.ResearchDocuments.Select(d => d.Id).ToListAsync()).Should().Equal(shared.Id);

        await using var asNoOne = CreateContext();
        (await asNoOne.ResearchDocuments.Select(d => d.Id).ToListAsync()).Should().Equal(shared.Id);
        (await new ResearchDocumentRepository(asNoOne).ListByStatusUnscopedAsync(ResearchIndexStatus.Pending, 10))
            .Select(d => d.Id).Should().BeEquivalentTo([shared.Id, ownedByA.Id], "the indexer works over the whole corpus");
    }

    [DockerRequiredFact]
    public async Task Thesis_and_watchlist_reads_follow_the_acting_person_and_the_unscoped_reads_serve_jobs()
    {
        await SeedAsync(NewThesis(_userA), NewThesis(_userB),
            new WatchlistItem { UserId = _userA, Ticker = "AAPL" });

        await using (var asA = CreateContext(_userA))
        {
            var theses = new ThesisRepository(asA);
            (await theses.ListAsync(_userA)).Should().ContainSingle();
            (await theses.ListAsync(_userB)).Should().BeEmpty("naming another person does not lift the owner scope");
        }

        await using var asNoOne = CreateContext();
        var noPersonTheses = new ThesisRepository(asNoOne);
        (await noPersonTheses.ListAsync(_userA)).Should().BeEmpty();
        (await noPersonTheses.ListUnscopedAsync(_userA)).Should().ContainSingle(t => t.UserId == _userA);
        (await noPersonTheses.GetUserIdsWithThesesUnscopedAsync()).Should().Contain([_userA, _userB]);

        var noPersonWatchlist = new WatchlistRepository(asNoOne);
        (await noPersonWatchlist.ListAsync(_userA)).Should().BeEmpty();
        (await noPersonWatchlist.ListUnscopedAsync(_userA)).Should().ContainSingle(w => w.Ticker == "AAPL");
        (await noPersonWatchlist.ListUnscopedAsync(_userB)).Should().BeEmpty("each opted-out read keeps its UserId predicate");
    }

    [DockerRequiredFact]
    public async Task Ips_reads_follow_the_acting_person_and_the_unscoped_reads_serve_jobs()
    {
        await SeedAsync(NewIps(_userA), NewIps(_userB));

        await using (var asA = CreateContext(_userA))
        {
            var repository = new IpsRepository(asA);
            (await repository.GetCurrentAsync(_userA)).Should().NotBeNull();
            (await repository.GetCurrentAsync(_userB)).Should().BeNull();
        }

        await using var asNoOne = CreateContext();
        var noPerson = new IpsRepository(asNoOne);
        (await noPerson.GetCurrentAsync(_userA)).Should().BeNull();
        (await noPerson.GetCurrentUnscopedAsync(_userA))!.UserId.Should().Be(_userA);
        (await noPerson.ListVersionsUnscopedAsync(_userB)).Should().ContainSingle(x => x.UserId == _userB);
        (await noPerson.GetUserIdsWithCurrentIpsUnscopedAsync()).Should().Contain([_userA, _userB]);
    }

    [DockerRequiredFact]
    public async Task Thesis_event_unscoped_reads_serve_the_snapshot_job_and_the_recorder_without_a_person()
    {
        var subjectA = Guid.NewGuid();
        var pending = NewEvent(_userA, subjectA, pricesPending: true);
        await SeedAsync(pending, NewEvent(_userB, Guid.NewGuid()));

        await using (var ctx = CreateContext())
        {
            var noPerson = new ThesisEventRepository(ctx);
            (await noPerson.ListAsync(_userA)).Should().BeEmpty();
            (await noPerson.ListUnscopedAsync(_userA)).Should().ContainSingle(e => e.Id == pending.Id);
            (await noPerson.ListPendingUnscopedAsync()).Select(e => e.Id).Should().Contain(pending.Id);
            (await noPerson.GetUserIdsWithEventsUnscopedAsync()).Should().Contain([_userA, _userB]);
            (await noPerson.GetLatestForSubjectUnscopedAsync(_userA, ThesisSubjectType.Thesis, subjectA))!
                .Id.Should().Be(pending.Id, "the recorder's one-Created check must find the existing event");
            (await noPerson.GetLatestForSubjectUnscopedAsync(_userB, ThesisSubjectType.Thesis, subjectA))
                .Should().BeNull("the opted-out read keeps its UserId predicate");

            pending.SubjectPrice = 101m;
            pending.PricesPending = false;
            await noPerson.UpdatePricesAsync(pending);
        }

        await using var asA = CreateContext(_userA);
        var stored = await asA.ThesisEvents.SingleAsync();
        stored.SubjectPrice.Should().Be(101m, "the price backfill runs with no person and must still land");
        stored.PricesPending.Should().BeFalse();
    }

    [DockerRequiredFact]
    public async Task Upserts_with_no_person_update_the_existing_row_instead_of_inserting_a_duplicate()
    {
        var thesis = NewThesis(_userA);
        await SeedAsync(thesis);

        await using (var ctx = CreateContext())
        {
            await new ThesisRepository(ctx).UpsertAsync(new InvestmentThesis
            {
                Id = thesis.Id, UserId = _userA, Ticker = "AAPL", ThesisText = "Revised.",
            });

            var candidates = new CandidateRepository(ctx);
            var first = await candidates.UpsertActiveAsync(_userA, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));
            var second = await candidates.UpsertActiveAsync(_userA, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));
            first.IsNew.Should().BeTrue();
            second.IsNew.Should().BeFalse("a filtered existence check would find nothing and insert a duplicate");
            second.Candidate.Id.Should().Be(first.Candidate.Id);
        }

        await using var asA = CreateContext(_userA);
        (await asA.Theses.SingleAsync()).ThesisText.Should().Be("Revised.");
        (await asA.OpportunityCandidates.CountAsync()).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Candidate_expiry_sweep_with_no_person_sees_every_users_expired_candidates()
    {
        var past = DateTimeOffset.UtcNow.AddDays(-1);
        await SeedAsync(
            new OpportunityCandidate { UserId = _userA, Ticker = "AAPL", ExpiresAt = past },
            new OpportunityCandidate { UserId = _userB, Ticker = "MSFT", ExpiresAt = past });

        await using var ctx = CreateContext();
        var expired = await new CandidateRepository(ctx).ListExpiredUnscopedAsync(DateTimeOffset.UtcNow);

        expired.Select(c => c.UserId).Should().Contain([_userA, _userB]);
    }

    [DockerRequiredFact]
    public async Task Corpus_source_reader_with_no_person_loads_every_users_theses_with_their_owner()
    {
        var a = NewThesis(_userA);
        var b = NewThesis(_userB, "MSFT");
        await SeedAsync(a, b);

        await using var ctx = CreateContext();
        var documents = await new ResearchCorpusSourceReader(ctx).LoadSourceDocumentsAsync();

        documents.Where(d => d.SourceType == ResearchDocumentSourceType.InvestmentThesis)
            .Select(d => (d.SourceId, d.UserId))
            .Should().BeEquivalentTo([(a.Id.ToString(), (Guid?)_userA), (b.Id.ToString(), (Guid?)_userB)]);
    }
}
