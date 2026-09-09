namespace FinanceSentry.Tests.Integration.BankSync;

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

/// <summary>
/// REST contract for the committed-merchant pins behind rule (d) of the committed-outflow
/// policy (spec 554):
///   GET    /api/v1/committed-merchants
///   POST   /api/v1/committed-merchants
///   DELETE /api/v1/committed-merchants?merchant=…
///
/// Runs against the real repository over the factory's in-memory BankSync context, so the key a
/// pin is stored under is the same one the policy reads back.
/// </summary>
public class CommittedMerchantsAPIContractTests(BankSyncApiFactory factory)
    : IClassFixture<BankSyncApiFactory>
{
    private readonly HttpClient _client = factory.CreateAuthenticatedClient();

    private const string Url = "/api/v1/committed-merchants";

    private sealed record PinShape(Guid Id, string MerchantKey, string DisplayName, DateTime PinnedAt);

    private sealed record ErrorShape(string Error, string ErrorCode);

    private static HttpContent Body(string merchant) => JsonContent.Create(new { merchant });

    [Fact]
    public async Task Post_NewMerchant_Returns201_WithThePinShape()
    {
        var response = await _client.PostAsync(Url, Body("Anytime Fitness Dublin"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var pin = await response.Content.ReadFromJsonAsync<PinShape>();
        pin.Should().NotBeNull();
        pin!.MerchantKey.Should().Be("anytime fitness dublin");
        pin.DisplayName.Should().Be("Anytime Fitness Dublin");
        pin.Id.Should().NotBeEmpty();
        pin.PinnedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task Post_MerchantAlreadyPinned_Returns200_NotAConflict()
    {
        await _client.PostAsync(Url, Body("Kredobank Mortgage"));

        var response = await _client.PostAsync(Url, Body("KREDOBANK MORTGAGE"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "re-pinning asks for a state that already holds — two spellings are one pin");
        var pin = await response.Content.ReadFromJsonAsync<PinShape>();
        pin!.MerchantKey.Should().Be("kredobank mortgage");
    }

    [Fact]
    public async Task Post_MerchantWithNoNameableText_Returns400()
    {
        var response = await _client.PostAsync(Url, Body("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a pin on the unnameable key would claim every unnamed debit as committed");
        var error = await response.Content.ReadFromJsonAsync<ErrorShape>();
        error!.ErrorCode.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public async Task Get_ReturnsTheCallersPinsAsAFlatList()
    {
        await _client.PostAsync(Url, Body("Vodafone Home Broadband"));

        var response = await _client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pins = await response.Content.ReadFromJsonAsync<List<PinShape>>();
        pins.Should().NotBeNull();
        pins!.Should().Contain(p =>
            p.MerchantKey == "vodafone home broadband" && p.DisplayName == "Vodafone Home Broadband");
    }

    [Fact]
    public async Task Delete_PinnedMerchant_Returns204_AndDropsItFromTheListing()
    {
        await _client.PostAsync(Url, Body("Bord Gais Energy"));

        var response = await _client.DeleteAsync($"{Url}?merchant=bord%20gais%20energy");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var pins = await _client.GetFromJsonAsync<List<PinShape>>(Url);
        pins.Should().NotContain(p => p.MerchantKey == "bord gais energy");
    }

    [Fact]
    public async Task Delete_MerchantThatWasNeverPinned_Returns404()
    {
        var response = await _client.DeleteAsync($"{Url}?merchant=never%20pinned%20shop");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<ErrorShape>();
        error!.ErrorCode.Should().Be("COMMITTED_PIN_NOT_FOUND");
    }

    [Fact]
    public async Task Endpoints_RequireAuthentication()
    {
        var anonymous = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
            });

        (await anonymous.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(Url, Body("Anytime Fitness"))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
