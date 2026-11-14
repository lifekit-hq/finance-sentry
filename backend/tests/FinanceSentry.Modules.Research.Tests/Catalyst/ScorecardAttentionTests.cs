namespace FinanceSentry.Modules.Research.Tests.Catalyst;

using FinanceSentry.Modules.Research.Application.Commands;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Catalyst;
using FinanceSentry.Modules.Research.Domain.Catalyst;
using FinanceSentry.Modules.Research.Tests.Opportunity;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>The attention measure is exposed to the scorecard as a fact (no score input) and never blocks a score.</summary>
public sealed class ScorecardAttentionTests
{
    private static async Task<ScoreCandidateResult> ScoreAsync(IThemeAttentionReader? reader)
    {
        var handler = new ScoreCandidateCommandHandler(
            new FakeCandidateRepository(), new FakeCandidateScoreRepository(), new FakeMarketStructureReader(), new FakeFundamentalsService(),
            new FakeIpsRepository(), new FakePositionCapSource(), new FakeBrokerageHoldingsReader([]), new FakeReturnCorrelationReader(),
            new RecordingRadarSignalWriter(), new FakeMarketRegimeSource(), new RecordingThesisEventRecorder(),
            new FakeOpportunityAlertGenerator(), Microsoft.Extensions.Options.Options.Create(new OpportunityOptions()), reader);

        return await handler.Handle(new ScoreCandidateCommand(Guid.NewGuid(), "MU"), CancellationToken.None);
    }

    [Fact]
    public async Task Scorecard_CarriesTheStoredReadingsOfTheTickersThemes_AsEvidence()
    {
        var reading = new ThemeAttentionReading(
            "theme:memory", "HBM memory", new DateOnly(2026, 11, 15), 30, 10m, 3m, 6, 7, CatalystHarness.Now);
        var reader = new Mock<IThemeAttentionReader>();
        reader.Setup(r => r.GetForTickerAsync("MU", It.IsAny<CancellationToken>())).ReturnsAsync(new AttentionFact([reading], null));

        var result = await ScoreAsync(reader.Object);

        var fact = result.Scorecard.Evidence.Attention!;
        fact.Readings.Should().ContainSingle().Which.Should().Be(reading);
        fact.Note.Should().BeNull();
    }

    [Fact]
    public async Task AFailingAttentionStore_DoesNotStopTheScore_AndSaysSoInTheFact()
    {
        var reader = new Mock<IThemeAttentionReader>();
        reader.Setup(r => r.GetForTickerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await ScoreAsync(reader.Object);

        result.Scorecard.Evidence.Attention!.Readings.Should().BeEmpty();
        result.Scorecard.Evidence.Attention.Note.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task WithNoAttentionStoreWired_TheFactIsAbsent()
    {
        var result = await ScoreAsync(null);

        result.Scorecard.Evidence.Attention.Should().BeNull();
    }
}
