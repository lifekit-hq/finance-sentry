namespace FinanceSentry.Tests.Integration.Companion;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

public class PushNotificationsContractTests(PushApiFactory factory) : IClassFixture<PushApiFactory>
{
    private const string Route = "/api/v1/notifications/push";

    private static object Body(string endpoint = "https://fcm.googleapis.com/fcm/send/abc") =>
        new { endpoint, keys = new { p256dh = "BPk", auth = "secret" } };

    [Fact]
    public async Task Every_endpoint_requires_sign_in()
    {
        var anon = factory.CreateClient();
        (await anon.GetAsync($"{Route}/public-key")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync($"{Route}/subscriptions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PutAsJsonAsync($"{Route}/preferences", new { pushEnabled = true })).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Keyless_server_reports_push_unavailable_and_refuses_subscriptions()
    {
        using var keyless = new PushApiFactory(configured: false);
        var client = keyless.CreateAuthenticatedClient();

        var key = await (await client.GetAsync($"{Route}/public-key")).Content.ReadFromJsonAsync<PushPublicKeyDto>();
        key!.Available.Should().BeFalse();
        key.PublicKey.Should().BeNull();

        var response = await client.PostAsJsonAsync($"{Route}/subscriptions", Body());
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PUSH_UNAVAILABLE");
        keyless.Subscriptions.Verify(
            r => r.UpsertByEndpointAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()), Times.Never);

        (await client.PutAsJsonAsync($"{Route}/preferences", new { pushEnabled = true })).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Configured_server_serves_only_the_public_key()
    {
        var response = await factory.CreateAuthenticatedClient().GetAsync($"{Route}/public-key");

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().Contain(PushApiFactory.PublicKey);
        raw.Should().NotContain(PushApiFactory.PrivateKey);
    }

    [Fact]
    public async Task Subscribe_stores_the_device_for_the_caller()
    {
        PushSubscription? stored = null;
        factory.Subscriptions
            .Setup(r => r.UpsertByEndpointAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<PushSubscription, CancellationToken>((s, _) => stored = s)
            .ReturnsAsync((PushSubscription s, CancellationToken _) => s);
        var client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Linux; Android 14) Chrome/126.0 Mobile Safari/537.36");

        var response = await client.PostAsJsonAsync($"{Route}/subscriptions", Body());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stored!.UserId.Should().Be(factory.TestUserId);
        stored.Endpoint.Should().Be("https://fcm.googleapis.com/fcm/send/abc");
        stored.DeviceLabel.Should().Be("Chrome on Android");
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("not a url")]
    [InlineData("https://push.example/abc")]
    [InlineData("https://fcm.googleapis.com.evil.example/abc")]
    [InlineData("https://evilnotify.windows.com/abc")]
    [InlineData("https://127.0.0.1/abc")]
    [InlineData("https://[::1]/abc")]
    [InlineData("https://localhost/abc")]
    [InlineData("https://user:pw@fcm.googleapis.com/abc")]
    [InlineData("https://fcm.googleapis.com:8443/abc")]
    public async Task Subscribe_rejects_an_endpoint_outside_the_push_service_allowlist(string endpoint)
    {
        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync($"{Route}/subscriptions", Body(endpoint));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PUSH_SUBSCRIPTION_INVALID");
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://eu.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc")]
    [InlineData("https://web.push.apple.com/abc")]
    [InlineData("https://api.push.apple.com/3/device/abc")]
    [InlineData("https://FCM.googleapis.com:443/fcm/send/abc")]
    public async Task Subscribe_accepts_known_push_service_hosts(string endpoint)
    {
        factory.Subscriptions
            .Setup(r => r.UpsertByEndpointAsync(It.IsAny<PushSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PushSubscription s, CancellationToken _) => s);

        var response = await factory.CreateAuthenticatedClient().PostAsJsonAsync($"{Route}/subscriptions", Body(endpoint));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Subscribe_rejects_a_body_without_keys()
    {
        var response = await factory.CreateAuthenticatedClient()
            .PostAsJsonAsync($"{Route}/subscriptions", new { endpoint = "https://fcm.googleapis.com/fcm/send/abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unsubscribe_unknown_device_returns_404_and_known_returns_204()
    {
        factory.Subscriptions
            .Setup(r => r.RemoveAsync(factory.TestUserId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var client = factory.CreateAuthenticatedClient();
        (await client.DeleteAsync($"{Route}/subscriptions/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var known = Guid.NewGuid();
        factory.Subscriptions.Setup(r => r.RemoveAsync(factory.TestUserId, known, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        (await client.DeleteAsync($"{Route}/subscriptions/{known}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Preferences_default_off_and_turning_push_on_leaves_the_notification_mode_alone()
    {
        CompanionNotificationSetting? saved = null;
        factory.Settings
            .Setup(r => r.GetOrDefaultAsync(factory.TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CompanionNotificationSetting { UserId = factory.TestUserId, Mode = NotificationMode.Quiet });
        factory.Settings
            .Setup(r => r.UpsertAsync(It.IsAny<CompanionNotificationSetting>(), It.IsAny<CancellationToken>()))
            .Callback<CompanionNotificationSetting, CancellationToken>((s, _) => saved = s)
            .Returns(Task.CompletedTask);
        var client = factory.CreateAuthenticatedClient();

        var before = await (await client.GetAsync($"{Route}/preferences")).Content.ReadFromJsonAsync<PushPreferencesDto>();
        before!.PushEnabled.Should().BeFalse();

        var put = await client.PutAsJsonAsync($"{Route}/preferences", new { pushEnabled = true });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        saved!.PushEnabled.Should().BeTrue();
        saved.Mode.Should().Be(NotificationMode.Quiet, "push is independent of Ledger's notification mode");
    }
}

public class PushApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _configured;

    public PushApiFactory() : this(true)
    {
    }

    internal PushApiFactory(bool configured) => _configured = configured;

    public const string PublicKey = "BPublicKeyForTests";
    public const string PrivateKey = "private-key-for-tests";
    private const string Secret = "test-jwt-secret-key-for-integration-tests-minimum-32-chars";

    public Mock<IPushSubscriptionRepository> Subscriptions { get; } = new(MockBehavior.Loose);
    public Mock<INotificationSettingRepository> Settings { get; } = new(MockBehavior.Loose);
    public Guid TestUserId { get; } = Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            Replace(services, Subscriptions.Object);
            Replace(services, Settings.Object);
            ReplaceDbContextWithInMemory<FinanceSentry.Modules.Auth.Infrastructure.Persistence.AuthDbContext>(
                services, $"PushTestAuth_{Guid.NewGuid()}");
        });

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("Deduplication:MasterKeyBase64", "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Encryption:CurrentKeyVersion", "1");
        builder.UseSetting("Encryption:Keys:1", "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Jwt:Secret", Secret);
        if (_configured)
        {
            builder.UseSetting("WebPush:PublicKey", PublicKey);
            builder.UseSetting("WebPush:PrivateKey", PrivateKey);
        }
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        TestUsers.EnsureExists(Services, TestUserId);
        client.DefaultRequestHeaders.Add("Cookie", $"fs_access_token={GenerateJwt(TestUserId)}");
        return client;
    }

    private static string GenerateJwt(Guid userId)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", userId.ToString())]),
            Audience = FinanceSentry.Core.Auth.AuthAudiences.App,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(System.Text.Encoding.ASCII.GetBytes(Secret)), SecurityAlgorithms.HmacSha256),
        }));
    }

    private static void Replace<T>(IServiceCollection services, T implementation) where T : class
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null) services.Remove(descriptor);
        services.AddScoped(_ => implementation);
    }

    private static void ReplaceDbContextWithInMemory<TContext>(IServiceCollection services, string dbName)
        where TContext : DbContext
    {
        foreach (var d in services
                     .Where(d => d.ServiceType == typeof(DbContextOptions<TContext>)
                              || d.ServiceType == typeof(TContext)
                              || d.ServiceType == typeof(IDbContextOptionsConfiguration<TContext>))
                     .ToList())
            services.Remove(d);

        services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(dbName));
    }
}
