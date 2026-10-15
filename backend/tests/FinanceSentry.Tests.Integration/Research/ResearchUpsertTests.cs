namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

/// <summary>
/// The Research upserts are each one <c>INSERT ... ON CONFLICT</c> on the table's existing unique index: a repeated
/// write of one key keeps one row and updates (or keeps) it, and writers racing on one key do not fail on the
/// index. The in-memory provider enforces neither the index nor raw SQL, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ResearchUpsertTests : IAsyncLifetime
{
    private static readonly DateOnly Day = new(2026, 7, 20);

    private readonly Guid _user = Guid.NewGuid();
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

    private ResearchDbContext CreateContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(actingUser ?? _user));

    // --- macro calendar -------------------------------------------------------------------------------------

    private static MacroEvent Macro(string name, string importance, string source, TimeOnly? time = null) => new()
    {
        EventDate = Day, Region = "US", Event = name, Importance = importance, Source = source, EventTime = time,
    };

    [DockerRequiredFact]
    public async Task Macro_repeated_event_keeps_one_row_updated_and_counts_only_inserts()
    {
        await using (var ctx = CreateContext())
        {
            var inserted = await new MacroCalendarRepository(ctx)
                .UpsertAsync([Macro("CPI", "medium", "a"), Macro("NFP", "high", "a")]);
            inserted.Should().Be(2);
        }

        await using (var ctx = CreateContext())
        {
            var inserted = await new MacroCalendarRepository(ctx)
                .UpsertAsync([Macro("CPI", "high", "b", new TimeOnly(13, 30)), Macro("FOMC", "high", "b")]);
            inserted.Should().Be(1, "CPI already existed");
        }

        await using var read = CreateContext();
        var rows = await read.MacroEvents.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(3);
        var cpi = rows.Single(r => r.Event == "CPI");
        cpi.Importance.Should().Be("high");
        cpi.Source.Should().Be("b");
        cpi.EventTime.Should().Be(new TimeOnly(13, 30));
    }

    [DockerRequiredFact]
    public async Task Macro_sync_corrects_the_misdated_first_seed_and_leaves_unknown_rows_alone()
    {
        // #1091: a database seeded by the first (wrong) schedule — CPI (Sep) on 15 Oct, the FOMC a day late — was
        // never corrected because the seed only ran into an empty table.
        await using (var ctx = CreateContext())
        {
            ctx.MacroEvents.AddRange(
                new MacroEvent
                {
                    EventDate = new DateOnly(2026, 10, 15), EventTime = new TimeOnly(8, 30), Event = "CPI (Sep)",
                    Region = "US", Importance = MacroImportance.High,
                    Source = "https://www.bls.gov/schedule/news_release/cpi.htm",
                },
                new MacroEvent
                {
                    EventDate = new DateOnly(2026, 10, 29), EventTime = new TimeOnly(14, 0), Event = "FOMC rate decision",
                    Region = "US", Importance = MacroImportance.High,
                    Source = "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm",
                },
                new MacroEvent
                {
                    EventDate = new DateOnly(2026, 10, 22), Event = "PMI (Oct)", Region = "US",
                    Importance = MacroImportance.Medium, Source = "manual",
                });
            await ctx.SaveChangesAsync();
        }

        var now = new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.Zero);
        MacroCalendarSyncResult result;
        await using (var ctx = CreateContext())
        {
            result = await new MacroCalendarService(
                new MacroCalendarRepository(ctx), ctx, NullLogger<MacroCalendarService>.Instance).SyncAsync(now);
        }

        result.PastButUpcoming.Should().ContainSingle().Which.Key.Should().Be("macro:CPI:2026-10");
        result.Corrected.Should().Be(2);
        result.Removed.Should().Be(0);
        result.Inserted.Should().Be(MacroCalendarSchedule.Events().Count - 2);

        await using var read = CreateContext();
        var rows = await read.MacroEvents.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle(r => r.Event == "CPI (Sep)").Which.EventDate.Should().Be(new DateOnly(2026, 10, 14));
        rows.Should().ContainSingle(r => r.Event == "FOMC rate decision" && r.EventDate.Month == 10)
            .Which.EventDate.Should().Be(new DateOnly(2026, 10, 28));
        rows.Should().ContainSingle(r => r.Event == "PMI (Oct)");

        await using (var ctx = CreateContext())
        {
            var again = await new MacroCalendarService(
                new MacroCalendarRepository(ctx), ctx, NullLogger<MacroCalendarService>.Instance).SyncAsync(now);
            again.Should().BeEquivalentTo(new MacroCalendarSyncResult(0, 0, 0, []));
        }
    }

    [DockerRequiredFact]
    public async Task Macro_event_repeated_in_one_batch_keeps_the_last_copy()
    {
        await using (var ctx = CreateContext())
        {
            var inserted = await new MacroCalendarRepository(ctx).UpsertAsync(
            [
                Macro("CPI", "medium", "first", new TimeOnly(8, 30)),
                Macro("CPI", "high", "revised", new TimeOnly(13, 30)),
            ]);
            inserted.Should().Be(1);
        }

        await using var read = CreateContext();
        var row = await read.MacroEvents.AsNoTracking().SingleAsync();
        row.Importance.Should().Be("high");
        row.Source.Should().Be("revised");
        row.EventTime.Should().Be(new TimeOnly(13, 30));
    }

    [DockerRequiredFact]
    public async Task Macro_concurrent_upserts_of_one_event_leave_one_row()
    {
        var writes = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new MacroCalendarRepository(ctx).UpsertAsync([Macro("CPI", "high", $"s{i}")]);
        });

        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.MacroEvents.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // --- quote cache ----------------------------------------------------------------------------------------

    private static QuoteCacheEntry Quote(string ticker, decimal price = 2.09m) => new()
    {
        Ticker = ticker, Price = price, Currency = "USD", FetchedAt = DateTimeOffset.UtcNow,
    };

    [DockerRequiredFact]
    public async Task Quote_refresh_overwrites_the_session_metadata_not_just_the_price()
    {
        await using (var ctx = CreateContext())
        {
            var original = Quote("BTC-USD");
            original.Session = "regular";
            await new QuoteCacheRepository(ctx).UpsertManyAsync([original]);
        }

        var pricedAt = new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        await using (var ctx = CreateContext())
        {
            var refreshed = Quote("BTC-USD", 3m);
            refreshed.ResolvedTicker = "BTC-USD";
            refreshed.PreviousClose = 2.5m;
            refreshed.MarketState = "REGULAR";
            refreshed.Session = "unknown";
            refreshed.IsStale = true;
            refreshed.SourcePriceTime = pricedAt;
            refreshed.RegularMarketTime = pricedAt;
            await new QuoteCacheRepository(ctx).UpsertManyAsync([refreshed, Quote("SPY")]);
        }

        await using var read = CreateContext();
        (await read.QuoteCache.AsNoTracking().CountAsync()).Should().Be(2);
        var row = await read.QuoteCache.AsNoTracking().SingleAsync(q => q.Ticker == "BTC-USD");
        row.Price.Should().Be(3m);
        row.PreviousClose.Should().Be(2.5m);
        row.ResolvedTicker.Should().Be("BTC-USD");
        row.MarketState.Should().Be("REGULAR");
        row.Session.Should().Be("unknown");
        row.IsStale.Should().BeTrue();
        row.SourcePriceTime.Should().Be(pricedAt);
        row.RegularMarketTime.Should().Be(pricedAt);
    }

    [DockerRequiredFact]
    public async Task Quote_failed_refresh_stages_nothing_so_the_next_save_on_the_shared_context_succeeds()
    {
        // Issue #626: a swallowed cache failure must not poison the caller's unit of work.
        await using var ctx = CreateContext();

        var write = async () => await new QuoteCacheRepository(ctx).UpsertManyAsync(
            [Quote("THIS-TICKER-IS-FAR-TOO-LONG-TO-STORE")]);
        await write.Should().ThrowAsync<PostgresException>("the column refuses the over-long ticker");

        ctx.ChangeTracker.Entries<QuoteCacheEntry>().Should().BeEmpty();

        var thesis = new InvestmentThesis { UserId = _user, Ticker = "XRP", ThesisText = "watch" };
        await new ThesisRepository(ctx).UpsertAsync(thesis);

        (await new ThesisRepository(ctx).FindByTickerAsync(_user, "XRP")).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task Quote_concurrent_refreshes_of_one_ticker_leave_one_row()
    {
        var writes = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new QuoteCacheRepository(ctx).UpsertManyAsync([Quote("SPY", i)]);
        });

        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.QuoteCache.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // --- analyst universe -----------------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task Universe_relisted_member_is_reactivated_with_the_new_reason_in_one_row()
    {
        await using (var ctx = CreateContext())
        {
            var repo = new AnalystUniverseRepository(ctx);
            await repo.UpsertMembersAsync([new AnalystUniverseMember { Ticker = "MU", Reason = UniverseReason.Watchlist }]);
            await repo.DeactivateAsync(["MU"]);
        }

        await using (var ctx = CreateContext())
        {
            await new AnalystUniverseRepository(ctx).UpsertMembersAsync(
                [new AnalystUniverseMember { Ticker = "MU", Reason = UniverseReason.Holding }]);
        }

        await using var read = CreateContext();
        var row = await read.AnalystUniverseMembers.AsNoTracking().SingleAsync();
        row.Active.Should().BeTrue();
        row.Reason.Should().Be(UniverseReason.Holding);
    }

    [DockerRequiredFact]
    public async Task Universe_concurrent_refreshes_leave_one_row_per_ticker()
    {
        var writes = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            await new AnalystUniverseRepository(ctx).UpsertMembersAsync(
            [
                new AnalystUniverseMember { Ticker = "MU", Reason = UniverseReason.Holding },
                new AnalystUniverseMember { Ticker = "NVDA", Reason = UniverseReason.Watchlist },
            ]);
        });

        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.AnalystUniverseMembers.AsNoTracking().CountAsync()).Should().Be(2);
    }

    // --- asset ledger read ----------------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task LedgerRead_regenerated_for_one_symbol_keeps_one_row_with_the_new_narrative()
    {
        var first = new AssetLedgerRead { UserId = _user, Symbol = " aapl ", Narrative = "one", SourceFingerprint = "f1" };
        await using (var ctx = CreateContext())
            await new AssetLedgerReadRepository(ctx).UpsertAsync(first);

        var regenerated = new AssetLedgerRead
        {
            UserId = _user, Symbol = "AAPL", Narrative = "two", SourceFingerprint = "f2",
            GeneratedAt = first.GeneratedAt.AddHours(1),
        };
        await using (var ctx = CreateContext())
            await new AssetLedgerReadRepository(ctx).UpsertAsync(regenerated);

        await using var read = CreateContext();
        var row = await read.AssetLedgerReads.AsNoTracking().SingleAsync();
        row.Id.Should().Be(first.Id, "the stored row is replaced in place");
        row.Symbol.Should().Be("AAPL");
        row.Narrative.Should().Be("two");
        row.SourceFingerprint.Should().Be("f2");
        row.GeneratedAt.Should().BeCloseTo(regenerated.GeneratedAt, TimeSpan.FromMilliseconds(1));
    }

    [DockerRequiredFact]
    public async Task LedgerRead_concurrent_regenerations_leave_one_row()
    {
        var writes = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new AssetLedgerReadRepository(ctx).UpsertAsync(
                new AssetLedgerRead { UserId = _user, Symbol = "AAPL", Narrative = $"n{i}", SourceFingerprint = "f" });
        });

        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.AssetLedgerReads.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task PriceTarget_set_again_keeps_one_row_with_the_new_price_and_delete_removes_it()
    {
        var first = new AssetPriceTarget { UserId = _user, Symbol = " aapl ", TargetPrice = 200m, Currency = "USD" };
        await using (var ctx = CreateContext())
            await new AssetPriceTargetRepository(ctx).UpsertAsync(first);

        await using (var ctx = CreateContext())
            await new AssetPriceTargetRepository(ctx).UpsertAsync(
                new AssetPriceTarget { UserId = _user, Symbol = "AAPL", TargetPrice = 245.5m, Currency = "USD" });

        await using (var read = CreateContext())
        {
            var row = await read.AssetPriceTargets.AsNoTracking().SingleAsync();
            row.Id.Should().Be(first.Id, "the stored row is replaced in place");
            row.Symbol.Should().Be("AAPL");
            row.TargetPrice.Should().Be(245.5m);
        }

        await using (var ctx = CreateContext())
            (await new AssetPriceTargetRepository(ctx).DeleteAsync(_user, "aapl")).Should().BeTrue();

        await using var after = CreateContext();
        (await after.AssetPriceTargets.AnyAsync()).Should().BeFalse();
    }

    // --- recommendation trends ------------------------------------------------------------------------------

    private static RecommendationTrend Trend(string ticker, DateOnly period, int buy, string source) => new()
    {
        Ticker = ticker, Period = period, Buy = buy, Hold = 2, Source = source,
    };

    [DockerRequiredFact]
    public async Task Trend_repeated_period_keeps_one_row_updated_and_counts_only_inserts()
    {
        await using (var ctx = CreateContext())
        {
            var inserted = await new RecommendationTrendRepository(ctx)
                .UpsertAsync([Trend("MU", Day, 5, "a"), Trend("MU", Day.AddMonths(-1), 4, "a")]);
            inserted.Should().Be(2);
        }

        await using (var ctx = CreateContext())
        {
            var inserted = await new RecommendationTrendRepository(ctx)
                .UpsertAsync([Trend("MU", Day, 9, "b"), Trend("NVDA", Day, 7, "b")]);
            inserted.Should().Be(1, "(MU, Day) already existed");
        }

        await using var read = CreateContext();
        (await read.RecommendationTrends.AsNoTracking().CountAsync()).Should().Be(3);
        var row = await read.RecommendationTrends.AsNoTracking().SingleAsync(t => t.Ticker == "MU" && t.Period == Day);
        row.Buy.Should().Be(9);
        row.Source.Should().Be("b");
    }

    [DockerRequiredFact]
    public async Task Trend_concurrent_ingests_of_one_period_leave_one_row()
    {
        var writes = Enumerable.Range(0, 8).Select(async i =>
        {
            await using var ctx = CreateContext();
            await new RecommendationTrendRepository(ctx).UpsertAsync([Trend("MU", Day, i, $"s{i}")]);
        });

        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.RecommendationTrends.AsNoTracking().CountAsync()).Should().Be(1);
    }

    // --- opportunity candidates -----------------------------------------------------------------------------

    [DockerRequiredFact]
    public async Task Candidate_concurrent_upserts_of_one_ticker_leave_one_active_row_and_one_new()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            return await new CandidateRepository(ctx)
                .UpsertActiveAsync(_user, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));
        }));

        results.Count(r => r.IsNew).Should().Be(1);
        results.Select(r => r.Candidate.Id).Distinct().Should().ContainSingle();

        await using var read = CreateContext();
        (await read.OpportunityCandidates.AsNoTracking().CountAsync(c => c.Status == CandidateStatus.Active))
            .Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Candidate_reactivated_after_leaving_active_inserts_a_new_row()
    {
        Guid firstId;
        await using (var ctx = CreateContext())
        {
            var repo = new CandidateRepository(ctx);
            var (first, isNew) = await repo.UpsertActiveAsync(_user, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));
            isNew.Should().BeTrue();
            firstId = first.Id;
            first.Status = CandidateStatus.Expired;
            await repo.UpdateAsync(first);
        }

        await using (var ctx = CreateContext())
        {
            var (second, isNew) = await new CandidateRepository(ctx)
                .UpsertActiveAsync(_user, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));
            isNew.Should().BeTrue("the partial index only covers Active rows");
            second.Id.Should().NotBe(firstId);
        }

        await using var read = CreateContext();
        var rows = await read.OpportunityCandidates.AsNoTracking().ToListAsync();
        rows.Should().HaveCount(2);
        rows.Count(c => c.Status == CandidateStatus.Active).Should().Be(1);
    }

    [DockerRequiredFact]
    public async Task Candidate_active_row_of_one_user_does_not_block_another_users_same_ticker()
    {
        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext(Guid.NewGuid());
        var other = Guid.NewGuid();

        var a = await new CandidateRepository(ctxA).UpsertActiveAsync(_user, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));
        var b = await new CandidateRepository(ctxB).UpsertActiveAsync(other, "NVDA", CandidateSource.Scan, TimeSpan.FromDays(30));

        a.IsNew.Should().BeTrue();
        b.IsNew.Should().BeTrue();
    }
}
