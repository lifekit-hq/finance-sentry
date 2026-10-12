namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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

    // --- analyst actions ------------------------------------------------------------------------------------

    private static AnalystAction Action(
        AnalystActionType type, string? priorRating, string? newRating, decimal? priorTarget, decimal? newTarget,
        string source) => new()
    {
        Ticker = "MU", Firm = "Morgan Stanley", ActionType = type, PriorRating = priorRating, NewRating = newRating,
        PriorTarget = priorTarget, NewTarget = newTarget, ActionDate = Day, Source = source,
    };

    [DockerRequiredFact]
    public async Task Action_from_two_sources_in_one_batch_collapses_into_one_richer_row()
    {
        await using var ctx = CreateContext();

        // MarketBeat has price targets but no ratings; Yahoo has ratings but no targets. Same action.
        var inserted = await new AnalystActionRepository(ctx).UpsertAsync(
        [
            Action(AnalystActionType.Upgrade, null, null, 98m, 135m, "marketbeat"),
            Action(AnalystActionType.Upgrade, "Equal-Weight", "Overweight", null, null, "yahoo"),
        ]);

        inserted.Should().Be(1);
        await using var read = CreateContext();
        var row = await read.AnalystActions.AsNoTracking().SingleAsync();
        row.PriorTarget.Should().Be(98m);
        row.NewTarget.Should().Be(135m);
        row.PriorRating.Should().Be("Equal-Weight");
        row.NewRating.Should().Be("Overweight");
    }

    [DockerRequiredFact]
    public async Task Action_repeated_in_a_later_batch_fills_only_the_null_fields_of_the_stored_row()
    {
        await using (var ctx = CreateContext())
        {
            await new AnalystActionRepository(ctx).UpsertAsync(
                [Action(AnalystActionType.Upgrade, null, null, 98m, 135m, "marketbeat")]);
        }

        int second;
        await using (var ctx = CreateContext())
        {
            second = await new AnalystActionRepository(ctx).UpsertAsync(
                [Action(AnalystActionType.Upgrade, "Equal-Weight", "Overweight", 1m, 2m, "yahoo")]);
        }

        second.Should().Be(0, "the same logical action already exists");
        await using var read = CreateContext();
        var row = await read.AnalystActions.AsNoTracking().SingleAsync();
        row.NewRating.Should().Be("Overweight", "a NULL field is filled");
        row.PriorTarget.Should().Be(98m, "a populated field is never overwritten");
        row.NewTarget.Should().Be(135m);
        row.Source.Should().Be("marketbeat");
    }

    [DockerRequiredFact]
    public async Task Action_distinct_types_are_separate_rows_and_concurrent_ingests_do_not_fail()
    {
        await using (var ctx = CreateContext())
        {
            var inserted = await new AnalystActionRepository(ctx).UpsertAsync(
            [
                Action(AnalystActionType.Upgrade, null, "Overweight", null, 135m, "yahoo"),
                Action(AnalystActionType.TargetChange, null, null, 120m, 140m, "marketbeat"),
            ]);
            inserted.Should().Be(2);
        }

        var writes = Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = CreateContext();
            await new AnalystActionRepository(ctx).UpsertAsync(
                [Action(AnalystActionType.Downgrade, "Buy", "Hold", null, null, "yahoo")]);
        });
        await Task.WhenAll(writes);

        await using var read = CreateContext();
        (await read.AnalystActions.AsNoTracking().CountAsync()).Should().Be(3);
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
}
