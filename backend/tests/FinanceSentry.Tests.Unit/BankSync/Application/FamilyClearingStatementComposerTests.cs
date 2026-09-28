namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FluentAssertions;
using Xunit;

/// <summary>
/// Unit tests for the monthly family-clearing digest composition (#434 S5). Exercises
/// <see cref="FamilyClearingStatementComposer"/> directly — no Hangfire, no DB.
/// </summary>
public sealed class FamilyClearingStatementComposerTests
{
    private static CounterpartyStatementLine Line(
        string name, decimal receivedUsd, decimal sentUsd, bool? rentConfirmed = null) =>
        new(name, "family_support", receivedUsd, sentUsd, receivedUsd - sentUsd, [],
            RentConfirmed: rentConfirmed);

    private static string[] BodyLines(FamilyStatementBrief brief) => brief.Body.Split('\n');

    [Fact]
    public void Headline_IncludesMonthAndTotals()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [Line("Mom", 500m, 1200m)], SupportTotalUsd: 1200m, ReceivedTotalUsd: 500m, ExcludedRoutingLegs: 0);

        var brief = FamilyClearingStatementComposer.Compose(statement);

        brief.Headline.Should().Contain("2026-08");
        brief.Headline.Should().Contain("$500");
        brief.Headline.Should().Contain("$1.2k");
    }

    [Fact]
    public void Body_ContainsOneLinePerCounterparty()
    {
        var statement = new FamilyClearingStatement(
            "2026-08",
            [Line("Mom", 500m, 1200m), Line("Dad", 0m, 300m)],
            SupportTotalUsd: 1500m, ReceivedTotalUsd: 500m, ExcludedRoutingLegs: 0);

        var lines = BodyLines(FamilyClearingStatementComposer.Compose(statement));

        lines.Should().Contain(l => l.StartsWith("Mom:"));
        lines.Should().Contain(l => l.StartsWith("Dad:"));
    }

    [Fact]
    public void Body_NotesRentConfirmed()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [Line("Mom", 1200m, 0m, rentConfirmed: true)],
            SupportTotalUsd: 0m, ReceivedTotalUsd: 1200m, ExcludedRoutingLegs: 0);

        FamilyClearingStatementComposer.Compose(statement)
            .Body.Should().Contain("rent confirmed");
    }

    [Fact]
    public void Body_NotesRentShort_WhenNotConfirmed()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [Line("Mom", 800m, 0m, rentConfirmed: false)],
            SupportTotalUsd: 0m, ReceivedTotalUsd: 800m, ExcludedRoutingLegs: 0);

        FamilyClearingStatementComposer.Compose(statement)
            .Body.Should().Contain("rent short");
    }

    [Fact]
    public void Body_ReportsNoActivity_WhenNoCounterparties()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [], SupportTotalUsd: 0m, ReceivedTotalUsd: 0m, ExcludedRoutingLegs: 0);

        FamilyClearingStatementComposer.Compose(statement)
            .Body.Should().Contain("No family-support activity this month.");
    }

    [Fact]
    public void Body_IncludesTotalsLine()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [Line("Mom", 500m, 1200m)], SupportTotalUsd: 1200m, ReceivedTotalUsd: 500m, ExcludedRoutingLegs: 0);

        FamilyClearingStatementComposer.Compose(statement)
            .Body.Should().Contain("Total: $500 in, $1.2k out");
    }

    [Fact]
    public void Body_NotesExcludedRoutingLegs_WhenPresent()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [Line("Mom", 500m, 1200m)], SupportTotalUsd: 1200m, ReceivedTotalUsd: 500m, ExcludedRoutingLegs: 2);

        FamilyClearingStatementComposer.Compose(statement)
            .Body.Should().Contain("2 routing leg(s) excluded — not missing money.");
    }

    [Fact]
    public void Body_OmitsExcludedRoutingLegsLine_WhenNone()
    {
        var statement = new FamilyClearingStatement(
            "2026-08", [Line("Mom", 500m, 1200m)], SupportTotalUsd: 1200m, ReceivedTotalUsd: 500m, ExcludedRoutingLegs: 0);

        FamilyClearingStatementComposer.Compose(statement)
            .Body.Should().NotContain("excluded");
    }

    [Fact]
    public void TotalMessageLines_DoNotExceedTwelve_WithManyCounterparties()
    {
        var counterparties = Enumerable.Range(1, 20)
            .Select(i => Line($"Person{i}", 100m, 200m))
            .ToList();
        var statement = new FamilyClearingStatement(
            "2026-08", counterparties, SupportTotalUsd: 4000m, ReceivedTotalUsd: 2000m, ExcludedRoutingLegs: 1);

        var brief = FamilyClearingStatementComposer.Compose(statement);

        var totalLines = 1 + BodyLines(brief).Length;
        totalLines.Should().BeLessOrEqualTo(12);
        brief.Body.Should().Contain("more — ask for detail.");
    }
}
