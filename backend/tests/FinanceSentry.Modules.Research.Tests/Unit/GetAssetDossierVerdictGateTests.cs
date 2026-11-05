namespace FinanceSentry.Modules.Research.Tests.Unit;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Domain.Recommendations;
using FinanceSentry.Modules.Research.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// The dossier's verdict sections (the scorecard's BUY/HOLD/AVOID word and Ledger's pending proposal) belong to the
/// owner (ai.use): a query that does not include them returns both null, never reads them, and leaves every
/// other section as it was.
/// </summary>
public class GetAssetDossierVerdictGateTests
{
    private const string Symbol = "ABCD";
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task IncludeVerdicts_ReturnsTheScorecardAndThePendingRecommendation()
    {
        var recommendations = PendingRecommendationFor(Symbol);

        var dossier = await HandleAsync(new GetAssetDossierQuery(User, Symbol, IncludeVerdicts: true), recommendations);

        dossier.Scorecard.Should().NotBeNull();
        dossier.Scorecard!.Verdict.Reasons.Should().NotBeEmpty("the reasons ride the verdict to the owner");
        dossier.PendingRecommendation.Should().NotBeNull();
        dossier.PendingRecommendation!.Ticker.Should().Be(Symbol);
    }

    [Fact]
    public async Task WithoutVerdicts_ReturnsNeitherAndNeverReadsThePendingRecommendation()
    {
        var recommendations = PendingRecommendationFor(Symbol);

        var dossier = await HandleAsync(new GetAssetDossierQuery(User, Symbol, IncludeVerdicts: false), recommendations);

        dossier.Scorecard.Should().BeNull("the reasons and the turns-BUY line live inside the scorecard's verdict, so they go with it");
        dossier.PendingRecommendation.Should().BeNull();
        dossier.Symbol.Should().Be(Symbol);
        recommendations.Verify(
            r => r.FindPendingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ByDefault_TheQueryIncludesVerdicts_SoTheAiGatedLedgerReadKeepsItsInputs()
        => new GetAssetDossierQuery(User, Symbol).IncludeVerdicts.Should().BeTrue();

    private static Mock<IRecommendationRepository> PendingRecommendationFor(string ticker)
    {
        var recommendations = new Mock<IRecommendationRepository>();
        recommendations
            .Setup(r => r.FindPendingAsync(User, ticker, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Recommendation
            {
                UserId = User,
                Ticker = ticker,
                Action = RecommendationAction.Add,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(3),
            });
        return recommendations;
    }

    private static Task<AssetDossierResult> HandleAsync(
        GetAssetDossierQuery query, Mock<IRecommendationRepository> recommendations)
    {
        var fundamentals = new Mock<IFundamentalsService>();
        fundamentals
            .Setup(f => f.GetFundamentalsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundamentalsResult(Symbol, [], new FundamentalsCoverage(FundamentalsCoverageStatus.NoSourceAvailable, null, null, [])));

        var handler = new GetAssetDossierQueryHandler(
            Mock.Of<IBookFiguresService>(),
            Mock.Of<IHoldingTaxLotsReader>(),
            Mock.Of<IAssetSignalReader>(),
            Mock.Of<IQueryHandler<GetThesesQuery, IReadOnlyList<ThesisDto>>>(),
            Mock.Of<IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>>(),
            Mock.Of<IQueryHandler<GetAnalystActionsQuery, AnalystActionsResult>>(),
            Mock.Of<IQueryHandler<GetNewsForTickerQuery, IReadOnlyList<NewsArticleDto>>>(),
            Mock.Of<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>(),
            Mock.Of<IPositionCapSource>(),
            Mock.Of<IPriceHistoryService>(),
            fundamentals.Object,
            Mock.Of<IAssetPriceTargetRepository>(),
            recommendations.Object,
            new PositionReturnsReader(Mock.Of<IPositionLedgerSource>(), Mock.Of<IHistoricalExchangeRateService>()),
            new DisposalTaxContextReader(Mock.Of<IInstrumentTaxRegimeSource>(), Mock.Of<IHistoricalExchangeRateService>()),
            new ScorecardReader(
                Mock.Of<IMarketStructureReader>(),
                fundamentals.Object,
                Mock.Of<IMarketRegimeSource>(),
                Options.Create(new OpportunityOptions()),
                Options.Create(new VerdictOptions())),
            NullLogger<GetAssetDossierQueryHandler>.Instance);

        return handler.Handle(query, CancellationToken.None);
    }
}
