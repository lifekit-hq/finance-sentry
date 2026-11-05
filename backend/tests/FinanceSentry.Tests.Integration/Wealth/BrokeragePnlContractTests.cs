namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ── Contract tests: GET /api/v1/wealth/brokerage/pnl (issue 982) ─────────────

public class BrokeragePnlContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly WealthApiFactory _factory = factory;

    [Fact]
    public async Task GetBrokeragePnl_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/wealth/brokerage/pnl");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBrokeragePnl_PutsRealizedAndUnrealized_OverTheNetDeposited()
    {
        var client = _factory.CreateAuthenticatedClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-10);
        await SeedAsync(from, today);

        var response = await client.GetAsync("/api/v1/wealth/brokerage/pnl");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrokeragePnlShape>();
        body!.From.Should().Be(from.ToString("yyyy-MM-dd"));
        body.NetDepositedUsd.Should().Be(1100m); // opening 1000 + deposit 100
        body.SourcesWithoutFlows.Should().BeEmpty();
        body.Currency.Should().Be("USD");

        body.Realized.Status.Should().Be("COMPLETE");
        body.Realized.Usd.Should().Be(200m); // sold 4 of 14 at 150 over an average cost of 100
        body.Realized.PercentOfNetDeposited.Should().Be(18.18m);

        body.Unrealized.Status.Should().Be("COMPLETE");
        body.Unrealized.Usd.Should().Be(200m); // 10 held, worth 1200 against a verified cost of 1000
        body.Unrealized.PercentOfNetDeposited.Should().Be(18.18m);
        body.Unrealized.Measured.Should().Be(1);
        body.Unrealized.Total.Should().Be(1);
    }

    private async Task SeedAsync(DateOnly from, DateOnly today)
    {
        using var scope = _factory.Services.CreateScope();
        var brokerage = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var credential = new InzhurCredential(_factory.TestUserId);
        typeof(InzhurCredential).GetProperty(nameof(InzhurCredential.CreatedAt))!
            .SetValue(credential, from.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        brokerage.InzhurCredentials.Add(credential);
        brokerage.BrokerageCashTransactions.Add(new BrokerageCashTransaction(
            _factory.TestUserId, "inzhur", "deposit-1", null, null, null,
            from.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), 100m, "Deposit", null, null, "USD", null));
        brokerage.BrokerageHoldings.Add(new BrokerageHolding(_factory.TestUserId, "AAA", "STK", 10m, 1200m, "ibkr", 100m));
        brokerage.BrokerageTrades.AddRange(
            Fill("AAA", 14m, 100m, from.AddDays(-20)),
            Fill("AAA", -4m, 150m, from.AddDays(5)));
        await brokerage.SaveChangesAsync();

        var wealth = scope.ServiceProvider.GetRequiredService<WealthDbContext>();
        wealth.NetWorthSnapshots.AddRange(Snapshot(from, 1000m), Snapshot(today, 1210m));
        await wealth.SaveChangesAsync();
    }

    private BrokerageTrade Fill(string symbol, decimal quantity, decimal price, DateOnly day) => new(
        _factory.TestUserId, "ibkr", $"{symbol}-{day:yyyyMMdd}-{quantity}", $"{symbol}-{day:yyyyMMdd}", null, null, symbol,
        quantity > 0 ? "O" : "C", null, day.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc), quantity, price,
        -(quantity * price), null, null, null, "USD", null, "USD", null);

    private NetWorthSnapshot Snapshot(DateOnly date, decimal brokerage) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _factory.TestUserId,
        SnapshotDate = date,
        BrokerageTotal = brokerage,
        BrokerageInvested = brokerage,
        TotalNetWorth = brokerage,
        TakenAt = DateTimeOffset.UtcNow,
    };
}

public record BrokeragePnlFigureShape(
    decimal? Usd, decimal? PercentOfNetDeposited, string Status, int Measured, int Total, decimal? Coverage,
    List<string> UnavailableSymbols);

public record BrokeragePnlShape(
    string? From, string? To, decimal? NetDepositedUsd, List<string> SourcesWithoutFlows,
    BrokeragePnlFigureShape Realized, BrokeragePnlFigureShape Unrealized, string Currency);
