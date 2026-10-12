namespace FinanceSentry.Modules.Research.Tests.Companion;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>
/// Dedup identity + richer-record merge for analyst actions (feature 030, T018/FR-003). Two sources
/// reporting the same (ticker, firm, date, type) must collapse to one row, keeping the richer fields.
/// </summary>
public sealed class AnalystActionRepositoryDedupTests
{
    private static readonly DateOnly ActionDate = new(2026, 7, 20);

    [Fact]
    public async Task Upsert_collapses_same_logical_action_from_two_sources_into_one_richer_row()
    {
        using var db = CompanionTestContext.Create();
        var repo = new AnalystActionRepository(db);

        // MarketBeat: has price targets, but no ratings. Yahoo: has ratings, no targets. Same action.
        var marketbeat = Action("MU", "Morgan Stanley", AnalystActionType.Upgrade,
            priorRating: null, newRating: null, priorTarget: 98m, newTarget: 135m, source: "marketbeat");
        var yahoo = Action("MU", "Morgan Stanley", AnalystActionType.Upgrade,
            priorRating: "Equal-Weight", newRating: "Overweight", priorTarget: null, newTarget: null, source: "yahoo");

        var inserted = await repo.UpsertAsync([marketbeat, yahoo]);

        inserted.Should().Be(1);
        db.AnalystActions.Should().HaveCount(1);

        var row = db.AnalystActions.Single();
        row.PriorTarget.Should().Be(98m);
        row.NewTarget.Should().Be(135m);
        row.PriorRating.Should().Be("Equal-Weight");
        row.NewRating.Should().Be("Overweight");
    }

    [Fact]
    public async Task Upsert_merges_into_existing_stored_row_without_duplicating()
    {
        using var db = CompanionTestContext.Create();
        var repo = new AnalystActionRepository(db);

        await repo.UpsertAsync([Action("MU", "Morgan Stanley", AnalystActionType.Upgrade,
            null, null, 98m, 135m, "marketbeat")]);

        var second = await repo.UpsertAsync([Action("MU", "Morgan Stanley", AnalystActionType.Upgrade,
            "Equal-Weight", "Overweight", null, null, "yahoo")]);

        second.Should().Be(0, "the same logical action already exists");
        db.AnalystActions.Should().HaveCount(1);
        db.AnalystActions.Single().NewRating.Should().Be("Overweight");
    }

    [Fact]
    public async Task Upsert_keeps_distinct_action_types_as_separate_rows()
    {
        using var db = CompanionTestContext.Create();
        var repo = new AnalystActionRepository(db);

        var inserted = await repo.UpsertAsync(
        [
            Action("MU", "Morgan Stanley", AnalystActionType.Upgrade, null, "Overweight", null, 135m, "yahoo"),
            Action("MU", "Morgan Stanley", AnalystActionType.TargetChange, null, null, 120m, 140m, "marketbeat"),
        ]);

        inserted.Should().Be(2);
        db.AnalystActions.Should().HaveCount(2);
    }

    private static AnalystAction Action(
        string ticker, string firm, AnalystActionType type,
        string? priorRating, string? newRating, decimal? priorTarget, decimal? newTarget, string source)
        => new()
        {
            Ticker = ticker,
            Firm = firm,
            ActionType = type,
            PriorRating = priorRating,
            NewRating = newRating,
            PriorTarget = priorTarget,
            NewTarget = newTarget,
            ActionDate = ActionDate,
            Source = source,
        };
}
