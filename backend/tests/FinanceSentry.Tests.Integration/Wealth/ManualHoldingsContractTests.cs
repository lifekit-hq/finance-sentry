namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Xunit;

// ── Contract tests: /api/v1/wealth/manual-holdings ───────────────────────────

public class ManualHoldingsContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private const string Route = "/api/v1/wealth/manual-holdings";

    private readonly WealthApiFactory _factory = factory;

    private static object Steam(decimal amount = 40m, string convertibility = "InEconomy") => new
    {
        label = "Steam wallet",
        amount,
        currency = "usd",
        asOf = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
        convertibility,
    };

    private HttpClient NewPerson() => _factory.CreateAuthenticatedClient(Guid.NewGuid());

    [Fact]
    public async Task NoAuth_Returns401()
        => (await _factory.CreateClient().GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Create_ThenList_ReturnsTheEntryAsAnL3Estimate()
    {
        var client = NewPerson();

        var created = await client.PostAsJsonAsync(Route, Steam());

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<ManualHoldingShape>();
        body!.Label.Should().Be("Steam wallet");
        body.Currency.Should().Be("USD");
        body.Convertibility.Should().Be("InEconomy");
        body.Reliability.Should().Be("L3Estimated");
        created.Headers.Location!.ToString().Should().EndWith($"{Route}/{body.Id}");

        var list = await client.GetFromJsonAsync<List<ManualHoldingShape>>(Route);
        list!.Should().ContainSingle().Which.Id.Should().Be(body.Id);
    }

    [Fact]
    public async Task Update_ChangesTheEntry_AndDelete_RemovesIt()
    {
        var client = NewPerson();
        var id = (await (await client.PostAsJsonAsync(Route, Steam())).Content.ReadFromJsonAsync<ManualHoldingShape>())!.Id;

        var updated = await client.PutAsJsonAsync($"{Route}/{id}", Steam(amount: 55.5m, convertibility: "Locked"));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await updated.Content.ReadFromJsonAsync<ManualHoldingShape>();
        body!.Amount.Should().Be(55.5m);
        body.Convertibility.Should().Be("Locked");

        (await client.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<ManualHoldingShape>>(Route))!.Should().BeEmpty();
        (await client.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a removed entry is gone to the person");
        (await client.PutAsJsonAsync($"{Route}/{id}", Steam(amount: 1m))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnotherPersonsEntry_IsNotListed_AndCannotBeChangedOrRemoved()
    {
        var owner = NewPerson();
        var id = (await (await owner.PostAsJsonAsync(Route, Steam())).Content.ReadFromJsonAsync<ManualHoldingShape>())!.Id;
        var other = NewPerson();

        (await other.GetFromJsonAsync<List<ManualHoldingShape>>(Route))!.Should().BeEmpty();
        var put = await other.PutAsJsonAsync($"{Route}/{id}", Steam(amount: 1m));
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await put.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("MANUAL_HOLDING_NOT_FOUND");
        (await other.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await owner.GetFromJsonAsync<List<ManualHoldingShape>>(Route))!.Single().Amount.Should().Be(40m);
    }

    public static TheoryData<object> InvalidBodies() => new()
    {
        new { label = "", amount = 40m, currency = "USD", asOf = "2026-10-01", convertibility = "InEconomy" },
        new { label = "Steam wallet", amount = 0m, currency = "USD", asOf = "2026-10-01", convertibility = "InEconomy" },
        new { label = "Steam wallet", amount = 40m, currency = "ZZZ", asOf = "2026-10-01", convertibility = "InEconomy" },
        new { label = "Steam wallet", amount = 40m, currency = "USD", asOf = "2099-01-01", convertibility = "InEconomy" },
        new { label = "Steam wallet", amount = 40m, currency = "USD", asOf = "2026-10-01" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_WithAnInvalidBody_Returns400ValidationError(object body)
    {
        var response = await NewPerson().PostAsJsonAsync(Route, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task TheEntry_CountsInTheWealthSummaryTotal_AsAnEstimatedOtherRow()
    {
        var client = NewPerson();
        await client.PostAsJsonAsync(Route, Steam());
        _factory.BankAccountRepoMock
            .Setup(r => r.GetByUserIdUnscopedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var summary = await client.GetFromJsonAsync<WealthSummaryShape>("/api/v1/wealth/summary");

        summary!.TotalNetWorth.Should().Be(40m);
        summary.Categories.Should().ContainSingle().Which.Category.Should().Be("other");
    }
}

public record ManualHoldingShape(
    Guid Id, string Label, decimal Amount, string Currency, DateOnly AsOf, string Convertibility, string Reliability);
