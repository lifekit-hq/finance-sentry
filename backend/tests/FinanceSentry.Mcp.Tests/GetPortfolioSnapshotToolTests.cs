using System.Reflection;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Mcp.Tools;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class GetPortfolioSnapshotToolTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IBookFiguresService> _book = new();

    private GetPortfolioSnapshotTool CreateSut() =>
        new(_book.Object, new FakeIdentityResolver { ResolvedUserId = UserId });

    private async Task<JsonObject> SnapshotJsonAsync(params BookFigurePosition[] positions)
    {
        var invested = positions.Sum(p => p.UsdValue);
        _book.Setup(b => b.ReadAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BookFigures(0m, 0m, 0m, invested, invested, positions, false, []));

        var snapshot = await CreateSut().ExecuteAsync();
        return JsonSerializer.SerializeToNode(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
    }

    // Binance rows carry a basis; the IBKR equities (Client Portal sync, no fills) do not: coverage 35%.
    private static BookFigurePosition[] PartiallyVerifiedBook() =>
    [
        new("SOL", "crypto", 20m, 4_000m, 5_388m, "binance"),
        new("DRAM", "equity", 10m, null, 6_478m, "ibkr"),
        new("GRAB", "equity", 10m, null, 1_529m, "ibkr"),
        new("CBRS", "equity", 10m, null, 1_943m, "ibkr"),
    ];

    [Fact]
    public async Task PartialBasis_CarriesNoBareAggregateUnrealizedFigure()
    {
        var json = await SnapshotJsonAsync(PartiallyVerifiedBook());

        json["headlines"]!["unrealizedPnl"]!["basisStatus"]!.GetValue<string>().Should().Be("PARTIAL");
        json["totalUnrealizedPnlUsd"].Should().BeNull();
        json["totalUnrealizedPnlPct"].Should().BeNull();
        json["totalCostBasisUsd"].Should().BeNull();
    }

    [Fact]
    public async Task PartialBasis_MovesTheAggregateIntoABlockThatCarriesItsCoverage()
    {
        var json = await SnapshotJsonAsync(PartiallyVerifiedBook());

        var block = json["verifiedUnrealizedPnl"]!.AsObject();
        block["basisStatus"]!.GetValue<string>().Should().Be("PARTIAL");
        block["unrealizedPnlUsd"]!.GetValue<decimal>().Should().Be(1_388m);
        block["unrealizedPnlPct"]!.GetValue<decimal>().Should().Be(34.7m);
        block["costBasisUsd"]!.GetValue<decimal>().Should().Be(4_000m);
        block["measuredValueUsd"]!.GetValue<decimal>().Should().Be(5_388m);
        block["totalValueUsd"]!.GetValue<decimal>().Should().Be(15_338m);
        block["coverage"]!.GetValue<decimal>().Should().Be(0.3513m);
        block["positionsMeasured"]!.GetValue<int>().Should().Be(1);
        block["positionsTotal"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public async Task CompleteBasis_KeepsTheTopLevelAggregate()
    {
        var json = await SnapshotJsonAsync(
            new BookFigurePosition("SOL", "crypto", 20m, 4_000m, 5_000m, "binance"),
            new BookFigurePosition("AAPL", "equity", 10m, 1_000m, 1_500m, "ibkr"));

        json["headlines"]!["unrealizedPnl"]!["basisStatus"]!.GetValue<string>().Should().Be("COMPLETE");
        json["totalUnrealizedPnlUsd"]!.GetValue<decimal>().Should().Be(1_500m);
        json["totalUnrealizedPnlPct"]!.GetValue<decimal>().Should().Be(30m);
        json["totalCostBasisUsd"]!.GetValue<decimal>().Should().Be(5_000m);
    }

    [Fact]
    public async Task NoBasis_CarriesNoAggregateAndNoBlock()
    {
        var json = await SnapshotJsonAsync(new BookFigurePosition("DRAM", "equity", 10m, null, 6_478m, "ibkr"));

        json["totalUnrealizedPnlUsd"].Should().BeNull();
        json["totalUnrealizedPnlPct"].Should().BeNull();
        json["verifiedUnrealizedPnl"].Should().BeNull();
    }

    [Fact]
    public async Task EachPosition_CarriesItsBasisState()
    {
        var json = await SnapshotJsonAsync(
            new BookFigurePosition("SOL", "crypto", 20m, 4_000m, 5_388m, "binance", "Verified"),
            new BookFigurePosition("DRAM", "equity", 10m, null, 6_478m, "ibkr", "Unknown"),
            new BookFigurePosition("GRAB", "equity", 10m, null, 1_529m, "ibkr", "Unverified"));

        var states = json["positions"]!.AsArray().ToDictionary(
            p => p!["symbol"]!.GetValue<string>(), p => p!["basisState"]!.GetValue<string>());
        states.Should().Equal(new Dictionary<string, string>
        {
            ["SOL"] = "Verified",
            ["DRAM"] = "Unknown",
            ["GRAB"] = "Unverified",
        });
    }

    [Fact]
    public void Description_SaysTheAggregateCoversOnlyFillVerifiedPositionsAndNamesCoverage()
    {
        var description = typeof(GetPortfolioSnapshotTool)
            .GetMethod(nameof(GetPortfolioSnapshotTool.ExecuteAsync))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("fill-verified");
        description.Should().Contain("verifiedUnrealizedPnl");
        description.Should().Contain("coverage");
        description.Should().Contain("basisState");
    }
}
