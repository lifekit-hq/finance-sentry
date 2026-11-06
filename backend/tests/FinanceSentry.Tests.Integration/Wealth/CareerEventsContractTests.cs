namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

// ── Contract tests: /api/v1/wealth/career-events ─────────────────────────────

public class CareerEventsContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private const string Route = "/api/v1/wealth/career-events";

    private readonly WealthApiFactory _factory = factory;

    private static string DaysAhead(int days) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(days).ToString("yyyy-MM-dd");

    private static object Promotion(decimal amount = 500m, string kind = "IncomeStep", string? endDate = null, string? note = "after review") => new
    {
        kind,
        date = DaysAhead(120),
        endDate,
        label = "Promotion",
        amount,
        currency = "eur",
        note,
    };

    private HttpClient NewPerson() => _factory.CreateAuthenticatedClient(Guid.NewGuid());

    [Fact]
    public async Task NoAuth_Returns401()
        => (await _factory.CreateClient().GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Create_ThenList_ReturnsTheEventAsCountedWhileItIsAhead()
    {
        var client = NewPerson();

        var created = await client.PostAsJsonAsync(Route, Promotion());

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<CareerEventShape>();
        body!.Label.Should().Be("Promotion");
        body.Currency.Should().Be("EUR");
        body.Kind.Should().Be("IncomeStep");
        body.Amount.Should().Be(500m);
        body.Note.Should().Be("after review");
        body.Counted.Should().BeTrue();
        body.Retired.Should().BeFalse();
        created.Headers.Location!.ToString().Should().EndWith($"{Route}/{body.Id}");

        (await client.GetFromJsonAsync<List<CareerEventShape>>(Route))!.Should().ContainSingle().Which.Id.Should().Be(body.Id);
    }

    [Fact]
    public async Task AnIncomeStepMayBeNegative_AndALeaveCarriesItsEndDate()
    {
        var client = NewPerson();

        var cut = await client.PostAsJsonAsync(Route, Promotion(amount: -400m));
        var leave = await client.PostAsJsonAsync(Route, Promotion(amount: 1_200m, kind: "Leave", endDate: DaysAhead(200)));

        cut.StatusCode.Should().Be(HttpStatusCode.Created);
        (await cut.Content.ReadFromJsonAsync<CareerEventShape>())!.Amount.Should().Be(-400m);
        leave.StatusCode.Should().Be(HttpStatusCode.Created);
        (await leave.Content.ReadFromJsonAsync<CareerEventShape>())!.EndDate.Should().Be(DaysAhead(200));
    }

    [Fact]
    public async Task Update_ChangesTheEvent_Retire_TakesItOutOfTheScenario_AndDelete_RemovesIt()
    {
        var client = NewPerson();
        var id = (await (await client.PostAsJsonAsync(Route, Promotion())).Content.ReadFromJsonAsync<CareerEventShape>())!.Id;

        var updated = await client.PutAsJsonAsync($"{Route}/{id}", Promotion(amount: 650m));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<CareerEventShape>())!.Amount.Should().Be(650m);

        var retired = await client.PostAsync($"{Route}/{id}/retire", null);
        retired.StatusCode.Should().Be(HttpStatusCode.OK);
        var ended = await retired.Content.ReadFromJsonAsync<CareerEventShape>();
        ended!.Retired.Should().BeTrue();
        ended.Counted.Should().BeFalse("a retired event never counts");

        (await client.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<CareerEventShape>>(Route))!.Should().BeEmpty();
        (await client.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnEventAlreadyInThePast_IsStoredAsHistory_AndNotCounted()
    {
        var past = new { kind = "OneOffIn", date = DaysAhead(-30), label = "Bonus", amount = 2_000m, currency = "USD" };

        var body = await (await NewPerson().PostAsJsonAsync(Route, past)).Content.ReadFromJsonAsync<CareerEventShape>();

        body!.Counted.Should().BeFalse("it is already in the baseline's history");
    }

    [Fact]
    public async Task AnotherPersonsEvent_IsNotListed_AndCannotBeChanged_Retired_OrRemoved()
    {
        var owner = NewPerson();
        var id = (await (await owner.PostAsJsonAsync(Route, Promotion())).Content.ReadFromJsonAsync<CareerEventShape>())!.Id;
        var other = NewPerson();

        (await other.GetFromJsonAsync<List<CareerEventShape>>(Route))!.Should().BeEmpty();
        var put = await other.PutAsJsonAsync($"{Route}/{id}", Promotion(amount: 1m));
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await put.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("CAREER_EVENT_NOT_FOUND");
        (await other.PostAsync($"{Route}/{id}/retire", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var kept = (await owner.GetFromJsonAsync<List<CareerEventShape>>(Route))!.Single();
        kept.Amount.Should().Be(500m);
        kept.Retired.Should().BeFalse();
    }

    public static TheoryData<object> InvalidBodies() => new()
    {
        new { kind = "IncomeStep", date = "2027-03-01", label = "", amount = 500m, currency = "EUR" },
        new { kind = "IncomeStep", date = "2027-03-01", label = "Promotion", amount = 0m, currency = "EUR" },
        new { kind = "OneOffIn", date = "2027-03-01", label = "Bonus", amount = -5m, currency = "EUR" },
        new { kind = "IncomeStep", date = "2027-03-01", label = "Promotion", amount = 500m, currency = "ZZZ" },
        new { kind = "Leave", date = "2027-03-01", label = "Leave", amount = 500m, currency = "EUR" },
        new { kind = "Leave", date = "2027-03-01", endDate = "2027-01-01", label = "Leave", amount = 500m, currency = "EUR" },
        new { kind = "OneOffIn", date = "2027-03-01", endDate = "2027-05-01", label = "Bonus", amount = 500m, currency = "EUR" },
        new { date = "2027-03-01", label = "Promotion", amount = 500m, currency = "EUR" },
        new { kind = "IncomeStep", label = "Promotion", amount = 500m, currency = "EUR" },
        new { kind = "IncomeStep", date = "2027-03-01", label = "Promotion", currency = "EUR" },
        new { kind = "IncomeStep", date = "2999-03-01", label = "Promotion", amount = 500m, currency = "EUR" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_WithAnInvalidBody_Returns400ValidationError(object body)
    {
        var response = await NewPerson().PostAsJsonAsync(Route, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }
}

public record CareerEventShape(
    Guid Id,
    string Kind,
    string Date,
    string? EndDate,
    string Label,
    decimal Amount,
    string Currency,
    string? Note,
    bool Counted,
    bool Retired);
