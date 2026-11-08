namespace FinanceSentry.Tests.Integration.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Modules.Auth.API.Authentication;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

/// <summary>
/// The fleet-agent identity next to the user scheme. Tokens are signed here with a locally generated key the host is
/// told to trust (no provider is contacted); every account and transaction is seeded fake data. A valid token with the
/// right scope reads and recategorises; a wrong scope, an expired token, a token with no scope (what the provider
/// issues to a revoked or disabled application) and any token that is not the fleet's are refused; user sign-in is
/// unchanged and a fleet token reaches nothing outside the fleet endpoints.
/// </summary>
public class FleetAgentAuthenticationTests : IClassFixture<FleetAgentApiFactory>
{
    private const string Read = FleetScopes.Read;
    private const string Recategorise = FleetScopes.Recategorise;
    private const string ReadAndRecategorise = $"{FleetScopes.Read} {FleetScopes.Recategorise}";

    private readonly FleetAgentApiFactory _factory;

    public FleetAgentAuthenticationTests(FleetAgentApiFactory factory)
    {
        _factory = factory;
        _factory.Seed();
    }

    [Fact]
    public async Task Read_WithReadScope_ReturnsTheOwnersTransactionsOnly()
    {
        var response = await Get("/api/v1/fleet/transactions?limit=200", _factory.Token(Read));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = (await Items(response)).Select(i => i.GetProperty("transactionId").GetGuid()).ToList();
        ids.Should().Contain(_factory.FoodTransactionId);
        ids.Should().NotContain(_factory.OtherUsersTransactionId);
    }

    [Fact]
    public async Task Read_Categories_WithReadScope_ReturnsTheCanonicalList()
    {
        var response = await Get("/api/v1/fleet/categories", _factory.Token(Read));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(CategoryKeys.Travel);
    }

    [Fact]
    public async Task Recategorise_WithScope_ChangesTheCategory_AndPuttingThePreviousBackUndoesIt()
    {
        var token = _factory.Token(ReadAndRecategorise);
        var path = $"/api/v1/fleet/transactions/{_factory.RecategorisedTransactionId}/category";

        var change = await Put(path, token, CategoryKeys.FoodAndDrink);

        change.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await change.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("previousCategory").GetString().Should().Be(CategoryKeys.Entertainment);
        body.GetProperty("category").GetString().Should().Be(CategoryKeys.FoodAndDrink);
        body.GetProperty("changed").GetBoolean().Should().BeTrue();
        _factory.CategoryOf(_factory.RecategorisedTransactionId).Should().Be(CategoryKeys.FoodAndDrink);

        var undo = await Put(path, token, body.GetProperty("previousCategory").GetString()!);

        undo.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.CategoryOf(_factory.RecategorisedTransactionId).Should().Be(CategoryKeys.Entertainment);
    }

    [Fact]
    public async Task Recategorise_RecordsTheChangeOnTheAuditTrailAsTheFleetAgent()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.AuditedTransactionId}/category";

        (await Put(path, _factory.Token(Recategorise), CategoryKeys.Entertainment)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var events = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().AuditEvents
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Where(e => e.ResourceId == _factory.AuditedTransactionId)
            .ToListAsync();
        events.Should().ContainSingle();
        events[0].ActorKind.Should().Be(AuditActorKind.FleetAgent);
        events[0].SubjectUserId.Should().Be(_factory.OwnerId);
        events[0].FieldNames.Should().Equal("MerchantCategory");
    }

    [Theory]
    [InlineData(CategoryKeys.TransferOut)]
    [InlineData(CategoryKeys.Income)]
    [InlineData(CategoryKeys.LoanPayments)]
    [InlineData(CategoryKeys.RentAndUtilities)]
    [InlineData(CategoryKeys.Travel)]
    public async Task Recategorise_IntoAFlowCategoryOrAcrossTheDailySpendSet_Returns409_AndChangesNothing(string category)
    {
        var path = $"/api/v1/fleet/transactions/{_factory.FoodTransactionId}/category";

        var response = await Put(path, _factory.Token(Recategorise), category);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.CategoryOf(_factory.FoodTransactionId).Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public async Task Recategorise_OutOfAFlowCategory_Returns409()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.TransferTransactionId}/category";

        var response = await Put(path, _factory.Token(Recategorise), CategoryKeys.Travel);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.CategoryOf(_factory.TransferTransactionId).Should().Be(CategoryKeys.TransferOut);
    }

    [Fact]
    public async Task Recategorise_OutOfRentAndUtilities_Returns409()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.RentTransactionId}/category";

        var response = await Put(path, _factory.Token(Recategorise), CategoryKeys.GeneralServices);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.CategoryOf(_factory.RentTransactionId).Should().Be(CategoryKeys.RentAndUtilities);
    }

    [Fact]
    public async Task Recategorise_IntoTheDailySpendSetFromOutside_Returns409()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.ServicesTransactionId}/category";

        var response = await Put(path, _factory.Token(Recategorise), CategoryKeys.FoodAndDrink);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _factory.CategoryOf(_factory.ServicesTransactionId).Should().Be(CategoryKeys.GeneralServices);
    }

    [Fact]
    public async Task Recategorise_BetweenTwoCategoriesOutsideEverySet_Succeeds()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.OutsideSetsTransactionId}/category";

        var response = await Put(path, _factory.Token(Recategorise), CategoryKeys.Travel);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.CategoryOf(_factory.OutsideSetsTransactionId).Should().Be(CategoryKeys.Travel);
    }

    [Fact]
    public async Task Recategorise_ToAnUnknownCategory_Returns400()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.FoodTransactionId}/category";

        (await Put(path, _factory.Token(Recategorise), "NOT_A_CATEGORY")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Recategorise_AnotherPersonsTransaction_Returns404()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.OtherUsersTransactionId}/category";

        (await Put(path, _factory.Token(Recategorise), CategoryKeys.Travel)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.CategoryOf(_factory.OtherUsersTransactionId).Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Fact]
    public async Task Read_WithOnlyTheRecategoriseScope_Returns403()
    {
        (await Get("/api/v1/fleet/transactions", _factory.Token(Recategorise))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recategorise_WithOnlyTheReadScope_Returns403_AndChangesNothing()
    {
        var path = $"/api/v1/fleet/transactions/{_factory.FoodTransactionId}/category";

        (await Put(path, _factory.Token(Read), CategoryKeys.Travel)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _factory.CategoryOf(_factory.FoodTransactionId).Should().Be(CategoryKeys.FoodAndDrink);
    }

    [Theory]
    [InlineData("")]
    [InlineData("openid profile")]
    [InlineData("basket:price-ingest")]
    public async Task AnyEndpoint_WithATokenCarryingNoFleetScope_Returns401(string scope)
    {
        // What the provider issues to a revoked or disabled application: a valid token with no fleet scope.
        (await Get("/api/v1/fleet/transactions", _factory.Token(scope))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnyEndpoint_WithAnExpiredToken_Returns401()
    {
        var expired = _factory.Token(ReadAndRecategorise, issuedAt: DateTime.UtcNow.AddHours(-2), lifetime: TimeSpan.FromHours(1));

        (await Get("/api/v1/fleet/transactions", expired)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnyEndpoint_WithATokenForAnotherAudience_Returns401()
    {
        var token = _factory.Token(ReadAndRecategorise, audience: "https://other.lifekit.internal/api");

        (await Get("/api/v1/fleet/transactions", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnyEndpoint_WithATokenFromAnotherIssuer_Returns401()
    {
        var token = _factory.Token(ReadAndRecategorise, issuer: "https://elsewhere.test/oidc");

        (await Get("/api/v1/fleet/transactions", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnyEndpoint_WithATokenSignedByAnotherKey_Returns401()
    {
        var token = FleetAgentApiFactory.TokenSignedBy(
            new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "forged" },
            ReadAndRecategorise, FleetAgentApiFactory.ClientId, FleetAgentApiFactory.ClientId);

        (await Get("/api/v1/fleet/transactions", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnyEndpoint_WithATokenOfAnotherApplication_Returns401()
    {
        var token = _factory.Token(ReadAndRecategorise, clientId: "some-other-m2m-app", subject: "some-other-m2m-app");

        (await Get("/api/v1/fleet/transactions", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnyEndpoint_WithAPersonsTokenForTheSameAudience_Returns401()
    {
        // A person's access token carries the application they signed in with, never a subject equal to a client id.
        var token = _factory.Token(ReadAndRecategorise, subject: "a-person");

        (await Get("/api/v1/fleet/transactions", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FleetEndpoints_WithoutAToken_Return401()
    {
        (await Get("/api/v1/fleet/transactions", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FleetEndpoints_WithAUsersSessionToken_Return401()
    {
        var session = await _factory.SignInMemberAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={session}");
        (await client.GetAsync("/api/v1/fleet/transactions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session);
        (await bearer.GetAsync("/api/v1/fleet/transactions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserSignIn_IsUnchanged()
    {
        var session = await _factory.SignInMemberAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={session}");

        (await client.GetAsync("/api/v1/profile")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/api/v1/profile")]
    [InlineData("GET", "/api/v1/accounts/transactions")]
    [InlineData("DELETE", "/api/v1/crypto/binance/disconnect")]
    [InlineData("POST", "/api/v1/auth/mcp/service-token")]
    [InlineData("POST", "/api/v1/agent/chat")]
    public async Task FleetToken_CannotReachAnyEndpointOutsideTheFleetSurface(string method, string path)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.Token(ReadAndRecategorise));

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> Get(string path, string? token)
    {
        using var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(path);
    }

    private async Task<HttpResponseMessage> Put(string path, string token, string category)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.PutAsJsonAsync(path, new { category });
    }

    private static async Task<List<JsonElement>> Items(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. body.GetProperty("items").EnumerateArray()];
    }
}

/// <summary>What the fleet identity does when the deployment is not set up for it, or set up ambiguously.</summary>
public class FleetAgentAccountTests
{
    [Fact]
    public async Task WithTwoOwnersAndNoAccountConfigured_EveryFleetTokenIsRefused()
    {
        using var factory = new FleetAgentApiFactory { ExtraOwners = 1 };
        factory.Seed();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(FleetScopes.Read));

        (await client.GetAsync("/api/v1/fleet/transactions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithTwoOwners_TheConfiguredAccountIsTheOneTheFleetActsAs()
    {
        using var factory = new FleetAgentApiFactory { ExtraOwners = 1, ActAsOwner = true };
        factory.Seed();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(FleetScopes.Read));

        (await client.GetAsync("/api/v1/fleet/transactions")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhenTheOwnerIsLockedOut_EveryFleetTokenIsRefused()
    {
        using var factory = new FleetAgentApiFactory();
        factory.Seed();
        await factory.LockOutOwnerAsync();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(FleetScopes.Read));

        (await client.GetAsync("/api/v1/fleet/transactions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhenTheIdentityIsOff_EveryFleetTokenIsRefused()
    {
        using var factory = new FleetAgentApiFactory { Enabled = false };
        factory.Seed();

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(FleetScopes.Read));

        (await client.GetAsync("/api/v1/fleet/transactions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// An API host trusting a locally generated key as the identity provider, with one seeded Owner and fake transactions.
/// Each instance has its own auth store (see <see cref="AuthApiFactory"/>), so owner counts can differ per test.
/// </summary>
public sealed class FleetAgentApiFactory : AuthApiFactory
{
    /// <summary>Owners seeded besides <see cref="OwnerId"/> (read when the host first starts).</summary>
    public int ExtraOwners { get; init; }

    /// <summary>Whether <c>Auth:FleetAgent:ActAsUserId</c> names <see cref="OwnerId"/>.</summary>
    public bool ActAsOwner { get; init; }

    public bool Enabled { get; init; } = true;

    public const string Authority = "https://identity.test/oidc";
    public const string ClientId = "fleet-agent-test-app";
    public const string Resource = FleetAgentOptions.DefaultAudience;

    // The BankSync in-memory store is shared by every host in the process, so each factory seeds ids of its own.
    public Guid OwnerId { get; } = Guid.NewGuid();
    public Guid OtherUserId { get; } = Guid.NewGuid();
    public Guid FoodTransactionId { get; } = Guid.NewGuid();
    public Guid RecategorisedTransactionId { get; } = Guid.NewGuid();
    public Guid AuditedTransactionId { get; } = Guid.NewGuid();
    public Guid RentTransactionId { get; } = Guid.NewGuid();
    public Guid ServicesTransactionId { get; } = Guid.NewGuid();
    public Guid OutsideSetsTransactionId { get; } = Guid.NewGuid();
    public Guid TransferTransactionId { get; } = Guid.NewGuid();
    public Guid OtherUsersTransactionId { get; } = Guid.NewGuid();

    private static readonly ECDsaSecurityKey SigningKey = new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = "fleet-test" };

    private readonly Lock _seedLock = new();
    private bool _seeded;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:FleetAgent:Authority", Authority);
        builder.UseSetting("Auth:FleetAgent:ClientId", ClientId);
        builder.UseSetting("Auth:FleetAgent:Enabled", Enabled.ToString());
        if (ActAsOwner)
            builder.UseSetting("Auth:FleetAgent:ActAsUserId", OwnerId.ToString());

        var configuration = new OpenIdConnectConfiguration { Issuer = Authority };
        configuration.SigningKeys.Add(SigningKey);
        builder.ConfigureServices(services => services.PostConfigure<JwtBearerOptions>(
            FleetAgentOptions.Scheme,
            options => options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration)));
    }

    /// <summary>A token as the identity provider issues it to the fleet-agent application (client credentials).</summary>
    public string Token(
        string scope,
        string? clientId = null,
        string? subject = null,
        string? issuer = null,
        string? audience = null,
        DateTime? issuedAt = null,
        TimeSpan? lifetime = null)
    {
        var client = clientId ?? ClientId;
        return TokenSignedBy(SigningKey, scope, client, subject ?? client, issuer, audience, issuedAt, lifetime);
    }

    public static string TokenSignedBy(
        SecurityKey key,
        string scope,
        string clientId,
        string subject,
        string? issuer = null,
        string? audience = null,
        DateTime? issuedAt = null,
        TimeSpan? lifetime = null)
    {
        var start = issuedAt ?? DateTime.UtcNow.AddMinutes(-1);
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", subject), new Claim("client_id", clientId), new Claim("scope", scope)]),
            Issuer = issuer ?? Authority,
            Audience = audience ?? Resource,
            NotBefore = start,
            IssuedAt = start,
            Expires = start.Add(lifetime ?? TimeSpan.FromHours(1)),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
        }));
    }

    public void Seed()
    {
        lock (_seedLock)
        {
            if (_seeded)
                return;
            _seeded = true;
        }

        TestUsers.EnsureExists(Services, OwnerId, AuthRoles.Owner);
        TestUsers.EnsureExists(Services, OtherUserId);
        for (var i = 0; i < ExtraOwners; i++)
            TestUsers.EnsureExists(Services, Guid.NewGuid(), AuthRoles.Owner);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankSyncDbContext>();
        foreach (var key in new[]
                 {
                     CategoryKeys.FoodAndDrink, CategoryKeys.Entertainment, CategoryKeys.Travel, CategoryKeys.TransferOut,
                     CategoryKeys.Income, CategoryKeys.LoanPayments, CategoryKeys.RentAndUtilities, CategoryKeys.GeneralServices,
                 })
        {
            if (!db.Categories.Any(c => c.Key == key))
                db.Categories.Add(new Category { Key = key, Label = key, SortOrder = 1 });
        }

        var ownerAccount = new BankAccount(OwnerId, $"ext-{OwnerId:N}", "Fake Bank", "checking", "1111", "Fake Owner", "USD", OwnerId, "truelayer");
        var otherAccount = new BankAccount(OtherUserId, $"ext-{OtherUserId:N}", "Fake Bank", "checking", "2222", "Fake Other", "USD", OtherUserId, "truelayer");
        db.Set<BankAccount>().AddRange(ownerAccount, otherAccount);
        db.Set<Transaction>().AddRange(
            Fake(FoodTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.FoodAndDrink),
            Fake(RecategorisedTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.Entertainment),
            Fake(AuditedTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.FoodAndDrink),
            Fake(TransferTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.TransferOut),
            Fake(RentTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.RentAndUtilities),
            Fake(ServicesTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.GeneralServices),
            Fake(OutsideSetsTransactionId, OwnerId, ownerAccount.Id, CategoryKeys.GeneralServices),
            Fake(OtherUsersTransactionId, OtherUserId, otherAccount.Id, CategoryKeys.FoodAndDrink));
        db.SaveChanges();
    }

    public string? CategoryOf(Guid transactionId)
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<BankSyncDbContext>().Set<Transaction>()
            .IgnoreQueryFilters([OwnerQueryFilter.Name])
            .Single(t => t.Id == transactionId).MerchantCategory;
    }

    /// <summary>A password session of a Member (a second Owner would make the fleet's account ambiguous).</summary>
    public async Task<string> SignInMemberAsync()
    {
        const string email = "fleet-member@test.com";
        const string password = "TestPass123!";
        await EnsureUserExistsAsync(email, password);
        return await SignInAsync(email, password);
    }

    public async Task LockOutOwnerAsync()
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var owner = (await users.FindByIdAsync(OwnerId.ToString()))!;
        await users.SetLockoutEnabledAsync(owner, true);
        await users.SetLockoutEndDateAsync(owner, DateTimeOffset.UtcNow.AddHours(1));
    }

    private static Transaction Fake(Guid id, Guid userId, Guid accountId, string category)
    {
        var transaction = Build(userId, accountId, category, id);
        // Entity.Id is init-only (assigned by the entity); the tests need known ids to address rows over HTTP.
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(transaction, id);
        return transaction;
    }

    private static Transaction Build(Guid userId, Guid accountId, string category, Guid id) => new()
    {
        AccountId = accountId,
        UserId = userId,
        Amount = 12.5m,
        TransactionDate = DateTime.UtcNow.AddDays(-1),
        PostedDate = DateTime.UtcNow.AddDays(-1),
        TransactionType = "debit",
        MerchantName = "Fake Merchant",
        MerchantCategory = category,
        Description = "Fake Merchant",
        UniqueHash = id.ToString("N"),
        IsActive = true,
    };
}
