namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Issue #626, root cause: <see cref="QuoteCacheRepository"/> shares the caller's scoped
/// <c>ResearchDbContext</c>, and every caller treats a quote refresh as best-effort and swallows
/// its failure. A failed cache write that left its rows staged in the change tracker therefore
/// survived the swallow, and the caller's *next* <c>SaveChangesAsync</c> re-attempted it and threw
/// — which is how a quote-cache fault surfaced as <c>save_thesis</c> failing on every call while
/// every read succeeded.
/// </summary>
public sealed class QuoteCacheUnitOfWorkTests
{
    /// <summary>Longer than the 20-char limit the model declares, so the insert is refused.</summary>
    private const string OverLongTicker = "THIS-TICKER-IS-FAR-TOO-LONG-TO-STORE";

    private static QuoteCacheEntry Quote(string ticker) => new()
    {
        Ticker = ticker,
        Price = 2.09m,
        Currency = "USD",
        FetchedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task AFailedCacheWrite_LeavesNothingStaged_SoTheNextSaveOnTheSharedContextSucceeds()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        await using var ctx = fixture.CreateContext();
        var cache = new QuoteCacheRepository(ctx);

        var write = async () => await cache.UpsertManyAsync([Quote(OverLongTicker)], CancellationToken.None);
        await write.Should().ThrowAsync<DbUpdateException>("the constraint must refuse the row");

        ctx.ChangeTracker.Entries<QuoteCacheEntry>()
            .Should().NotContain(e => e.State == EntityState.Added,
                "a rejected cache row must not stay staged in the caller's unit of work");

        // The caller swallowed the cache failure and carries on with its own write — as
        // ThesisEventRecorder.TryGetPricesAsync does between the thesis write and the event write.
        var thesis = new InvestmentThesis { UserId = Guid.NewGuid(), Ticker = "XRP", ThesisText = "watch" };
        var carryOn = async () => await new ThesisRepository(ctx).UpsertAsync(thesis, CancellationToken.None);

        await carryOn.Should().NotThrowAsync(
            "the cache failure must not be contagious — this is the #626 failure verbatim");

        (await new ThesisRepository(ctx).FindByTickerAsync(thesis.UserId, "XRP", CancellationToken.None))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task AFailedCacheWrite_DoesNotLeakTheAttemptedValues_IntoALaterSave()
    {
        await using var fixture = await ThesisSqliteFixture.CreateAsync();
        await using var ctx = fixture.CreateContext();
        var cache = new QuoteCacheRepository(ctx);

        await cache.UpsertManyAsync([Quote("SPY")], CancellationToken.None);

        // One good row and one that the constraint refuses: the whole call fails, so the update to
        // SPY must be rolled back too rather than riding along on someone else's SaveChanges.
        var updated = Quote("SPY");
        updated.Price = 999m;
        var write = async () => await cache.UpsertManyAsync(
            [updated, Quote(OverLongTicker)], CancellationToken.None);
        await write.Should().ThrowAsync<DbUpdateException>();

        await new ThesisRepository(ctx).UpsertAsync(
            new InvestmentThesis { UserId = Guid.NewGuid(), Ticker = "XRP", ThesisText = "watch" },
            CancellationToken.None);

        await using var readCtx = fixture.CreateContext();
        var persisted = await readCtx.QuoteCache.SingleAsync(q => q.Ticker == "SPY");
        persisted.Price.Should().Be(2.09m,
            "the rejected batch's update must not be committed by the next unrelated SaveChanges");
    }
}
