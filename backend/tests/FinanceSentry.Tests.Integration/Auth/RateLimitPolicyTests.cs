namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using FinanceSentry.API.Conventions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// The rate-limit policies are attached to the endpoints they were written for, and partition on the
/// client address the forwarded-headers middleware resolved.
/// </summary>
public class RateLimitPolicyTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const int AnonymousPermitPerMinute = 3;
    private const int AuthSessionPermitPerMinute = 7;
    private const int HealthProbeBurst = 25;
    private const int PastTheCredentialDefault = 20;
    private const string RefreshPath = "/api/v1/auth/refresh";
    private const string HealthPath = "/api/v1/health";

    private static readonly string[] SessionPaths =
        ["/api/v1/auth/me", "/api/v1/auth/methods", "/api/v1/auth/oidc/start"];

    [Fact]
    public async Task AnonymousAuthEndpoint_AboveItsPermit_Returns429()
    {
        await using var scoped = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(RateLimitPartitions.AnonymousPermitKey, AnonymousPermitPerMinute.ToString()));
        using var client = scoped.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= AnonymousPermitPerMinute; i++)
        {
            statuses.Add((await client.PostAsync(RefreshPath, content: null)).StatusCode);
        }

        statuses.Take(AnonymousPermitPerMinute).Should().NotContain(HttpStatusCode.TooManyRequests);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SessionEndpoints_ShareTheirOwnHigherBudget_NotTheCredentialOne()
    {
        await using var scoped = factory.WithWebHostBuilder(builder => builder
            .UseSetting(RateLimitPartitions.AnonymousPermitKey, AnonymousPermitPerMinute.ToString())
            .UseSetting(RateLimitPartitions.AuthSessionPermitKey, AuthSessionPermitPerMinute.ToString()));
        using var client = scoped.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= AuthSessionPermitPerMinute; i++)
        {
            statuses.Add((await client.GetAsync(SessionPaths[i % SessionPaths.Length])).StatusCode);
        }

        // Past the 3/min credential permit, still admitted: the session reads are on the 7/min budget.
        statuses.Take(AuthSessionPermitPerMinute).Should().NotContain(HttpStatusCode.TooManyRequests);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);

        // And they never spent the credential budget.
        (await client.PostAsync(RefreshPath, content: null)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task SessionEndpoints_AtTheDefaultPermit_AdmitMoreThanTheCredentialDefault()
    {
        using var client = factory.CreateClient();

        // The credential default is 10/min; the session default is 60/min.
        for (var i = 0; i < PastTheCredentialDefault; i++)
        {
            (await client.GetAsync(SessionPaths[1])).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public async Task HealthProbe_IsExemptFromRateLimiting()
    {
        using var client = factory.CreateClient();

        for (var i = 0; i < HealthProbeBurst; i++)
        {
            (await client.GetAsync(HealthPath)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }
    }

    [Fact]
    public void Partitions_KeyOnTheResolvedClientAddress()
    {
        var first = RateLimitPartitions.Anonymous(ContextFrom("100.101.102.103"));
        var same = RateLimitPartitions.Anonymous(ContextFrom("100.101.102.103"));
        var other = RateLimitPartitions.Anonymous(ContextFrom("100.101.102.104"));

        first.PartitionKey.Should().Be(same.PartitionKey);
        first.PartitionKey.Should().NotBe(other.PartitionKey);
    }

    [Fact]
    public void AuthenticatedPartition_WithoutAUser_FallsBackToTheClientAddress()
    {
        var partition = RateLimitPartitions.Authenticated(ContextFrom("100.101.102.103"));

        partition.PartitionKey.Should().Be("ip:100.101.102.103");
    }

    private static DefaultHttpContext ContextFrom(string address)
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }
}
