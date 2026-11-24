namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ── Contract tests: GET /api/v1/wealth/{brokerage,crypto}/costs ──────────────
// The real host, handlers and readers over the rows already collected; hand-computed expectations in USD.

public class SleeveCostsContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly From = Today.AddDays(-10);

    private readonly WealthApiFactory _factory = factory;

    [Fact]
    public async Task NoAuth_Returns401()
        => (await _factory.CreateClient().GetAsync("/api/v1/wealth/brokerage/costs")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task BrokerageOnly_SumsCommissionFeeFxAndWithholding_AndTheShareOfReturns()
    {
        var user = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(user);
        await SeedBrokerageAsync(user);

        var body = await GetAsync(client, "brokerage");

        body.Commissions.Should().Be(3m);
        body.Fees.Should().Be(11m);
        body.FxConversion.Should().Be(2m);
        body.Withholding.Should().Be(5m);
        body.Total.Should().Be(21m);
        body.ShareOfReturns.Should().Be(0.1603m); // 21 / (110 + 21)
        body.UnvaluedRows.Should().Be(0);
        body.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task CryptoOnly_CountsTheFillFees_AndLeavesAnUnvaluedFeeOutOfTheTotal()
    {
        var user = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(user);
        await SeedCryptoAsync(user);

        var body = await GetAsync(client, "crypto");

        body.Commissions.Should().Be(15m);
        body.Total.Should().Be(15m);
        body.Fees.Should().Be(0m);
        body.UnvaluedRows.Should().Be(1);
        body.ShareOfReturns.Should().Be(0.12m); // 15 / (110 + 15)
    }

    [Fact]
    public async Task BothBooks_EachSleeveReadsOnlyItsOwnRows()
    {
        var user = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(user);
        await SeedBrokerageAsync(user);
        await SeedCryptoAsync(user, snapshots: false);

        var brokerage = await GetAsync(client, "brokerage");
        var crypto = await GetAsync(client, "crypto");

        brokerage.Total.Should().Be(21m);
        crypto.Total.Should().Be(15m);
    }

    [Fact]
    public async Task ABookWithNoCosts_ReturnsZeroTotalAndNoShare()
    {
        var user = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(user);

        foreach (var sleeve in new[] { "brokerage", "crypto" })
        {
            var body = await GetAsync(client, sleeve);
            body.Total.Should().Be(0m);
            body.ShareOfReturns.Should().BeNull();
            body.UnvaluedRows.Should().Be(0);
        }
    }

    [Fact]
    public async Task ACostDatedAfterTheGrowthWindowCloses_IsNotInTheShare()
    {
        var user = Guid.NewGuid();
        var client = _factory.CreateAuthenticatedClient(user);
        // The value series ends yesterday; a 40 fee dated today is in the total but outside the growth window.
        await SeedBrokerageAsync(user, closeYesterday: true, extraFeeToday: 40m);

        var body = await GetAsync(client, "brokerage");

        body.Total.Should().Be(61m);
        body.ShareOfReturns.Should().Be(0.1603m); // 21 / (110 + 21): the 40 today is not in the window
    }

    private static async Task<CostsShape> GetAsync(HttpClient client, string sleeve)
    {
        var response = await client.GetAsync($"/api/v1/wealth/{sleeve}/costs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CostsShape>())!;
    }

    private static DateTime At(DateOnly day) => day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

    private async Task SeedBrokerageAsync(Guid user, bool closeYesterday = false, decimal extraFeeToday = 0m)
    {
        using var scope = _factory.Services.CreateScope();
        var brokerage = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var credential = new InzhurCredential(user);
        typeof(InzhurCredential).GetProperty(nameof(InzhurCredential.CreatedAt))!
            .SetValue(credential, From.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        brokerage.InzhurCredentials.Add(credential);
        BrokerageCashTransaction Cash(string id, string type, decimal amount, DateOnly day)
            => new(user, "inzhur", id, null, null, null, At(day), amount, type, null, null, "USD", null);
        brokerage.BrokerageCashTransactions.AddRange(
            Cash("deposit-1", "Deposit", 100m, From.AddDays(1)),
            Cash("fee-1", "Other Fees", -10m, From.AddDays(2)),
            Cash("wht-1", "Withholding Tax", -5m, From.AddDays(3)),
            Cash("fx-1", "Conversion Surcharge", -2m, From.AddDays(4)));
        if (extraFeeToday > 0m)
            brokerage.BrokerageCashTransactions.Add(Cash("fee-today", "Other Fees", -extraFeeToday, Today));
        brokerage.BrokerageTrades.Add(new BrokerageTrade(user, "ibkr", "trade-1", "t", null, null, "AAPL", "O", null, At(From.AddDays(5)),
            1m, 100m, -100m, null, null, -3m, "USD", -1m, "USD", null, null));
        await brokerage.SaveChangesAsync();

        var close = closeYesterday ? Today.AddDays(-1) : Today;
        var wealth = scope.ServiceProvider.GetRequiredService<WealthDbContext>();
        wealth.NetWorthSnapshots.AddRange(Snapshot(user, From, 1000m, brokerage: true), Snapshot(user, close, 1210m, brokerage: true));
        await wealth.SaveChangesAsync();
    }

    private async Task SeedCryptoAsync(Guid user, bool snapshots = true)
    {
        using var scope = _factory.Services.CreateScope();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoSyncDbContext>();
        var credential = ExchangeCredential.Create(user, CryptoExchangeProvider.RevolutX, [1], [1], [1], [1], [1], [1], 1);
        typeof(ExchangeCredential).GetProperty(nameof(ExchangeCredential.FirstLinkedAt))!
            .SetValue(credential, From.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        credential.MarkFlowsWalked(DateTime.UtcNow);
        crypto.ExchangeCredentials.Add(credential);
        var at = At(From.AddDays(1));
        crypto.CryptoFlowEvents.Add(CryptoFlowEvent.Create(user, CryptoExchangeProvider.RevolutX,
            new CryptoFlow("receive-1", CryptoFlowTypes.Receive, at, 100m, "USD", true, null, null, null, "revolut", "completed"), at));
        CryptoTradeRecord Fill(string id, DateOnly day, decimal? fee, decimal? feeUsd)
            => CryptoTradeRecord.Create(user, "binance",
                new CryptoTrade(id, "BTC", "USDT", 0.1m, 60000m, 6000m, true, At(day), fee, fee is null ? null : "BNB", feeUsd));
        crypto.CryptoTrades.AddRange(
            Fill("f1", From.AddDays(2), 0.001m, 6m),
            Fill("f2", From.AddDays(3), 9m, 9m),
            Fill("f3", From.AddDays(4), 5m, null),
            Fill("f4", From.AddDays(5), null, null));
        await crypto.SaveChangesAsync();

        if (!snapshots)
            return;
        var wealth = scope.ServiceProvider.GetRequiredService<WealthDbContext>();
        wealth.NetWorthSnapshots.AddRange(Snapshot(user, From, 1000m, brokerage: false), Snapshot(user, Today, 1210m, brokerage: false));
        await wealth.SaveChangesAsync();
    }

    private static NetWorthSnapshot Snapshot(Guid user, DateOnly date, decimal value, bool brokerage) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user,
        SnapshotDate = date,
        BrokerageTotal = brokerage ? value : 0m,
        BrokerageInvested = brokerage ? value : 0m,
        CryptoTotal = brokerage ? 0m : value,
        TotalNetWorth = value,
        TakenAt = DateTimeOffset.UtcNow,
    };

    private sealed record CostsShape(
        string AsOf, string YearStart, decimal Commissions, decimal Fees, decimal FxConversion, decimal Withholding,
        decimal Total, decimal? ShareOfReturns, int UnvaluedRows, int ApproximateRows, List<string> FxReasons, string Currency);
}
