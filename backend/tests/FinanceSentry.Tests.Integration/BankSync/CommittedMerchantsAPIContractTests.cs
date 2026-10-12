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
/// The rejection and authentication paths run over the factory's in-memory BankSync context; the
/// paths that store a pin run on real Postgres in <see cref="CommittedMerchantsPostgresContractTests"/>,
/// because the pin insert is raw <c>INSERT ... ON CONFLICT</c> SQL the in-memory provider cannot run.
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
    public async Task Post_MerchantWithNoNameableText_Returns400()
    {
        var response = await _client.PostAsync(Url, Body("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a pin on the unnameable key would claim every unnamed debit as committed");
        var error = await response.Content.ReadFromJsonAsync<ErrorShape>();
        error!.ErrorCode.Should().Be("VALIDATION_ERROR");
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
