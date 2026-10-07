using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Tests.Integration.BrokerageSync;

public class BrokerageControllerHoldingsContractTests : IClassFixture<BrokerageApiFactory>
{
    private readonly HttpClient _client;
    private readonly BrokerageApiFactory _factory;

    public BrokerageControllerHoldingsContractTests(BrokerageApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task GetHoldings_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/brokerage/holdings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetHoldings_NoAccount_Returns200WithEmptyPositions()
    {
        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var response = await _client.GetAsync("/api/v1/brokerage/holdings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrokerageHoldingsResponseShape>();
        body.Should().NotBeNull();
        body!.Positions.Should().BeEmpty();
        body.TotalUsdValue.Should().Be(0m);
    }

    [Fact]
    public async Task GetHoldings_WithHoldings_Returns200WithShape()
    {
        var holding = new BrokerageHolding(_factory.TestUserId, "AAPL", "STK", 10m, 1500m, "ibkr");

        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerageHolding> { holding });

        var response = await _client.GetAsync("/api/v1/brokerage/holdings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrokerageHoldingsResponseShape>();
        body.Should().NotBeNull();
        body!.Provider.Should().Be("ibkr");
        body.Positions.Should().HaveCount(1);
        body.Positions[0].Symbol.Should().Be("AAPL");
        body.Positions[0].InstrumentType.Should().Be("STK");
        body.Positions[0].Quantity.Should().Be(10m);
        body.Positions[0].UsdValue.Should().Be(1500m);
        body.TotalUsdValue.Should().Be(1500m);
        body.SyncedAt.Should().NotBeNull();
        body.SyncedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task GetHoldings_TagsEachPositionWithItsAssetClass()
    {
        var holdings = new List<BrokerageHolding>
        {
            new(_factory.TestUserId, "AAPL", "STK", 10m, 1500m, "ibkr"),
            new(_factory.TestUserId, "USD Cash", "CASH", 100m, 100m, "ibkr"),
            new(_factory.TestUserId, "UAH Cash", "CASH", 4000m, 100m, "inzhur"),
            new(_factory.TestUserId, "WAR", "BOND", 1m, 50m, "inzhur"),
            new(_factory.TestUserId, "FUND", "REIT", 1m, 50m, "inzhur"),
        };

        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(holdings);

        var response = await _client.GetAsync("/api/v1/brokerage/holdings");

        var body = await response.Content.ReadFromJsonAsync<BrokerageHoldingsResponseShape>();
        body!.Positions.ToDictionary(p => p.Symbol, p => p.AssetClass).Should().BeEquivalentTo(
            new Dictionary<string, string>
            {
                ["AAPL"] = "Equities",
                ["USD Cash"] = "Cash",
                ["UAH Cash"] = "Cash",
                ["WAR"] = "Bonds",
                ["FUND"] = "RealEstate",
            });
    }

    [Fact]
    public async Task GetHoldings_StaleData_ReturnsIsStaleTrue()
    {
        var holding = new BrokerageHolding(_factory.TestUserId, "AAPL", "STK", 10m, 1500m, "ibkr");
        typeof(BrokerageHolding)
            .GetField("<SyncedAt>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(holding, DateTime.UtcNow.AddHours(-2));

        _factory.HoldingRepoMock
            .Setup(r => r.GetByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BrokerageHolding> { holding });

        var response = await _client.GetAsync("/api/v1/brokerage/holdings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrokerageHoldingsResponseShape>();
        body!.IsStale.Should().BeTrue();
    }
}
