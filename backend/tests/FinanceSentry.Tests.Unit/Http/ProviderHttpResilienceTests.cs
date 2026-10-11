namespace FinanceSentry.Tests.Unit.Http;

using System.Net;
using System.Net.Http.Headers;
using FinanceSentry.Infrastructure.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>The shared provider pipeline, built the way the modules register it (DI, on a fake clock).</summary>
public class ProviderHttpResilienceTests
{
    private const string ClientName = "provider";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongestBackoff = TimeSpan.FromSeconds(10);

    private static bool IsRetryWait(TimeSpan due) => due < LongestBackoff;

    private static (HttpClient Client, ScriptedHandler Handler, RecordingTimeProvider Time) Build(
        Func<int, HttpResponseMessage> respond, int maxRetries = ProviderHttpResilience.DefaultMaxRetries)
    {
        var time = new RecordingTimeProvider();
        var handler = new ScriptedHandler(respond);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddHttpClient(ClientName, c => c.Timeout = Timeout)
            .AddProviderResilience(Timeout, maxRetries)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return (factory.CreateClient(ClientName), handler, time);
    }

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    [Fact]
    public async Task Get_RetriesATransientStatusOnTheFakeClock()
    {
        var (client, handler, time) = Build(call => Status(call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        using var response = await time.RunAsync(client.GetAsync("https://provider.test/x"), IsRetryWait);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Calls.Should().Be(2);
        time.Waits.Should().ContainSingle("the backoff ran on the injected TimeProvider, not the wall clock");
    }

    [Fact]
    public async Task Get_HonoursRetryAfter()
    {
        var retryAfter = TimeSpan.FromSeconds(7);
        var (client, handler, time) = Build(call =>
        {
            var response = Status(call == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (call == 1)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
            return response;
        });

        using var response = await time.RunAsync(client.GetAsync("https://provider.test/x"), IsRetryWait);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Calls.Should().Be(2);
        time.Waits.Should().Equal(retryAfter);
    }

    [Fact]
    public async Task Get_GivesUpAfterTheConfiguredRetries()
    {
        var (client, handler, time) = Build(_ => Status(HttpStatusCode.BadGateway));

        using var response = await time.RunAsync(client.GetAsync("https://provider.test/x"), IsRetryWait);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        handler.Calls.Should().Be(1 + ProviderHttpResilience.DefaultMaxRetries);
    }

    [Fact]
    public async Task Get_DoesNotRetryAPermanentStatus()
    {
        var (client, handler, _) = Build(_ => Status(HttpStatusCode.NotFound));

        using var response = await client.GetAsync("https://provider.test/x");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Post_IsNeverRetried()
    {
        var (client, handler, _) = Build(_ => Status(HttpStatusCode.ServiceUnavailable));

        using var response = await client.PostAsync("https://provider.test/x", new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ZeroRetries_SendsOnce()
    {
        var (client, handler, _) = Build(_ => Status(HttpStatusCode.ServiceUnavailable), maxRetries: 0);

        using var response = await client.GetAsync("https://provider.test/x");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.Calls.Should().Be(1);
    }

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(++Calls));
    }
}
