namespace FinanceSentry.Tests.Integration.Research;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Modules.Research.Application.Queries;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Ports;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

/// <summary>
/// The dossier handler reads its sections through handlers that share the request's one scoped
/// <see cref="ResearchDbContext"/>. A DbContext allows a single operation at a time, so the sections must be read
/// one after another: run in parallel, the second operation throws, the handler's per-section guard turns that
/// into a missing section, and the page silently loses Analysts, News or the thesis. The in-memory provider
/// completes every query synchronously and never overlaps two, so this needs real Postgres.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AssetDossierSharedContextTests : IAsyncLifetime
{
    private const string Symbol = "MU";
    private const int Loads = 5;

    private readonly Guid _user = Guid.NewGuid();
    private TestDatabase? _database;

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres16.CreateDatabaseAsync();

        await using var setup = CreateContext();
        await setup.Database.EnsureCreatedAsync();

        setup.Theses.Add(new InvestmentThesis { UserId = _user, Ticker = Symbol, ThesisText = "MU compounds." });
        setup.AnalystActions.Add(new AnalystAction
        {
            Ticker = Symbol,
            Firm = "Firm",
            ActionType = AnalystActionType.Upgrade,
            ActionDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Source = "test",
        });
        setup.News.Add(new NewsArticle
        {
            Source = "test",
            Title = "MU news",
            Url = "https://example.test/mu",
            Tickers = [Symbol],
            PublishedAt = DateTimeOffset.UtcNow,
            ContentHash = Guid.NewGuid().ToString("N"),
        });
        setup.AssetPriceTargets.Add(new AssetPriceTarget { UserId = _user, Symbol = Symbol, TargetPrice = 150m });
        await setup.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    private ResearchDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>().UseNpgsql(_database!.ConnectionString).Options,
            new FixedCurrentUser(_user));

    [DockerRequiredFact]
    public async Task Handle_SectionsSharingOneContext_AllPresentInEveryLoad()
    {
        await using var db = CreateContext();
        var handler = new GetAssetDossierQueryHandler(
            Mock.Of<IBookFiguresService>(),
            Mock.Of<IHoldingTaxLotsReader>(),
            Mock.Of<IAssetSignalReader>(),
            new GetThesesQueryHandler(new ThesisRepository(db)),
            Mock.Of<IQueryHandler<GetValuationSnapshotQuery, ValuationSnapshotResult>>(),
            new GetAnalystActionsQueryHandler(
                new AnalystActionRepository(db),
                new AnalystUniverseRepository(db),
                new RecommendationTrendRepository(db)),
            new GetNewsForTickerQueryHandler(new NewsRepository(db)),
            Mock.Of<IQueryHandler<GetEarningsCalendarQuery, IReadOnlyList<EarningsEventDto>>>(),
            Mock.Of<IPositionCapSource>(),
            Mock.Of<IPriceHistoryService>(),
            Mock.Of<IFundamentalsService>(),
            new AssetPriceTargetRepository(db),
            new PositionReturnsReader(Mock.Of<IPositionLedgerSource>(), Mock.Of<IHistoricalExchangeRateService>()));

        for (var i = 0; i < Loads; i++)
        {
            var dossier = await handler.Handle(new GetAssetDossierQuery(_user, Symbol), CancellationToken.None);

            dossier.Thesis.Should().NotBeNull($"load {i}");
            dossier.Analysts.Should().NotBeNull($"load {i}");
            dossier.Analysts!.RecentActions.Should().HaveCount(1, $"load {i}");
            dossier.RecentNews.Should().HaveCount(1, $"load {i}");
            dossier.UserTarget!.TargetPrice.Should().Be(150m, $"load {i}");
        }
    }
}
