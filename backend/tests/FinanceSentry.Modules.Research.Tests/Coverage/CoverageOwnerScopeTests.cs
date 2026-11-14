namespace FinanceSentry.Modules.Research.Tests.Coverage;

using FinanceSentry.Modules.Research.Domain.Coverage;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>The coverage tables are per-user: a request sees its own rows, a job opts out and names the user.</summary>
public sealed class CoverageOwnerScopeTests
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static ResearchDbContext Context(string database, Guid? acting)
        => new(new DbContextOptionsBuilder<ResearchDbContext>().UseInMemoryDatabase(database).Options, new FixedCurrentUser(acting));

    [Fact]
    public async Task EachPersonSeesOnlyTheirCoverage_AndNoPersonSeesNone()
    {
        var database = $"coverage-scope-{Guid.NewGuid():N}";
        await using (var seed = Context(database, null))
        {
            seed.CoverageMembers.AddRange(
                new CoverageMember { UserId = UserA, Ticker = "NVDA", AddedReason = CoverageReason.Held },
                new CoverageMember { UserId = UserB, Ticker = "AVGO", AddedReason = CoverageReason.Watchlist });
            seed.CoverageJournal.AddRange(
                new CoverageJournalEntry { UserId = UserA, Ticker = "NVDA", Rule = CoverageRuleIds.Held, Detail = "d" },
                new CoverageJournalEntry { UserId = UserB, Ticker = "AVGO", Rule = CoverageRuleIds.Watchlist, Detail = "d" });
            seed.CoverageVerdictObservations.AddRange(
                new CoverageVerdictObservation { UserId = UserA, Ticker = "NVDA", Verdict = "Buy", FiredRuleId = "buy" },
                new CoverageVerdictObservation { UserId = UserB, Ticker = "AVGO", Verdict = "Wait", FiredRuleId = "otherwise" });
            await seed.SaveChangesAsync();
        }

        await using (var asA = Context(database, UserA))
        {
            (await asA.CoverageMembers.Select(m => m.Ticker).ToListAsync()).Should().Equal("NVDA");
            (await asA.CoverageJournal.Select(e => e.Ticker).ToListAsync()).Should().Equal("NVDA");
            (await asA.CoverageVerdictObservations.Select(o => o.Ticker).ToListAsync()).Should().Equal("NVDA");
        }

        await using var asNoOne = Context(database, null);
        (await asNoOne.CoverageMembers.AnyAsync()).Should().BeFalse();
        (await asNoOne.CoverageJournal.AnyAsync()).Should().BeFalse();
        (await asNoOne.CoverageVerdictObservations.AnyAsync()).Should().BeFalse();
        (await new CoverageRepository(asNoOne).ListMembersUnscopedAsync(UserB)).Select(m => m.Ticker).Should().Equal("AVGO");
    }

    [Fact]
    public async Task SavingAnEvaluationWithNoPerson_AddsUpdatesAndRemovesOnlyTheNamedUsersRows()
    {
        var database = $"coverage-save-{Guid.NewGuid():N}";
        await using var ctx = Context(database, null);
        var drop = new CoverageMember { UserId = UserA, Ticker = "DROP", AddedReason = CoverageReason.Screen };
        var keep = new CoverageMember { UserId = UserA, Ticker = "KEEP", AddedReason = CoverageReason.Screen };
        var other = new CoverageMember { UserId = UserB, Ticker = "DROP", AddedReason = CoverageReason.Screen };
        ctx.CoverageMembers.AddRange(drop, keep, other);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var repository = new CoverageRepository(ctx);
        var members = await repository.ListMembersUnscopedAsync(UserA);
        var kept = members.Single(m => m.Ticker == "KEEP");
        kept.CorrelationToBook = 0.31m;
        await repository.SaveEvaluationAsync(
            UserA,
            [new CoverageMember { UserId = UserA, Ticker = "NEW", AddedReason = CoverageReason.Held }],
            [kept],
            [members.Single(m => m.Ticker == "DROP")],
            [new CoverageJournalEntry { UserId = UserA, Ticker = "DROP", Action = CoverageAction.Removed, Rule = CoverageRuleIds.NoEvent, Detail = "d" }]);

        var after = await repository.ListMembersUnscopedAsync(UserA);
        after.Select(m => m.Ticker).Should().Equal("KEEP", "NEW");
        after.Single(m => m.Ticker == "KEEP").CorrelationToBook.Should().Be(0.31m);
        (await repository.ListMembersUnscopedAsync(UserB)).Should().ContainSingle(m => m.Ticker == "DROP");
        (await repository.ListJournalUnscopedAsync(UserA, "DROP", 5)).Should().ContainSingle();
    }

    [Fact]
    public async Task AnObservationForADayIsRecordedOnce()
    {
        await using var ctx = Context($"coverage-obs-{Guid.NewGuid():N}", null);
        var repository = new CoverageRepository(ctx);
        var day = new DateOnly(2026, 11, 14);

        (await repository.AddObservationUnscopedAsync(new CoverageVerdictObservation
        {
            UserId = UserA, Ticker = "MU", ObservedOn = day, Verdict = "Buy", FiredRuleId = "buy",
        })).Should().BeTrue();
        (await repository.AddObservationUnscopedAsync(new CoverageVerdictObservation
        {
            UserId = UserA, Ticker = "MU", ObservedOn = day, Verdict = "Avoid", FiredRuleId = "weak",
        })).Should().BeFalse();

        (await repository.ListObservationsUnscopedAsync(UserA, day)).Should().ContainSingle(o => o.Verdict == "Buy");
    }
}
