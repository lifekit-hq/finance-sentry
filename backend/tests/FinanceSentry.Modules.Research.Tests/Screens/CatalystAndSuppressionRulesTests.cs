namespace FinanceSentry.Modules.Research.Tests.Screens;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Screens;
using FluentAssertions;
using Xunit;

public sealed class CatalystAndSuppressionRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 13);

    private static EarningsCalendarItem Earnings(string ticker, DateOnly date, string type = EarningsEventType.Earnings)
        => new(ticker, type, date, false, "finnhub");

    private static readonly IReadOnlySet<string> Universe = new HashSet<string>(StringComparer.Ordinal) { "MU", "NVDA", "BRK-B" };

    [Fact]
    public void Catalyst_KeepsUniverseNamesReportingInsideTheWindow_SoonestFirstThenByTicker()
    {
        var items = new[]
        {
            Earnings("NVDA", Today.AddDays(5)),
            Earnings("MU", Today.AddDays(5)),
            Earnings("BRK.B", Today.AddDays(2)),
            Earnings("ZZZZ", Today.AddDays(1)),
        };

        var picked = CatalystScreenRules.Select(items, Universe, Today, 10);

        picked.Should().Equal(
            new CatalystCandidate("BRK-B", Today.AddDays(2)),
            new CatalystCandidate("MU", Today.AddDays(5)),
            new CatalystCandidate("NVDA", Today.AddDays(5)));
    }

    [Fact]
    public void Catalyst_WindowIncludesTodayAndTheLastDay_ButNotTheDayAfterNorYesterday()
    {
        var items = new[]
        {
            Earnings("MU", Today),
            Earnings("NVDA", Today.AddDays(10)),
            Earnings("BRK-B", Today.AddDays(11)),
        };

        CatalystScreenRules.Select(items, Universe, Today, 10).Select(c => c.Ticker).Should().Equal("MU", "NVDA");
        CatalystScreenRules.Select([Earnings("MU", Today.AddDays(-1))], Universe, Today, 10).Should().BeEmpty();
    }

    [Fact]
    public void Catalyst_TakesTheEarliestDateWhenATickerAppearsTwice_AndIgnoresOtherEventTypes()
    {
        var items = new[]
        {
            Earnings("MU", Today.AddDays(8)),
            Earnings("MU", Today.AddDays(3)),
            Earnings("NVDA", Today.AddDays(1), EarningsEventType.ExDividend),
        };

        CatalystScreenRules.Select(items, Universe, Today, 10)
            .Should().Equal(new CatalystCandidate("MU", Today.AddDays(3)));
    }

    private static readonly DateTimeOffset Removed = new(2026, 10, 1, 1, 10, 0, TimeSpan.Zero);

    [Fact]
    public void Suppression_ANameNeverRemoved_IsNeverSuppressed()
        => ScreenSuppression.Suppressed(null, Removed.AddDays(-400), Removed, 30).Should().BeFalse();

    [Fact]
    public void Suppression_EvidenceAlreadyInViewAtTheRemoval_IsSuppressed_EvenAfterTheCooldown()
    {
        ScreenSuppression.Suppressed(Removed, Removed.AddDays(-5), Removed.AddDays(200), 30).Should().BeTrue();
        ScreenSuppression.Suppressed(Removed, Removed, Removed.AddDays(200), 30).Should().BeTrue("evidence at the removal instant is not newer");
    }

    [Fact]
    public void Suppression_NewerEvidenceInsideTheCooldown_IsSuppressed()
        => ScreenSuppression.Suppressed(Removed, Removed.AddDays(2), Removed.AddDays(29), 30).Should().BeTrue();

    [Fact]
    public void Suppression_NewerEvidenceAfterTheCooldown_Stands()
        => ScreenSuppression.Suppressed(Removed, Removed.AddDays(2), Removed.AddDays(30), 30).Should().BeFalse();

    [Fact]
    public void ScreenHit_Reasons_LeadWithTheTag_AndDoNotRepeatIt()
    {
        var hit = new ScreenHit(
            ScreenTags.RelativeStrength, "MU", ["scan: top-decile RS", ScreenTags.RelativeStrength, "SCAN: top-decile rs"], "d", Removed);

        hit.Reasons.Should().Equal(ScreenTags.RelativeStrength, "scan: top-decile RS");
        ScreenTags.All.Should().OnlyContain(t => ScreenTags.IsScreenTag(t));
        ScreenTags.IsScreenTag("scan: top-decile RS").Should().BeFalse();
    }
}
