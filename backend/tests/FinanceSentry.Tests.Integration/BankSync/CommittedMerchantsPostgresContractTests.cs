namespace FinanceSentry.Tests.Integration.BankSync;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

/// <summary>
/// The REST paths of /api/v1/committed-merchants that store a pin, over the real repository on real
/// Postgres: pinning is one <c>INSERT ... ON CONFLICT DO NOTHING</c>, which the in-memory provider cannot
/// run, and the key a pin is stored under is the one the committed-outflow policy (spec 554) reads back.
/// The schema is built by the API's own startup migrations against a fresh database. Requires Docker
/// (<see cref="DockerRequiredFactAttribute"/> skips otherwise; CI has it).
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgresClusterStateCollection.Name)]
public sealed class CommittedMerchantsPostgresContractTests : IAsyncLifetime
{
    private const string Url = "/api/v1/committed-merchants";

    private readonly Guid _user = Guid.NewGuid();
    private TestDatabase? _database;
    private PostgresApiFactory? _factory;
    private HttpClient? _client;

    private sealed record PinShape(Guid Id, string MerchantKey, string DisplayName, DateTime PinnedAt);

    private static HttpContent Body(string merchant) => JsonContent.Create(new { merchant });

    public async Task InitializeAsync()
    {
        _database = await PostgresServer.Postgres14.CreateDatabaseAsync(resetsClusterRoles: true);

        _factory = new PostgresApiFactory(_database.ConnectionString);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        TestUsers.EnsureExists(_factory.Services, _user);
        _client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={BankSyncApiFactory.GenerateTestJwt(_user)}");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [DockerRequiredFact]
    public async Task Post_NewMerchant_Returns201_WithThePinShape()
    {
        var response = await _client!.PostAsync(Url, Body("Anytime Fitness Dublin"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var pin = await response.Content.ReadFromJsonAsync<PinShape>();
        pin.Should().NotBeNull();
        pin!.MerchantKey.Should().Be("anytime fitness dublin");
        pin.DisplayName.Should().Be("Anytime Fitness Dublin");
        pin.Id.Should().NotBeEmpty();
        pin.PinnedAt.Should().NotBe(default);
    }

    [DockerRequiredFact]
    public async Task Post_MerchantAlreadyPinned_Returns200_NotAConflict()
    {
        var first = await _client!.PostAsync(Url, Body("Kredobank Mortgage"));
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.PostAsync(Url, Body("KREDOBANK MORTGAGE"));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "re-pinning asks for a state that already holds — two spellings are one pin");
        var pin = await response.Content.ReadFromJsonAsync<PinShape>();
        pin!.MerchantKey.Should().Be("kredobank mortgage");
        pin.DisplayName.Should().Be("Kredobank Mortgage", "the first spelling is kept");
    }

    [DockerRequiredFact]
    public async Task Get_ReturnsTheCallersPinsAsAFlatList()
    {
        (await _client!.PostAsync(Url, Body("Vodafone Home Broadband"))).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var response = await _client.GetAsync(Url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pins = await response.Content.ReadFromJsonAsync<List<PinShape>>();
        pins.Should().NotBeNull();
        pins!.Should().ContainSingle(p =>
            p.MerchantKey == "vodafone home broadband" && p.DisplayName == "Vodafone Home Broadband");
    }

    [DockerRequiredFact]
    public async Task Delete_PinnedMerchant_Returns204_AndDropsItFromTheListing()
    {
        (await _client!.PostAsync(Url, Body("Bord Gais Energy"))).StatusCode
            .Should().Be(HttpStatusCode.Created);

        var response = await _client.DeleteAsync($"{Url}?merchant=bord%20gais%20energy");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var pins = await _client.GetFromJsonAsync<List<PinShape>>(Url);
        pins.Should().NotContain(p => p.MerchantKey == "bord gais energy");
    }

    private sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:ReadOnly", connectionString);
            builder.UseSetting("Deduplication:MasterKeyBase64",
                "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
            builder.UseSetting("Encryption:CurrentKeyVersion", "1");
            builder.UseSetting("Encryption:Keys:1",
                "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
            builder.UseSetting("Jwt:Secret",
                "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
            builder.UseSetting("Jwt:ExpiryMinutes", "60");
        }
    }
}
