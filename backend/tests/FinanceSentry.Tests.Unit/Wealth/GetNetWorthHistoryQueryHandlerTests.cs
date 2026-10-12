namespace FinanceSentry.Tests.Unit.Wealth;

using System.Text.Json;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class GetNetWorthHistoryQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ExposesTheSplitWhenPresent_AndNullsWhereTheDayHasNone()
    {
        var withSplit = new NetWorthSnapshot
        {
            UserId = UserId,
            SnapshotDate = new DateOnly(2026, 10, 2),
            BankingTotal = 1_000m,
            BrokerageTotal = 500m,
            CryptoTotal = 250m,
            TotalNetWorth = 1_750m,
            CashTotal = 1_150m,
            BrokerageInvested = 400m,
            CryptoInvested = 200m,
        };
        var withoutSplit = new NetWorthSnapshot
        {
            UserId = UserId,
            SnapshotDate = new DateOnly(2026, 5, 1),
            BankingTotal = 900m,
            TotalNetWorth = 900m,
        };
        var repo = new Mock<INetWorthSnapshotRepository>();
        repo.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([withoutSplit, withSplit]);

        var response = await new GetNetWorthHistoryQueryHandler(repo.Object)
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.Snapshots[0].CashTotal.Should().BeNull("a day before the split existed has no split, not zero");
        response.Snapshots[0].BrokerageInvested.Should().BeNull();
        response.Snapshots[0].CryptoInvested.Should().BeNull();
        response.Snapshots[1].CashTotal.Should().Be(1_150m);
        response.Snapshots[1].BrokerageInvested.Should().Be(400m);
        response.Snapshots[1].CryptoInvested.Should().Be(200m);
        response.Snapshots[1].TotalNetWorth.Should().Be(1_750m);
    }

    [Fact]
    public void ResponseJson_KeepsEveryExistingFieldAndAddsTheThreeNullableOnes()
    {
        var dto = new NetWorthSnapshotDto(new DateOnly(2026, 5, 1), 900m, 0m, 0m, 900m, "USD", null, false);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().Contain([
            "snapshotDate", "bankingTotal", "brokerageTotal", "cryptoTotal", "totalNetWorth",
            "currency", "staleSleeves", "isApproximate",
            "cashTotal", "brokerageInvested", "cryptoInvested"]);
        json.RootElement.GetProperty("cashTotal").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
