namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Infrastructure.Authentication;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

/// <summary>
/// The Data Protection key ring lives in the Auth database (issue #930), so every app instance built from the same
/// database reads the same keys. Without it each process mints its own in-memory ring and an invite link issued
/// before a deploy is rejected by the process that comes up after it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DataProtectionKeyRingCollection
{
    public const string Name = "Data Protection key ring (process environment)";
}

/// <remarks>
/// The api container is read-only with HOME=/, so the platform's default key location (~/.aspnet) cannot be created
/// and each process falls back to an in-memory ring. HOME is pointed at a regular file for the test to get the same
/// result on any machine (a developer box would otherwise share keys through ~/.aspnet and pass for the wrong
/// reason), which is why the class runs alone.
/// </remarks>
[Collection(DataProtectionKeyRingCollection.Name)]
public sealed class DataProtectionKeyRingTests : IDisposable
{
    private readonly string? _home = Environment.GetEnvironmentVariable("HOME");
    private readonly string _homeFile = Path.GetTempFileName();

    public DataProtectionKeyRingTests() => Environment.SetEnvironmentVariable("HOME", _homeFile);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _home);
        File.Delete(_homeFile);
    }

    [Fact]
    public async Task InviteToken_IssuedByOneInstance_ValidatesInASecondInstanceOverTheSameDatabase()
    {
        var sharedStore = new InMemoryDatabaseRoot();
        await using var first = new SharedAuthStoreFactory(sharedStore);
        await using var second = new SharedAuthStoreFactory(sharedStore);
        var (userId, token) = await first.CreatePendingInviteAsync("friend-keyring@test.com");

        var accept = await second.CookieClient().PostAsJsonAsync("/api/v1/auth/invite/accept",
            new { userId, token, password = "quiet lantern orchard" });

        accept.StatusCode.Should().Be(HttpStatusCode.OK, "both instances must protect and unprotect with the persisted key ring");
    }

    [Fact]
    public void KeyRing_WithACertificateConfigured_IsStoredEncryptedAndReadableByAnotherInstance()
    {
        using var certificate = SelfSignedCertificate();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [AuthDataProtectionExtensions.CertificateConfigKey] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx)),
        }).Build();
        var sharedStore = new InMemoryDatabaseRoot();
        using var first = ProviderOver(sharedStore, config);
        using var second = ProviderOver(sharedStore, config);

        var protectedPayload = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("secret");

        second.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Unprotect(protectedPayload).Should().Be("secret");
        using var scope = first.CreateScope();
        var keys = scope.ServiceProvider.GetRequiredService<AuthDbContext>().DataProtectionKeys.ToList();
        keys.Should().NotBeEmpty();
        keys.All(k => k.Xml!.Contains("<EncryptedData") && !k.Xml.Contains("masterKey")).Should().BeTrue("the key material must not sit in the table in the clear");
    }

    private static ServiceProvider ProviderOver(InMemoryDatabaseRoot sharedStore, IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(NoCurrentUser.Instance);
        services.AddDbContext<AuthDbContext>(o => o.UseInMemoryDatabase("auth-keyring-encrypted", sharedStore));
        services.AddAuthDataProtection(config);
        return services.BuildServiceProvider();
    }

    private static X509Certificate2 SelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=finance-sentry-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    /// <summary>One API host whose Auth database is shared with every other instance given the same root.</summary>
    private sealed class SharedAuthStoreFactory(InMemoryDatabaseRoot sharedStore) : AuthApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
                ReplaceWithInMemory<AuthDbContext>(
                    services, "auth-keyring-shared", sharedStore));
        }
    }
}
