namespace FinanceSentry.Tests.Unit.Auth;

using FinanceSentry.Modules.Auth.API.Authentication;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

/// <summary>
/// Session cookie naming (<see cref="AuthCookies"/>): <c>__Host-</c> names in production with a read fallback
/// to, and a delete of, the legacy unprefixed names; unprefixed names elsewhere.
/// </summary>
public class AuthCookiesTests
{
    private static readonly DateTimeOffset Expires = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public void Write_InProduction_UsesTheHostPrefix_AndDeletesTheLegacyName()
    {
        var context = new DefaultHttpContext();

        AuthCookies.Write(context.Response, AuthCookies.AccessToken, "token", Expires, secure: true);

        var setCookies = context.Response.Headers.SetCookie.ToArray();
        setCookies.Should().Contain(c => c!.StartsWith("__Host-fs_access_token=token;", StringComparison.Ordinal)
                                       && c.Contains("secure", StringComparison.OrdinalIgnoreCase)
                                       && c.Contains("path=/", StringComparison.OrdinalIgnoreCase)
                                       && !c.Contains("domain=", StringComparison.OrdinalIgnoreCase));
        setCookies.Should().Contain(c => c!.StartsWith("fs_access_token=;", StringComparison.Ordinal)
                                       && c.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_OutsideProduction_UsesTheUnprefixedName_Only()
    {
        var context = new DefaultHttpContext();

        AuthCookies.Write(context.Response, AuthCookies.RefreshToken, "token", Expires, secure: false);

        context.Response.Headers.SetCookie.ToArray().Should().ContainSingle()
            .Which.Should().StartWith("fs_refresh_token=token;");
    }

    [Theory]
    [InlineData("__Host-fs_access_token=new; fs_access_token=old", "new")]
    [InlineData("fs_access_token=old", "old")]
    [InlineData("", null)]
    public void Read_InProduction_PrefersTheHostName_AndFallsBackToTheLegacyName(string cookieHeader, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookieHeader;

        AuthCookies.Read(context.Request.Cookies, AuthCookies.AccessToken, secure: true).Should().Be(expected);
    }

    [Fact]
    public void Read_OutsideProduction_IgnoresTheHostName()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "__Host-fs_access_token=new";

        AuthCookies.Read(context.Request.Cookies, AuthCookies.AccessToken, secure: false).Should().BeNull();
    }

    [Fact]
    public void Delete_InProduction_DeletesBothNames()
    {
        var context = new DefaultHttpContext();

        AuthCookies.Delete(context.Response, AuthCookies.RefreshToken, secure: true);

        context.Response.Headers.SetCookie.ToArray().Should().HaveCount(2)
            .And.Contain(c => c!.StartsWith("__Host-fs_refresh_token=;", StringComparison.Ordinal))
            .And.Contain(c => c!.StartsWith("fs_refresh_token=;", StringComparison.Ordinal));
    }
}
