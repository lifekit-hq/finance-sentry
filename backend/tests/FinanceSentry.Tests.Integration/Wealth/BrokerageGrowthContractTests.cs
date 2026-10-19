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

// ── Contract tests: GET /api/v1/wealth/brokerage/growth ──────────────────────

public class BrokerageGrowthContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly WealthApiFactory _factory = factory;

    [Fact]
    public async Task GetBrokerageGrowth_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/wealth/brokerage/growth");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBrokerageGrowth_SplitsTheChange_IntoContributedAndGrowth()
    {
        var client = _factory.CreateAuthenticatedClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-10);
        await SeedAsync(from, today);

        var response = await client.GetAsync("/api/v1/wealth/brokerage/growth");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrokerageGrowthShape>();
        body!.SeriesStart.Should().Be(from.ToString("yyyy-MM-dd"));
        body.From.Should().Be(from.ToString("yyyy-MM-dd"));
        body.StartValue.Should().Be(1000m);
        body.EndValue.Should().Be(1210m);
        body.Contributed.Should().Be(100m);
        body.NewlyLinked.Should().Be(0m);
        body.Growth.Should().Be(110m);
        body.MoneyWeightedReturn.Should().Be(0.1m);
        body.TimeWeightedReturn.Should().Be(0.1m);
        body.SourcesWithoutFlows.Should().BeEmpty();
        body.Currency.Should().Be("USD");
        body.Headline.Amount.Should().Be(110m);
        body.Headline.Method.Should().Be("MODIFIED_DIETZ");
        body.Headline.Scope.Should().Be("sleeve:brokerage");
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
        await brokerage.SaveChangesAsync();

        var wealth = scope.ServiceProvider.GetRequiredService<WealthDbContext>();
        wealth.NetWorthSnapshots.AddRange(Snapshot(from, 1000m), Snapshot(today, 1210m));
        await wealth.SaveChangesAsync();
    }

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

public record BrokerageGrowthHeadlineShape(decimal? Amount, decimal? Percent, string Method, string Scope, string Quality);
public record BrokerageGrowthShape(
    string? SeriesStart, string? From, string? To, decimal? StartValue, decimal? EndValue, decimal? Contributed,
    decimal? NewlyLinked, decimal? Growth, decimal? MoneyWeightedReturn, List<string> SourcesWithoutFlows,
    BrokerageGrowthHeadlineShape Headline, string Currency, decimal? TimeWeightedReturn);
