namespace FinanceSentry.Tests.Unit.BankSync.Monobank;

using System.Net;
using System.Net.Http.Headers;
using FinanceSentry.Modules.BankSync.Infrastructure.Monobank;
using FinanceSentry.Tests.Unit.Http;
using FluentAssertions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Xunit;

/// <summary>Monobank's 429 pipeline on a fake clock: no test waits the 60 s the API asks for.</summary>
public class MonobankResilienceTests
{
    private const string ClientInfoJson = """{"clientId":"c1","name":"Name","accounts":[]}""";

    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private static bool IsRetryWait(TimeSpan due) => due != MonobankResilience.AttemptTimeout;

    private static (MonobankHttpClient Client, ScriptedHandler Handler, RecordingTimeProvider Time) Build(
        Func<int, HttpResponseMessage> respond, RecordingTimeProvider? clock = null)
    {
        var time = clock ?? new RecordingTimeProvider();
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage> { TimeProvider = time };
        MonobankResilience.Configure(pipeline, time);

        var handler = new ScriptedHandler(respond);
        var http = new HttpClient(new ResilienceHandler(_ => pipeline.Build()) { InnerHandler = handler })
        {
            BaseAddress = new Uri("https://api.monobank.ua"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return (new MonobankHttpClient(http), handler, time);
    }

    private static HttpResponseMessage RateLimited(Func<HttpResponseMessage, RetryConditionHeaderValue?>? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new TrackedContent("slow down") };
        response.Headers.RetryAfter = retryAfter?.Invoke(response);
        return response;
    }

    private static HttpResponseMessage OkClientInfo() =>
        new(HttpStatusCode.OK) { Content = new StringContent(ClientInfoJson, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task RateLimited_WaitsForRetryAfterThenSucceeds()
    {
        var (client, handler, time) = Build(call => call == 1
            ? RateLimited(_ => new RetryConditionHeaderValue(7 * Second))
            : OkClientInfo());

        var info = await time.RunAsync(client.GetClientInfoAsync("token"), IsRetryWait);

        info.ClientId.Should().Be("c1");
        handler.Calls.Should().Be(2);
        // Retry-After replaces the fixed 60 s sleep.
        time.Waits.Should().Equal(7 * Second);
    }

    [Fact]
    public async Task RateLimited_WithoutRetryAfter_FallsBackTo60ThenTwoMinutes()
    {
        var (client, handler, time) = Build(call => call < 3 ? RateLimited() : OkClientInfo());

        await time.RunAsync(client.GetClientInfoAsync("token"), IsRetryWait);

        handler.Calls.Should().Be(3);
        time.Waits.Should().Equal(60 * Second, 120 * Second);
    }

    [Fact]
    public async Task RateLimited_RetryAfterDate_IsMeasuredOnTheInjectedClock()
    {
        var time = new RecordingTimeProvider();
        var (client, _, _) = Build(call => call == 1
            ? RateLimited(_ => new RetryConditionHeaderValue(time.GetUtcNow() + 30 * Second))
            : OkClientInfo(), time);

        await time.RunAsync(client.GetClientInfoAsync("token"), IsRetryWait);

        time.Waits.Should().Equal(30 * Second);
    }

    [Fact]
    public async Task RateLimited_RetryAfterBeyondTheCap_IsCapped()
    {
        var (client, _, time) = Build(call => call == 1
            ? RateLimited(_ => new RetryConditionHeaderValue(TimeSpan.FromHours(1)))
            : OkClientInfo());

        await time.RunAsync(client.GetClientInfoAsync("token"), IsRetryWait);

        time.Waits.Should().Equal(MonobankResilience.MaxRetryDelay);
    }

    [Fact]
    public async Task RateLimitedThroughout_ThrowsAfterThreeAttemptsAndDisposesEveryResponse()
    {
        var responses = new List<HttpResponseMessage>();
        var (client, handler, time) = Build(_ =>
        {
            var response = RateLimited();
            responses.Add(response);
            return response;
        });

        var act = () => time.RunAsync(client.GetClientInfoAsync("token"), IsRetryWait);

        var ex = (await act.Should().ThrowAsync<MonobankException>()).Which;
        ex.ErrorCode.Should().Be("MONOBANK_RATE_LIMITED");
        ex.StatusCode.Should().Be(429);
        handler.Calls.Should().Be(1 + MonobankResilience.MaxRetries);
        responses.Select(r => ((TrackedContent)r.Content).Disposed).Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task ServerError_IsNotRetriedAndItsResponseIsDisposed()
    {
        HttpResponseMessage? sent = null;
        var (client, handler, _) = Build(_ => sent = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new TrackedContent("boom"),
        });

        var act = () => client.GetClientInfoAsync("token");

        (await act.Should().ThrowAsync<MonobankException>()).Which.ErrorCode.Should().Be("MONOBANK_SERVER_ERROR");
        handler.Calls.Should().Be(1);
        ((TrackedContent)sent!.Content).Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Webhook_RateLimited_ResendsTheSameBody()
    {
        var bodies = new List<string>();
        var (client, handler, time) = Build(call => call == 1 ? RateLimited() : new HttpResponseMessage(HttpStatusCode.OK));
        handler.OnRequest = async request => bodies.Add(await request.Content!.ReadAsStringAsync());

        await time.RunAsync(client.SetWebhookAsync("token", "https://hook.test/mono"), IsRetryWait);

        handler.Calls.Should().Be(2);
        bodies.Should().HaveCount(2).And.AllBeEquivalentTo("""{"webHookUrl":"https://hook.test/mono"}""");
    }

    private sealed class TrackedContent(string text) : StringContent(text)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ScriptedHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Func<HttpRequestMessage, Task>? OnRequest { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (OnRequest is not null)
                await OnRequest(request);
            return respond(Calls);
        }
    }
}
