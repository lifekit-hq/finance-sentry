namespace FinanceSentry.Tests.Unit.Auth;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.API.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

/// <summary>
/// The shared access-token validation: <see cref="AccessTokenValidationParameters"/> (used by the API host, the MCP
/// HTTP host and the stdio MCP host's stored-credential check) and the startup requirement for
/// <c>Jwt:Secret</c> in the hosts that register <see cref="AccessTokenAuthenticationExtensions.AddAccessTokenAuthentication"/>.
/// </summary>
public class AccessTokenValidationTests
{
    // 64+ bytes so an HS512 token can be signed with the same key.
    private const string Secret = "access-token-validation-tests-secret-0123456789-0123456789-0123456789";

    [Fact]
    public void Create_PinsHs256_AndTheAudienceLifetimeAndClockSkewRules()
    {
        var parameters = AccessTokenValidationParameters.Create(Secret, AuthAudiences.Mcp);

        parameters.ValidAlgorithms.Should().Equal(SecurityAlgorithms.HmacSha256);
        parameters.ValidateIssuerSigningKey.Should().BeTrue();
        parameters.ValidateIssuer.Should().BeFalse();
        parameters.ValidateAudience.Should().BeTrue();
        parameters.ValidAudience.Should().Be(AuthAudiences.Mcp);
        parameters.ValidateLifetime.Should().BeTrue();
        parameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
        parameters.IssuerSigningKey.Should().BeOfType<SymmetricSecurityKey>()
            .Which.Key.Should().Equal(Encoding.UTF8.GetBytes(Secret));
    }

    [Theory]
    [InlineData(null, "app")]
    [InlineData("", "app")]
    [InlineData("  ", "app")]
    [InlineData(Secret, "")]
    public void Create_RejectsABlankSecretOrAudience(string? secret, string audience)
    {
        var act = () => AccessTokenValidationParameters.Create(secret!, audience);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_AcceptsAnHs256Token_AndRejectsAnHs512OneSignedWithTheSameKey()
    {
        var parameters = AccessTokenValidationParameters.Create(Secret, AuthAudiences.App);
        var handler = new JwtSecurityTokenHandler();

        var accepted = () => handler.ValidateToken(Token(SecurityAlgorithms.HmacSha256), parameters, out _);
        var rejected = () => handler.ValidateToken(Token(SecurityAlgorithms.HmacSha512), parameters, out _);

        accepted.Should().NotThrow();
        rejected.Should().Throw<SecurityTokenException>();

        // The algorithm pin is what rejects it: the same token passes once the pin is lifted.
        var unpinned = parameters.Clone();
        unpinned.ValidAlgorithms = null;
        var acceptedUnpinned = () => handler.ValidateToken(Token(SecurityAlgorithms.HmacSha512), unpinned, out _);
        acceptedUnpinned.Should().NotThrow();
    }

    [Fact]
    public void Validator_FailsWithAClearMessage_WhenTheSecretIsMissingOrBlank()
    {
        var validator = new JwtOptionsValidator();

        validator.Validate(null, new JwtOptions { Secret = Secret }).Succeeded.Should().BeTrue();
        foreach (var secret in new string?[] { null, "", "   " })
        {
            var result = validator.Validate(null, new JwtOptions { Secret = secret });

            result.Failed.Should().BeTrue();
            result.FailureMessage.Should().Contain("Jwt:Secret is not configured");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Host_RefusesToStart_WhenTheSecretIsMissingOrEmpty(string? secret)
    {
        using var host = BuildHost(secret);

        var start = () => host.StartAsync();

        (await start.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain("Jwt:Secret is not configured");
    }

    [Fact]
    public async Task Host_Starts_AndConfiguresJwtBearerWithTheSharedParameters_WhenTheSecretIsSet()
    {
        using var host = BuildHost(Secret);
        await host.StartAsync();

        var bearer = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.TokenValidationParameters.ValidAlgorithms.Should().Equal(SecurityAlgorithms.HmacSha256);
        bearer.TokenValidationParameters.ValidAudience.Should().Be(AuthAudiences.App);
        await host.StopAsync();
    }

    private static IHost BuildHost(string? secret)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = secret });
        builder.Services.AddAccessTokenAuthentication(AuthAudiences.App);
        return builder.Build();
    }

    private static string Token(string algorithm) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            audience: AuthAudiences.App,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), algorithm)));
}
