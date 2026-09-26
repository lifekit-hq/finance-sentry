using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinanceSentry.Tests.Integration.BrokerageSync;

// ── Contract tests: instrument master (fs-435 S4) ────────────────────────────
//
// GET /api/v1/brokerage/instruments lists the caller's instruments with their
// (possibly null) classification. PUT .../{id}/classification is the only
// human-set path, and only ever touches the caller's own instruments.

public class BrokerageControllerInstrumentsContractTests(BrokerageApiFactory factory)
    : IClassFixture<BrokerageApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();
    private readonly BrokerageApiFactory _factory = factory;

    private BrokerageInstrument SeedInstrument(Guid userId, long conid = 265598, string symbol = "AAPL")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var instrument = new BrokerageInstrument(userId, "ibkr", conid, symbol, "STK");
        db.BrokerageInstruments.Add(instrument);
        db.SaveChanges();
        return instrument;
    }

    [Fact]
    public async Task GetInstruments_NoAuth_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/v1/brokerage/instruments");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInstruments_ListsCallersInstruments_WithNullClassification()
    {
        var instrument = SeedInstrument(_factory.TestUserId, conid: 300777, symbol: "GOOG");

        var response = await _client.GetAsync("/api/v1/brokerage/instruments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<InstrumentsResponseShape>();
        body.Should().NotBeNull();
        var item = body!.Items.Should().ContainSingle(i => i.Id == instrument.Id).Which;
        item.Conid.Should().Be(300777);
        item.Symbol.Should().Be("GOOG");
        item.Classification.Should().BeNull();
    }

    [Fact]
    public async Task SetClassification_NoAuth_Returns401()
    {
        var instrument = SeedInstrument(_factory.TestUserId);
        var anonClient = _factory.CreateClient();

        var response = await anonClient.PutAsJsonAsync(
            $"/api/v1/brokerage/instruments/{instrument.Id}/classification",
            new { Classification = "OrdinaryShare" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SetClassification_OwnInstrument_Returns204_AndPersists()
    {
        var instrument = SeedInstrument(_factory.TestUserId);

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/brokerage/instruments/{instrument.Id}/classification",
            new { Classification = "OffshoreFund" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResponse = await _client.GetAsync("/api/v1/brokerage/instruments");
        var body = await getResponse.Content.ReadFromJsonAsync<InstrumentsResponseShape>();
        body!.Items.Single(i => i.Id == instrument.Id).Classification.Should().Be("OffshoreFund");
    }

    [Fact]
    public async Task SetClassification_AnotherUsersInstrument_Returns404_AndDoesNotMutateIt()
    {
        var otherUserId = Guid.NewGuid();
        var instrument = SeedInstrument(otherUserId, conid: 111222, symbol: "MSFT");

        var response = await _client.PutAsJsonAsync(
            $"/api/v1/brokerage/instruments/{instrument.Id}/classification",
            new { Classification = "OrdinaryShare" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BrokerageSyncDbContext>();
        var reloaded = db.BrokerageInstruments.Single(i => i.Id == instrument.Id);
        reloaded.Classification.Should().BeNull();
    }
}

// ── Response shapes ───────────────────────────────────────────────────────────

public record InstrumentItemShape(
    Guid Id, string Provider, long Conid, string? Isin, string Symbol, string InstrumentType, string? Classification);

public record InstrumentsResponseShape(List<InstrumentItemShape> Items);
