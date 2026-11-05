namespace FinanceSentry.Modules.PortfolioConstruction.Tests;

using FinanceSentry.Integration;
using FinanceSentry.Modules.Research.Domain.Ports;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public sealed class ResearchUniverseScorecardAdapterTests
{
    private readonly Mock<IScorecardSnapshotReader> _snapshots = new();

    private ResearchUniverseScorecardAdapter Adapter() =>
        new(_snapshots.Object, NullLogger<ResearchUniverseScorecardAdapter>.Instance);

    [Fact]
    public async Task MapsTheSnapshot_WithRawBesideAdjusted_AndTheFiredRule()
    {
        _snapshots.Setup(s => s.GetAsync("AAA", It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot("AAA"));

        var cards = await Adapter().GetAsync(["AAA"], CancellationToken.None);

        var card = cards.Single();
        card.Scored.Should().BeTrue();
        card.StructureRaw.Should().Be(64);
        card.StructureAdjusted.Should().Be(68);
        card.StructureBasis.Should().Be("adjusted");
        card.Fundamentals.Should().Be(72);
        card.Crowding.Should().Be("Normal");
        card.ValuationPremiumPct.Should().Be(16.7m);
        card.VerdictWord.Should().Be("Buy");
        card.FiredRuleId.Should().Be("buy");
        card.FiredRuleDescription.Should().Be("Structure and fundamentals clear the BUY bars.");
    }

    [Fact]
    public async Task AFailingSymbol_BecomesAFailedRow_AndDoesNotBlankTheRest()
    {
        _snapshots.Setup(s => s.GetAsync("BAD", It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("provider down"));
        _snapshots.Setup(s => s.GetAsync("GOOD", It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot("GOOD"));

        var cards = await Adapter().GetAsync(["BAD", "GOOD"], CancellationToken.None);

        cards.Select(c => c.Symbol).Should().Equal("BAD", "GOOD");
        cards[0].Scored.Should().BeFalse();
        cards[0].FailureReason.Should().Be(ResearchUniverseScorecardAdapter.UnreadableReason);
        cards[1].Scored.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        _snapshots.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        var act = () => Adapter().GetAsync(["AAA"], CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ScorecardSnapshot Snapshot(string symbol) => new(
        symbol, Cards.ScoredAt, 64, 68, "adjusted", [], 72, [], "Normal", 0.04m, false, 28m, 24m, 16.7m, 9.1m, null, false,
        "Buy", 1, "buy", "Structure and fundamentals clear the BUY bars.");
}
