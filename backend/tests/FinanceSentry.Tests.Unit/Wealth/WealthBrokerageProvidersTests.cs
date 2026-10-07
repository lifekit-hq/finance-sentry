namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Application.Services;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>Brokerage holdings from more than one broker: one institution each, judged on its own cadence.</summary>
public class WealthBrokerageProvidersTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static BrokerageHoldingSummary Holding(string provider, string symbol, decimal usd, TimeSpan age)
        => new(symbol, provider == "inzhur" ? "REIT" : "STK", 1m, usd, DateTime.UtcNow - age, provider);

    private static WealthAggregationService Service(
        IReadOnlyList<BrokerageHoldingSummary> holdings, IReadOnlyDictionary<string, string>? connectionStatuses = null)
    {
        var accounts = new Mock<IBankingAccountsReader>();
        accounts.Setup(r => r.GetAccountSummariesAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var brokerage = new Mock<IBrokerageHoldingsReader>();
        brokerage.Setup(r => r.GetHoldingsAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(holdings);
        var connections = new Mock<IBrokerageConnectionStatusReader>();
        connections.Setup(r => r.GetStatusesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionStatuses ?? new Dictionary<string, string>());

        return new WealthAggregationService(
            accounts.Object, Mock.Of<IBankingTransactionReader>(), null, brokerage.Object, connections.Object);
    }

    [Fact]
    public async Task Each_broker_is_its_own_institution_and_both_count_toward_the_total()
    {
        var result = await Service(
        [
            Holding("ibkr", "AAPL", 1_000m, TimeSpan.FromMinutes(5)),
            Holding("inzhur", "Fund A", 250m, TimeSpan.FromHours(20)),
        ]).GetWealthSummaryAsync(UserId, null, null);

        var brokerage = result.Categories.Single(c => c.Category == "brokerage");
        brokerage.TotalInBaseCurrency.Should().Be(1_250m);
        brokerage.Institutions.Select(i => (i.Provider, i.Name, i.SyncStatus)).Should().BeEquivalentTo(
            [("ibkr", "Interactive Brokers", "synced"), ("inzhur", "Inzhur", "synced")],
            "a once-a-day read twenty hours old is current");
        brokerage.Institutions.Single(i => i.Provider == "inzhur").Accounts.Should().ContainSingle(a => a.Currency == "Fund A" && a.BankName == "Inzhur");
    }

    [Fact]
    public async Task Inzhur_holdings_older_than_a_day_and_a_half_are_stale()
    {
        var result = await Service([Holding("inzhur", "Fund A", 250m, TimeSpan.FromHours(40))]).GetWealthSummaryAsync(UserId, null, null);

        result.Categories.Single().Institutions.Single().SyncStatus.Should().Be("stale");
    }

    [Fact]
    public async Task A_lapsed_Inzhur_session_shows_reconnect_needed_while_its_holdings_still_count()
    {
        var result = await Service(
            [Holding("inzhur", "Fund A", 250m, TimeSpan.FromHours(1))],
            new Dictionary<string, string> { ["inzhur"] = "reauth_required" }).GetWealthSummaryAsync(UserId, null, null);

        var institution = result.Categories.Single().Institutions.Single();
        institution.SyncStatus.Should().Be("reauth_required");
        institution.TotalInBaseCurrency.Should().Be(250m);
    }

    [Fact]
    public async Task Provider_filter_keeps_only_that_brokers_holdings()
    {
        var result = await Service(
        [
            Holding("ibkr", "AAPL", 1_000m, TimeSpan.Zero),
            Holding("inzhur", "Fund A", 250m, TimeSpan.Zero),
        ]).GetWealthSummaryAsync(UserId, null, "inzhur");

        result.Categories.Single().Institutions.Single().Provider.Should().Be("inzhur");
    }
}
