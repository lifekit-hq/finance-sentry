using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Mcp.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace FinanceSentry.Mcp.Tests;

public sealed class McpJwtAuthenticationMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_Accepts_Bearer_Token_And_Sets_User()
    {
        const string secret = "super-secret-key-for-mcp-tests-123456";
        var userId = Guid.NewGuid();
        var token = CreateJwt(secret, userId, "user@example.com");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret
            })
            .Build();

        ClaimsPrincipal? principalSeen = null;
        var middleware = new McpJwtAuthenticationMiddleware(
            context =>
            {
                principalSeen = context.User;
                return Task.CompletedTask;
            },
            config,
            NullLogger<McpJwtAuthenticationMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";

        await middleware.InvokeAsync(context);

        principalSeen.Should().NotBeNull();
        principalSeen!.FindFirst(ClaimTypes.NameIdentifier)?.Value
            .Should().Be(userId.ToString());
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_Returns_401_When_Token_Missing()
    {
        const string secret = "super-secret-key-for-mcp-tests-123456";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret
            })
            .Build();

        var middleware = new McpJwtAuthenticationMiddleware(
            _ => Task.CompletedTask,
            config,
            NullLogger<McpJwtAuthenticationMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Be("Bearer");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    [InlineData("/metrics")]
    public async Task InvokeAsync_Passes_Anonymous_Platform_Paths_Through_Without_Token(string path)
    {
        var (middleware, reachedNext) = MiddlewareCapturingNext();
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        reachedNext().Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("/health/")]
    [InlineData("/health/db")]
    [InlineData("/HEALTH")]
    [InlineData("/")]
    public async Task InvokeAsync_Keeps_401_On_Every_Other_Path_Without_Token(string path)
    {
        var (middleware, reachedNext) = MiddlewareCapturingNext();
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        reachedNext().Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private static (McpJwtAuthenticationMiddleware Middleware, Func<bool> ReachedNext) MiddlewareCapturingNext()
    {
        const string secret = "super-secret-key-for-mcp-tests-123456";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = secret
            })
            .Build();

        var reached = false;
        var middleware = new McpJwtAuthenticationMiddleware(
            _ =>
            {
                reached = true;
                return Task.CompletedTask;
            },
            config,
            NullLogger<McpJwtAuthenticationMiddleware>.Instance);
        return (middleware, () => reached);
    }

    private static string CreateJwt(string secret, Guid userId, string email)
    {
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Aud, "mcp"),
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
