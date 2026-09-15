namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Opportunity;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

/// <summary>
/// Universe compose-and-deactivate behaviour (feature 030, T019/FR-002): seed ∪ holdings ∪ watchlist
/// ∪ open candidates; ownership reasons win over IndexConstituent; departed members de-activate.
/// </summary>
public sealed class AnalystUniverseServiceTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Sync_composes_holdings_watchlist_candidates_and_seed_with_ownership_precedence()
    {
        using var db = CompanionTestContext.Create();
        db.WatchlistItems.Add(new WatchlistItem { UserId = User, Ticker = "TSLA" });
        db.OpportunityCandidates.Add(new OpportunityCandidate
        {
            UserId = User, Ticker = "PLTR", Source = CandidateSource.Ledger,
            Status = CandidateStatus.Active, ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();

        // AAPL is both a seed constituent and a holding — Holding must win.
        var holdings = new[]
        {
            new BrokerageHoldingSummary("AAPL", "STK", 10, 2000, DateTime.UtcNow, "ibkr"),
            new BrokerageHoldingSummary("BTC", "CRYPTO", 1, 50000, DateTime.UtcNow, "binance"),
        };
        var repo = new FakeAnalystUniverseRepository();
        var service = new AnalystUniverseService(
            repo, db, new FakeBrokerageReader(holdings), new FakeBankingTotalsReader(User),
            new Sp500ConstituentSource(), NullLogger<AnalystUniverseService>.Instance);

        var active = await service.SyncAsync();

        active.Should().Contain(m => m.Ticker == "AAPL" && m.Reason == UniverseReason.Holding);
        active.Should().Contain(m => m.Ticker == "TSLA" && m.Reason == UniverseReason.Watchlist);
        active.Should().Contain(m => m.Ticker == "PLTR" && m.Reason == UniverseReason.Candidate);
        active.Should().Contain(m => m.Ticker == "MU" && m.Reason == UniverseReason.IndexConstituent);
        active.Should().NotContain(m => m.Ticker == "BTC", "crypto holdings are not equities");
    }

    [Fact]
    public async Task Sync_deactivates_previously_active_member_no_longer_resolved()
    {
        using var db = CompanionTestContext.Create();
        var repo = new FakeAnalystUniverseRepository();
        // A stale auto member that isn't in the seed, holdings, watchlist, or candidates.
        repo.Members.Add(new AnalystUniverseMember
        {
            Ticker = "ZZZZ", Reason = UniverseReason.Watchlist, Active = true,
        });

        var service = new AnalystUniverseService(
            repo, db, new FakeBrokerageReader(), new FakeBankingTotalsReader(User),
            new Sp500ConstituentSource(), NullLogger<AnalystUniverseService>.Instance);

        await service.SyncAsync();

        repo.Deactivated.Should().Contain("ZZZZ");
        repo.Members.Single(m => m.Ticker == "ZZZZ").Active.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_never_deactivates_manual_members()
    {
        using var db = CompanionTestContext.Create();
        var repo = new FakeAnalystUniverseRepository();
        repo.Members.Add(new AnalystUniverseMember
        {
            Ticker = "MANL", Reason = UniverseReason.Manual, Active = true,
        });

        var service = new AnalystUniverseService(
            repo, db, new FakeBrokerageReader(), new FakeBankingTotalsReader(User),
            new Sp500ConstituentSource(), NullLogger<AnalystUniverseService>.Instance);

        await service.SyncAsync();

        repo.Deactivated.Should().NotContain("MANL");
    }
}
