namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.CryptoSync.Domain;
using FinanceSentry.Modules.CryptoSync.Domain.Interfaces;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ── Contract tests: GET /api/v1/wealth/crypto/growth ─────────────────────────
// Same response shape as the brokerage split (BrokerageGrowthShape).

public class CryptoGrowthContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private readonly WealthApiFactory _factory = factory;

    [Fact]
    public async Task GetCryptoGrowth_NoAuth_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/wealth/crypto/growth");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCryptoGrowth_SplitsTheVenuesChange_IntoContributedAndGrowth()
    {
        var client = _factory.CreateAuthenticatedClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-10);
        await SeedAsync(from, today);

        var response = await client.GetAsync("/api/v1/wealth/crypto/growth");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BrokerageGrowthShape>();
        body!.SeriesStart.Should().Be(from.ToString("yyyy-MM-dd"));
        body.StartValue.Should().Be(1000m);
        body.EndValue.Should().Be(1210m);
        body.Contributed.Should().Be(100m);
        body.Growth.Should().Be(110m);
        body.SourcesWithoutFlows.Should().BeEmpty();
        body.Currency.Should().Be("USD");
        body.Headline.Amount.Should().Be(110m);
        body.Headline.Scope.Should().Be("sleeve:crypto");
    }

    private async Task SeedAsync(DateOnly from, DateOnly today)
    {
        using var scope = _factory.Services.CreateScope();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoSyncDbContext>();
        var credential = ExchangeCredential.Create(_factory.TestUserId, CryptoExchangeProvider.RevolutX, [1], [1], [1], [1], [1], [1], 1);
        typeof(ExchangeCredential).GetProperty(nameof(ExchangeCredential.CreatedAt))!
            .SetValue(credential, from.AddDays(-30).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        credential.MarkFlowsWalked(DateTime.UtcNow);
        crypto.ExchangeCredentials.Add(credential);
        var at = from.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        crypto.CryptoFlowEvents.Add(CryptoFlowEvent.Create(_factory.TestUserId, CryptoExchangeProvider.RevolutX,
            new CryptoFlow("receive-1", CryptoFlowTypes.Receive, at, 100m, "USD", true, null, null, null, "revolut", "completed"), at));
        await crypto.SaveChangesAsync();

        var wealth = scope.ServiceProvider.GetRequiredService<WealthDbContext>();
        wealth.NetWorthSnapshots.AddRange(Snapshot(from, 1000m), Snapshot(today, 1210m));
        await wealth.SaveChangesAsync();
    }

    private NetWorthSnapshot Snapshot(DateOnly date, decimal crypto) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _factory.TestUserId,
        SnapshotDate = date,
        CryptoTotal = crypto,
        TotalNetWorth = crypto,
        TakenAt = DateTimeOffset.UtcNow,
    };
}
