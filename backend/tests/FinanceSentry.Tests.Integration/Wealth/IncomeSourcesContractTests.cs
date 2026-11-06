namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

// ── Contract tests: /api/v1/wealth/income-sources ────────────────────────────

public class IncomeSourcesContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private const string Route = "/api/v1/wealth/income-sources";

    private readonly WealthApiFactory _factory = factory;

    private static object DayJob(decimal amount = 3_000m, string cadence = "Monthly", string? endDate = null) => new
    {
        kind = "Salary",
        label = "Day job",
        amount,
        currency = "eur",
        cadence,
        grossOrNet = "Net",
        startDate = "2025-01-01",
        endDate,
    };

    private HttpClient NewPerson() => _factory.CreateAuthenticatedClient(Guid.NewGuid());

    [Fact]
    public async Task NoAuth_Returns401()
        => (await _factory.CreateClient().GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Create_ThenList_ReturnsTheSourceWithItsMonthlyEquivalent()
    {
        var client = NewPerson();

        var created = await client.PostAsJsonAsync(Route, DayJob(amount: 600m, cadence: "Weekly"));

        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await created.Content.ReadFromJsonAsync<IncomeSourceShape>();
        body!.Label.Should().Be("Day job");
        body.Currency.Should().Be("EUR");
        body.Kind.Should().Be("Salary");
        body.Cadence.Should().Be("Weekly");
        body.GrossOrNet.Should().Be("Net");
        body.MonthlyEquivalent.Should().Be(2_600m);
        body.Active.Should().BeTrue();
        created.Headers.Location!.ToString().Should().EndWith($"{Route}/{body.Id}");

        (await client.GetFromJsonAsync<List<IncomeSourceShape>>(Route))!.Should().ContainSingle().Which.Id.Should().Be(body.Id);
    }

    [Fact]
    public async Task AnIrregularSource_HasNoMonthlyEquivalent()
    {
        var body = await (await NewPerson().PostAsJsonAsync(Route, DayJob(cadence: "Irregular"))).Content.ReadFromJsonAsync<IncomeSourceShape>();

        body!.MonthlyEquivalent.Should().BeNull();
    }

    [Fact]
    public async Task Update_ChangesTheSource_Retire_EndsItToday_AndDelete_RemovesIt()
    {
        var client = NewPerson();
        var id = (await (await client.PostAsJsonAsync(Route, DayJob())).Content.ReadFromJsonAsync<IncomeSourceShape>())!.Id;

        var updated = await client.PutAsJsonAsync($"{Route}/{id}", DayJob(amount: 3_300m));
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<IncomeSourceShape>())!.Amount.Should().Be(3_300m);

        var retired = await client.PostAsync($"{Route}/{id}/retire", null);
        retired.StatusCode.Should().Be(HttpStatusCode.OK);
        var ended = await retired.Content.ReadFromJsonAsync<IncomeSourceShape>();
        ended!.Active.Should().BeTrue("it runs until the end of today");
        ended.EndDate.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"));

        (await client.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<List<IncomeSourceShape>>(Route))!.Should().BeEmpty();
        (await client.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnotherPersonsSource_IsNotListed_AndCannotBeChanged_Retired_OrRemoved()
    {
        var owner = NewPerson();
        var id = (await (await owner.PostAsJsonAsync(Route, DayJob())).Content.ReadFromJsonAsync<IncomeSourceShape>())!.Id;
        var other = NewPerson();

        (await other.GetFromJsonAsync<List<IncomeSourceShape>>(Route))!.Should().BeEmpty();
        var put = await other.PutAsJsonAsync($"{Route}/{id}", DayJob(amount: 1m));
        put.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await put.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("INCOME_SOURCE_NOT_FOUND");
        (await other.PostAsync($"{Route}/{id}/retire", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"{Route}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var kept = (await owner.GetFromJsonAsync<List<IncomeSourceShape>>(Route))!.Single();
        kept.Amount.Should().Be(3_000m);
        kept.EndDate.Should().BeNull();
    }

    public static TheoryData<object> InvalidBodies() => new()
    {
        new { kind = "Salary", label = "", amount = 3_000m, currency = "EUR", cadence = "Monthly", grossOrNet = "Net", startDate = "2025-01-01" },
        new { kind = "Salary", label = "Day job", amount = 0m, currency = "EUR", cadence = "Monthly", grossOrNet = "Net", startDate = "2025-01-01" },
        new { kind = "Salary", label = "Day job", amount = 3_000m, currency = "ZZZ", cadence = "Monthly", grossOrNet = "Net", startDate = "2025-01-01" },
        new { kind = "Salary", label = "Day job", amount = 3_000m, currency = "EUR", cadence = "Monthly", grossOrNet = "Net", startDate = "2025-01-01", endDate = "2024-01-01" },
        new { label = "Day job", amount = 3_000m, currency = "EUR", cadence = "Monthly", grossOrNet = "Net", startDate = "2025-01-01" },
        new { kind = "Salary", label = "Day job", amount = 3_000m, currency = "EUR", grossOrNet = "Net", startDate = "2025-01-01" },
        new { kind = "Salary", label = "Day job", amount = 3_000m, currency = "EUR", cadence = "Monthly", startDate = "2025-01-01" },
        new { kind = "Salary", label = "Day job", amount = 3_000m, currency = "EUR", cadence = "Monthly", grossOrNet = "Net" },
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

public record IncomeSourceShape(
    Guid Id,
    string Kind,
    string Label,
    decimal Amount,
    string Currency,
    string Cadence,
    string GrossOrNet,
    string StartDate,
    string? EndDate,
    decimal? MonthlyEquivalent,
    bool Active);
