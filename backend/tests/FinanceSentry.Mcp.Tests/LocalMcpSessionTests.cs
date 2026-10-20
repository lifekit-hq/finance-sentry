using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Core.Auth;
using FinanceSentry.Mcp.Abstractions;
using FinanceSentry.Modules.Auth.API.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// The stdio host's check of its stored access token: it shares the HTTP hosts' validation parameters (so it
/// pins HS256 too), and an absent <c>Jwt:Secret</c> leaves it without a principal rather than failing startup.
/// </summary>
public sealed class LocalMcpSessionTests
{
    // 64+ bytes so an HS512 token can be signed with the same key.
    private const string Secret = "local-mcp-session-tests-secret-0123456789-0123456789-0123456789-0123";

    [Fact]
    public async Task Accepts_An_Hs256_Token()
    {
        var session = await InitializeAsync(Secret, Token(SecurityAlgorithms.HmacSha256));

        session.GetPrincipal().Should().NotBeNull();
    }

    [Fact]
    public async Task Rejects_A_Token_Signed_With_A_Non_Hs256_Algorithm()
    {
        var session = await InitializeAsync(Secret, Token(SecurityAlgorithms.HmacSha512));

        session.GetPrincipal().Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Has_No_Principal_When_The_Secret_Is_Absent_And_Does_Not_Throw(string? secret)
    {
        var session = await InitializeAsync(secret, Token(SecurityAlgorithms.HmacSha256));

        session.GetPrincipal().Should().BeNull();
    }

    private static async Task<LocalMcpSession> InitializeAsync(string? secret, string token)
    {
        var credentialFile = Path.Combine(Path.GetTempPath(), $"mcp-auth-test-{Guid.NewGuid():N}", "mcp-auth.json");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:CredentialFile"] = credentialFile })
            .Build();
        var store = new LocalMcpCredentialStore(Options.Create(new McpOptions { CredentialFile = credentialFile }));
        store.Save(new StoredMcpCredentials(
            "http://localhost:5001/api/v1",
            "http://localhost:4200",
            token,
            "refresh-token",
            DateTime.UtcNow.AddHours(1),
            Guid.NewGuid().ToString(),
            "user@example.com",
            "mcp.full_access"));

        var session = new LocalMcpSession(
            store,
            new ServiceCollection().AddLogging().AddMcpOAuthTokenClient().BuildServiceProvider()
                .GetRequiredService<McpOAuthTokenClient>(),
            Options.Create(new JwtOptions { Secret = secret }),
            NullLogger<LocalMcpSession>.Instance);
        await session.InitializeAsync();
        return session;
    }

    private static string Token(string algorithm) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            audience: AuthAudiences.Mcp,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), algorithm)));
}
