using System.Net;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceSentry.Tests.Unit.BrokerageSync.Inzhur;

public class InzhurApiClientTests
{
    private readonly FakeInzhurHandler _handler = new();

    private InzhurApiClient Client() => new(new HttpClient(_handler), Options.Create(new InzhurOptions()), NullLogger<InzhurApiClient>.Instance);

    [Fact]
    public async Task Refresh_posts_with_the_stored_cookie_and_keeps_a_rotated_one()
    {
        _handler.Then(_ =>
        {
            var response = InzhurFakes.Json(HttpStatusCode.OK, new { accessToken = "fresh-token" });
            response.Headers.Add("Set-Cookie", $"{InzhurFakes.RefreshCookieName}=fake-refresh-2; Domain={InzhurFakes.AuthHost}; Path=/; Secure; HttpOnly");
            return response;
        });

        var refreshed = await Client().RefreshAsync(InzhurFakes.Session());

        var request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri.AbsoluteUri.Should().Be("https://api.inzhur.reit/auth/api/v1/auth/refresh");
        request.Cookie.Should().Be($"{InzhurFakes.RefreshCookieName}=fake-refresh-1");
        refreshed.AccessToken.Should().Be("fresh-token");
        refreshed.Cookies.Should().ContainSingle(c => c.Name == InzhurFakes.RefreshCookieName).Which.Value.Should().Be("fake-refresh-2");
    }

    [Fact]
    public async Task A_rotated_cookie_under_another_path_supersedes_the_pasted_one()
    {
        _handler
            .Then(_ =>
            {
                var response = InzhurFakes.Json(HttpStatusCode.OK, new { accessToken = "fresh-token" });
                response.Headers.Add("Set-Cookie", $"{InzhurFakes.RefreshCookieName}=fake-refresh-2; Path=/auth; Secure; HttpOnly");
                response.Headers.Add("Set-Cookie", "other=fake-other; Path=/; Secure");
                return response;
            })
            .Then(InzhurFakes.Json(HttpStatusCode.OK, new { accessToken = "next-token" }));
        var pasted = InzhurSession.FromRefreshCookie(InzhurFakes.RefreshCookieName, "fake-pasted", new Uri(new InzhurOptions().AuthBaseUrl));

        var refreshed = await Client().RefreshAsync(pasted);
        await Client().RefreshAsync(refreshed);

        refreshed.Cookies.Where(c => c.Name == InzhurFakes.RefreshCookieName).Should().ContainSingle().Which.Value.Should().Be("fake-refresh-2");
        _handler.Requests[0].Cookie.Should().Be($"{InzhurFakes.RefreshCookieName}=fake-pasted");
        _handler.Requests[1].Cookie.Should().Contain($"{InzhurFakes.RefreshCookieName}=fake-refresh-2").And.NotContain("fake-pasted");
    }

    [Fact]
    public async Task Portfolio_is_two_GETs_with_the_bearer_token()
    {
        _handler
            .Then(InzhurFakes.Json(HttpStatusCode.OK, InzhurFakes.UserAssets(InzhurFakes.Fund("Fund A", 1m, 100m, 90m))))
            .Then(InzhurFakes.Json(HttpStatusCode.OK, InzhurFakes.BrokerAccount(10m, 0m)));

        var portfolio = await Client().GetPortfolioAsync(InzhurFakes.Session());

        _handler.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get && r.Authorization == $"Bearer {InzhurFakes.AccessToken}");
        _handler.Requests.Select(r => r.Uri.AbsolutePath).Should().Equal("/core/api/v1/user-assets", "/core/api/v1/users/broker-account");
        portfolio.Assets.Should().ContainSingle(a => a.Name == "Fund A");
        portfolio.BrokerAccount!.AvailableBalanceUah.Should().Be(10m);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "anything", InzhurFailureKind.ReauthRequired)]
    [InlineData(HttpStatusCode.BadRequest, "Invalid or expired refresh token", InzhurFailureKind.ReauthRequired)]
    [InlineData(HttpStatusCode.BadRequest, "Missing refresh token", InzhurFailureKind.ReauthRequired)]
    [InlineData(HttpStatusCode.TooManyRequests, "slow down", InzhurFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadGateway, "upstream", InzhurFailureKind.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, "Validation failed", InzhurFailureKind.Unexpected)]
    public async Task Refresh_failures_are_classified(HttpStatusCode status, string message, InzhurFailureKind expected)
    {
        _handler.Then(InzhurFakes.Error(status, message));

        var act = () => Client().RefreshAsync(InzhurFakes.Session());

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(expected);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable_and_transient()
    {
        _handler.Then(_ => throw new HttpRequestException("connection refused"));

        var act = () => Client().GetPortfolioAsync(InzhurFakes.Session());

        var ex = (await act.Should().ThrowAsync<InzhurApiException>()).Which;
        ex.Kind.Should().Be(InzhurFailureKind.Unavailable);
        ex.IsTransient.Should().BeTrue();
    }

    [Fact]
    public async Task A_refresh_without_a_token_is_unexpected()
    {
        _handler.Then(InzhurFakes.Json(HttpStatusCode.OK, new { }));

        var act = () => Client().RefreshAsync(InzhurFakes.Session());

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.Unexpected);
    }

    [Theory]
    [InlineData("{}", "{\"availableBalanceUAH\":1}")]
    [InlineData("{\"assets\":null}", "{\"availableBalanceUAH\":1}")]
    [InlineData("{\"assets\":[]}", "null")]
    [InlineData("{\"assets\":[]}", "{}")]
    public async Task A_portfolio_without_its_containers_is_unexpected_not_empty(string assetsBody, string accountBody)
    {
        _handler
            .Then(Raw(assetsBody))
            .Then(Raw(accountBody));

        var act = () => Client().GetPortfolioAsync(InzhurFakes.Session());

        (await act.Should().ThrowAsync<InzhurApiException>()).Which.Kind.Should().Be(InzhurFailureKind.Unexpected);
    }

    private static HttpResponseMessage Raw(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
