using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

public class InzhurSessionTests
{
    [Fact]
    public void Serialized_session_round_trips()
    {
        var session = InzhurFakes.Session();

        var restored = InzhurSession.Deserialize(session.Serialize());

        restored.AccessToken.Should().Be(session.AccessToken);
        restored.Cookies.Should().BeEquivalentTo(session.Cookies);
    }

    [Fact]
    public void A_pasted_refresh_cookie_is_sent_to_the_auth_host_only()
    {
        var session = InzhurSession.FromRefreshCookie(InzhurFakes.RefreshCookieName, "fake-pasted", new Uri("https://api.inzhur.reit/auth"));

        var container = session.ToContainer();

        session.AccessToken.Should().BeEmpty("the first refresh fetches one");
        container.GetCookieHeader(new Uri("https://api.inzhur.reit/auth/api/v1/auth/refresh")).Should().Be($"{InzhurFakes.RefreshCookieName}=fake-pasted");
        container.GetCookieHeader(new Uri("http://api.inzhur.reit/auth/api/v1/auth/refresh")).Should().BeEmpty("the cookie is Secure");
        container.GetCookieHeader(new Uri("https://www.inzhur.reit/")).Should().BeEmpty();
        session.ToString().Should().NotContain("fake-pasted");
    }

    [Fact]
    public void Container_round_trip_keeps_live_cookies_and_drops_expired_ones()
    {
        var session = new InzhurSession(InzhurFakes.AccessToken,
        [
            new InzhurCookie("live", "v1", InzhurFakes.AuthHost, "/", DateTime.UtcNow.AddDays(30), true, true),
            new InzhurCookie("gone", "v2", InzhurFakes.AuthHost, "/", DateTime.UtcNow.AddDays(-1), true, true),
        ]);

        var restored = InzhurSession.FromContainer("next-token", session.ToContainer());

        restored.AccessToken.Should().Be("next-token");
        restored.Cookies.Should().ContainSingle(c => c.Name == "live" && c.Value == "v1" && c.HttpOnly);
    }

    [Fact]
    public void A_session_and_its_cookies_never_print_a_secret()
    {
        var session = InzhurFakes.Session(refreshValue: "fake-refresh-secret");

        session.ToString().Should().NotContain(InzhurFakes.AccessToken).And.NotContain("fake-refresh-secret");
        session.Cookies[0].ToString().Should().NotContain("fake-refresh-secret");
    }

    [Fact]
    public void Container_sends_the_jar_to_the_auth_host()
    {
        var header = InzhurFakes.Session().ToContainer().GetCookieHeader(new Uri("https://api.inzhur.reit/auth/api/v1/auth/refresh"));

        header.Should().Be($"{InzhurFakes.RefreshCookieName}=fake-refresh-1");
    }
}
