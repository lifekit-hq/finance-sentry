namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Domain.Coverage;
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

        perUser.Should().HaveCount(15);
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
    public async Task Price_targets_are_readable_and_removable_only_by_their_owner()
    {
        await SeedAsync(
            new AssetPriceTarget { UserId = _userA, Symbol = "AAPL", TargetPrice = 250m },
            new AssetPriceTarget { UserId = _userB, Symbol = "AAPL", TargetPrice = 180m });

        await using (var asA = CreateContext(_userA))
            (await new AssetPriceTargetRepository(asA).GetAsync(_userA, "aapl"))!.TargetPrice.Should().Be(250m);

        await using (var asB = CreateContext(_userB))
        {
            (await new AssetPriceTargetRepository(asB).GetAsync(_userA, "AAPL")).Should().BeNull(
                "the filter holds even when a read names another person explicitly");
            (await new AssetPriceTargetRepository(asB).DeleteAsync(_userA, "AAPL")).Should().BeFalse();
        }

        await using (var asNoOne = CreateContext())
            (await asNoOne.AssetPriceTargets.AnyAsync()).Should().BeFalse("no person in scope matches no row");

        await using var asA2 = CreateContext(_userA);
        (await asA2.AssetPriceTargets.Select(t => t.TargetPrice).ToListAsync()).Should().Equal(250m);
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
    public async Task Coverage_reads_follow_the_acting_person_and_the_unscoped_reads_serve_the_evaluation_job()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var memberA = new CoverageMember { UserId = _userA, Ticker = "NVDA", AddedReason = CoverageReason.Held };
        var memberB = new CoverageMember { UserId = _userB, Ticker = "NVDA", AddedReason = CoverageReason.Watchlist };
        await SeedAsync(
            memberA, memberB,
            new CoverageJournalEntry { UserId = _userA, Ticker = "NVDA", Action = CoverageAction.Added, Rule = CoverageRuleIds.Held, Reason = CoverageReason.Held, Detail = "Held in the book." },
            new CoverageJournalEntry { UserId = _userB, Ticker = "NVDA", Action = CoverageAction.Added, Rule = CoverageRuleIds.Watchlist, Reason = CoverageReason.Watchlist, Detail = "On the watchlist." },
            new CoverageVerdictObservation { UserId = _userA, Ticker = "NVDA", ObservedOn = today, Verdict = "Buy", FiredRuleId = "buy" },
            new CoverageVerdictObservation { UserId = _userB, Ticker = "NVDA", ObservedOn = today, Verdict = "Wait", FiredRuleId = "otherwise" });

        await using (var asA = CreateContext(_userA))
        {
            (await asA.CoverageMembers.Select(m => m.Id).ToListAsync()).Should().Equal(memberA.Id);
            (await asA.CoverageJournal.Select(e => e.UserId).ToListAsync()).Should().Equal(_userA);
            (await asA.CoverageVerdictObservations.Select(o => o.Verdict).ToListAsync()).Should().Equal("Buy");
            (await asA.CoverageMembers.AnyAsync(m => m.UserId == _userB)).Should().BeFalse(
                "the filter holds even when a query names another person explicitly");
        }

        await using var asNoOne = CreateContext();
        (await asNoOne.CoverageMembers.AnyAsync()).Should().BeFalse("no person in scope matches no row");
        (await asNoOne.CoverageJournal.AnyAsync()).Should().BeFalse();
        (await asNoOne.CoverageVerdictObservations.AnyAsync()).Should().BeFalse();

        var noPerson = new CoverageRepository(asNoOne);
        (await noPerson.ListMembersUnscopedAsync(_userA)).Should().ContainSingle(m => m.Id == memberA.Id);
        (await noPerson.ListMembersUnscopedAsync(_userB)).Should().ContainSingle(m => m.Id == memberB.Id, "each opted-out read keeps its UserId predicate");
        (await noPerson.ListUserIdsUnscopedAsync()).Should().Contain([_userA, _userB]);
        (await noPerson.ListJournalUnscopedAsync(_userA, "NVDA", 10)).Should().ContainSingle(e => e.UserId == _userA);
        (await noPerson.ListObservationsUnscopedAsync(_userA, today.AddDays(-1))).Should().ContainSingle(o => o.UserId == _userA);
        (await noPerson.AddObservationUnscopedAsync(new CoverageVerdictObservation
        {
            UserId = _userA, Ticker = "NVDA", ObservedOn = today, Verdict = "Avoid", FiredRuleId = "weak_structure",
        })).Should().BeFalse("a filtered existence check would find nothing and insert a duplicate for the day");
    }

    [DockerRequiredFact]
    public async Task Coverage_events_follow_the_acting_person_and_the_review_writes_are_scoped_to_the_named_user()
    {
        var at = DateTimeOffset.UtcNow;
        await SeedAsync(
            new CoverageMember { UserId = _userA, Ticker = "NVDA", AddedReason = CoverageReason.Held },
            new CoverageMember { UserId = _userB, Ticker = "NVDA", AddedReason = CoverageReason.Watchlist });

        CoverageEvent Event(string reference) => new()
        {
            Ticker = "NVDA", Source = "filing", Reference = reference, Summary = "10-Q filed", OccurredAt = at,
        };

        await using var asNoOne = CreateContext();
        var repository = new CoverageRepository(asNoOne);

        (await repository.AddEventsUnscopedAsync(_userA, [Event("acc-1")])).Should().ContainSingle();
        (await repository.AddEventsUnscopedAsync(_userA, [Event("acc-1")])).Should().BeEmpty(
            "a filtered existence check would find nothing and insert the repeat; the opted-out check sees it");
        (await repository.AddEventsUnscopedAsync(_userB, [Event("acc-1")])).Should().ContainSingle(
            "the identity is per person");

        await repository.TouchMembersUnscopedAsync(_userA, new Dictionary<string, DateTimeOffset> { ["NVDA"] = at });
        (await repository.ListMembersUnscopedAsync(_userA)).Single().LastEventAt.Should().NotBeNull();
        (await repository.ListMembersUnscopedAsync(_userB)).Single().LastEventAt.Should().BeNull(
            "a touch for one person never moves another's member");

        var (events, total, _, _) = await repository.ListEventsUnscopedAsync(_userA, at.AddMinutes(-1), 10);
        total.Should().Be(1);
        events.Should().ContainSingle().Which.UserId.Should().Be(_userA);

        await using (var asA = CreateContext(_userA))
        {
            (await asA.CoverageEvents.Select(e => e.UserId).ToListAsync()).Should().Equal(_userA);
        }

        (await asNoOne.CoverageEvents.AnyAsync()).Should().BeFalse("no person in scope matches no row");

        (await repository.PruneEventsUnscopedAsync(at.AddMinutes(1))).Should().Be(2);
    }

    [DockerRequiredFact]
    public async Task Catalyst_events_follow_the_acting_person_and_the_scan_writes_are_scoped_to_the_named_user()
    {
        var at = DateTimeOffset.UtcNow;
        var filed = DateOnly.FromDateTime(at.UtcDateTime);

        CatalystEvent Event(string accession) => new()
        {
            Ticker = "NVDA", Accession = accession, Form = "8-K", Items = "2.02", FilingDate = filed, Summary = "8-K item 2.02",
            Source = "fulltext", DetectedAt = at, Disposition = CatalystDisposition.Nominated, Detail = "nominated", DocumentUrl = string.Empty,
        };

        await using var asNoOne = CreateContext();
        var repository = new CatalystRepository(asNoOne);

        (await repository.AddEventsUnscopedAsync(_userA, [Event("acc-1")])).Should().Be(1);
        (await repository.AddEventsUnscopedAsync(_userA, [Event("acc-1")])).Should().Be(
            0, "a filtered existence check would find nothing and insert the repeat; the opted-out check sees it");
        (await repository.AddEventsUnscopedAsync(_userB, [Event("acc-1")])).Should().Be(1, "the identity is per person");

        (await repository.ListHandledUnscopedAsync(_userA, filed)).Should().BeEquivalentTo(["NVDA|acc-1"]);
        (await repository.ListHandledUnscopedAsync(_userA, filed.AddDays(1))).Should().BeEmpty();

        var (events, total) = await repository.ListEventsUnscopedAsync(_userA, at.AddMinutes(-1), 10);
        total.Should().Be(1);
        events.Should().ContainSingle().Which.UserId.Should().Be(_userA);

        await using (var asA = CreateContext(_userA))
        {
            (await asA.CatalystEvents.Select(e => e.UserId).ToListAsync()).Should().Equal(_userA);
        }

        (await asNoOne.CatalystEvents.AnyAsync()).Should().BeFalse("no person in scope matches no row");

        (await repository.PruneEventsUnscopedAsync(at.AddMinutes(1))).Should().Be(2);
    }

    [DockerRequiredFact]
    public async Task Coverage_event_digest_counts_removed_and_rescored_over_every_match_not_only_the_returned_page()
    {
        var at = DateTimeOffset.UtcNow;
        var removedTickers = new[] { "OLD1", "OLD2" };
        var tickers = new[] { "OLD1", "OLD2", "KEEP1", "KEEP2", "KEEP3" };
        await SeedAsync(tickers.Select((t, i) => new CoverageEvent
        {
            UserId = _userA, Ticker = t, Source = "filing", Reference = $"acc-{t}", Summary = "8-K filed",
            // OLD* are the oldest, so a page of the newest two (KEEP3, KEEP2) holds neither removal.
            OccurredAt = at.AddMinutes(i - 10).AddMinutes(removedTickers.Contains(t) ? -60 : 0),
            Disposition = removedTickers.Contains(t) ? CoverageEventDisposition.Removed : CoverageEventDisposition.Kept,
            Rescored = i != 2,
        }).Cast<object>().ToArray());
        await SeedAsync(new CoverageEvent
        {
            UserId = _userB, Ticker = "OTHER", Source = "filing", Reference = "acc-x", Summary = "x", OccurredAt = at,
            Disposition = CoverageEventDisposition.Removed, Rescored = true,
        });

        await using var asNoOne = CreateContext();
        var (events, total, removed, rescored) =
            await new CoverageRepository(asNoOne).ListEventsUnscopedAsync(_userA, at.AddDays(-1), 2);

        events.Select(e => e.Ticker).Should().Equal("KEEP3", "KEEP2");
        total.Should().Be(5);
        removed.Should().Be(2, "both removals sit outside the two newest rows but still count");
        rescored.Should().Be(4, "KEEP1 was not rescored; another person's row never counts");
    }

    [DockerRequiredFact]
    public async Task Coverage_evaluation_save_with_no_person_adds_updates_and_removes_only_the_named_users_rows()
    {
        var keep = new CoverageMember { UserId = _userA, Ticker = "KEEP", AddedReason = CoverageReason.Screen };
        var drop = new CoverageMember { UserId = _userA, Ticker = "DROP", AddedReason = CoverageReason.Screen };
        var other = new CoverageMember { UserId = _userB, Ticker = "DROP", AddedReason = CoverageReason.Screen };
        await SeedAsync(keep, drop, other);

        await using (var ctx = CreateContext())
        {
            var repository = new CoverageRepository(ctx);
            var tracked = (await repository.ListMembersUnscopedAsync(_userA)).Single(m => m.Ticker == "KEEP");
            tracked.CorrelationToBook = 0.31m;
            await repository.SaveEvaluationAsync(
                _userA,
                added: [new CoverageMember { UserId = _userA, Ticker = "NEW", AddedReason = CoverageReason.Held }],
                updated: [tracked],
                removed: [(await repository.ListMembersUnscopedAsync(_userA)).Single(m => m.Ticker == "DROP")],
                journal: [new CoverageJournalEntry
                {
                    UserId = _userA, Ticker = "DROP", Action = CoverageAction.Removed, Rule = CoverageRuleIds.NoEvent,
                    Reason = CoverageReason.Screen, Detail = "No event.",
                }]);
        }

        await using var verify = CreateContext();
        var members = await new CoverageRepository(verify).ListMembersUnscopedAsync(_userA);
        members.Select(m => m.Ticker).Should().Equal("KEEP", "NEW");
        members.Single(m => m.Ticker == "KEEP").CorrelationToBook.Should().Be(0.31m);
        (await new CoverageRepository(verify).ListMembersUnscopedAsync(_userB)).Should().ContainSingle(m => m.Ticker == "DROP",
            "another person's row for the same ticker is untouched");
        (await new CoverageRepository(verify).ListJournalUnscopedAsync(_userA, "DROP", 10)).Should().ContainSingle();
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
