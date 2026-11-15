namespace FinanceSentry.Tests.Integration.Wealth;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

// ── Contract tests: /api/v1/wealth/tax-profile ───────────────────────────────

public class TaxProfileContractTests(WealthApiFactory factory) : IClassFixture<WealthApiFactory>
{
    private const string Route = "/api/v1/wealth/tax-profile";

    private readonly WealthApiFactory _factory = factory;

    private HttpClient NewPerson() => _factory.CreateAuthenticatedClient(Guid.NewGuid());

    [Fact]
    public async Task NoAuth_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PutAsJsonAsync(Route, new { jurisdiction = "IE" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync(Route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BeforeAnythingIsSet_TheProfileIsEmpty_WithEveryValueNull()
    {
        var response = await NewPerson().GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<TaxProfileShape>())!;
        (body.Jurisdiction, body.JurisdictionNotes, body.Caveat, body.UpdatedAt).Should().Be((null, null, null, null));
        (body.HasJurisdictionNotes, body.HasCaveat).Should().Be((false, false));
        body.Disclosure.Should().Contain("not tax advice");
    }

    [Fact]
    public async Task Put_StoresTheProfile_AndGetReadsItBack()
    {
        var client = NewPerson();

        var put = await client.PutAsJsonAsync(Route, new { jurisdiction = "ie", jurisdictionNotes = "Synthetic: PAYE employee", caveat = "Synthetic: not advice" });

        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var body = (await client.GetFromJsonAsync<TaxProfileShape>(Route))!;
        body.Jurisdiction.Should().Be("IE");
        body.JurisdictionNotes.Should().Be("Synthetic: PAYE employee");
        body.Caveat.Should().Be("Synthetic: not advice");
        (body.HasJurisdictionNotes, body.HasCaveat).Should().Be((true, true));
        body.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task APutRestatesTheWholeProfile_SoAnOmittedFieldIsClearedToNull()
    {
        var client = NewPerson();
        await client.PutAsJsonAsync(Route, new { jurisdiction = "IE", jurisdictionNotes = "notes", caveat = "caveat" });

        await client.PutAsJsonAsync(Route, new { jurisdiction = "IE" });

        var body = (await client.GetFromJsonAsync<TaxProfileShape>(Route))!;
        body.Jurisdiction.Should().Be("IE");
        body.JurisdictionNotes.Should().BeNull();
        body.Caveat.Should().BeNull();
    }

    [Fact]
    public async Task TheProfile_IsPerPerson()
    {
        var owner = NewPerson();
        await owner.PutAsJsonAsync(Route, new { jurisdiction = "IE", caveat = "owner only" });

        var other = (await NewPerson().GetFromJsonAsync<TaxProfileShape>(Route))!;

        other.Jurisdiction.Should().BeNull();
        other.Caveat.Should().BeNull();
        (await owner.GetFromJsonAsync<TaxProfileShape>(Route))!.Caveat.Should().Be("owner only");
    }

    [Fact]
    public async Task Delete_RemovesTheProfile_AndIsIdempotent()
    {
        var client = NewPerson();
        await client.PutAsJsonAsync(Route, new { jurisdiction = "IE" });

        (await client.DeleteAsync(Route)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync(Route)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.GetFromJsonAsync<TaxProfileShape>(Route))!.Jurisdiction.Should().BeNull();
    }

    public static TheoryData<object> InvalidBodies() => new()
    {
        new { },
        new { jurisdiction = "", jurisdictionNotes = " ", caveat = "" },
        new { jurisdiction = "IRL", caveat = "caveat" },
        new { jurisdiction = "I1", caveat = "caveat" },
        new { jurisdiction = "IE", caveat = new string('x', 2_001) },
        new { jurisdiction = "IE", jurisdictionNotes = new string('x', 2_001) },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Put_WithAnInvalidBody_Returns400ValidationError(object body)
    {
        var response = await NewPerson().PutAsJsonAsync(Route, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }
}

public record TaxProfileShape(
    string? Jurisdiction,
    string? JurisdictionNotes,
    string? Caveat,
    bool HasJurisdictionNotes,
    bool HasCaveat,
    DateTimeOffset? UpdatedAt,
    string Disclosure);
