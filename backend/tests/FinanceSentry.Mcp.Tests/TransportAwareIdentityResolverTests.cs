using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Auth.API.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class TransportAwareIdentityResolverTests
{
    [Fact]
    public void GetUserId_Prefers_HttpContext_User_When_Authenticated()
    {
        var requestUserId = Guid.NewGuid();

        var localSession = CreateLocalSession(Guid.NewGuid(), "startup@example.com");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreatePrincipal(requestUserId, "request@example.com")
            }
        };

        var resolver = new TransportAwareIdentityResolver(accessor, localSession);

        resolver.GetUserId().Should().Be(requestUserId);
        resolver.GetEmail().Should().Be("request@example.com");
        resolver.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void GetUserId_Falls_Back_To_Local_Credentials_When_No_HttpContext_User()
    {
        var startupUserId = Guid.NewGuid();
        var localSession = CreateLocalSession(startupUserId, "startup@example.com");
        var resolver = new TransportAwareIdentityResolver(new HttpContextAccessor(), localSession);

        resolver.GetUserId().Should().Be(startupUserId);
        resolver.GetEmail().Should().Be("startup@example.com");
        resolver.IsConfigured.Should().BeTrue();
    }

    private static LocalMcpSession CreateLocalSession(Guid userId, string email)
    {
        const string secret = "super-secret-key-for-mcp-tests-123456";
        var token = CreateJwt(secret, userId, email, audience: "mcp");
        var tempDir = Path.Combine(Path.GetTempPath(), $"mcp-auth-test-{Guid.NewGuid():N}");
        var credentialFile = Path.Combine(tempDir, "mcp-auth.json");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret,
                ["Mcp:CredentialFile"] = credentialFile
            })
            .Build();

        var store = new LocalMcpCredentialStore(config);
        store.Save(new StoredMcpCredentials(
            "http://localhost:5001/api/v1",
            "http://localhost:4200",
            token,
            "refresh-token",
            DateTime.UtcNow.AddHours(1),
            userId.ToString(),
            email,
            "mcp.full_access"));

        var session = new LocalMcpSession(
            store,
            new ServiceCollection().AddLogging().AddMcpOAuthTokenClient().BuildServiceProvider()
                .GetRequiredService<McpOAuthTokenClient>(),
            Options.Create(new JwtOptions { Secret = secret }),
            NullLogger<LocalMcpSession>.Instance);
        session.InitializeAsync().GetAwaiter().GetResult();
        return session;
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, string email)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email)
        ], "test"));

    private static string CreateJwt(string secret, Guid userId, string email, string? audience = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email)
        };

        if (!string.IsNullOrWhiteSpace(audience))
            claims.Add(new Claim(JwtRegisteredClaimNames.Aud, audience));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
