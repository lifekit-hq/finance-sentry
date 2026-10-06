namespace FinanceSentry.Modules.Companion.Tests;

using System.Net;
using System.Security.Cryptography;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// The status-code matrix of the Web Push sender (spec 859 FR-007) over a fake <see cref="HttpMessageHandler"/>. The keys
/// are throwaway P-256 pairs made in memory for each test; nothing here touches a real push service.
/// </summary>
public sealed class WebPushSenderTests
{
    private const int AuthBytes = 16;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw exception;
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] PublicKey(ECParameters p) => [0x04, .. p.Q.X!, .. p.Q.Y!];

    private static WebPushOptions Keys()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(includePrivateParameters: true);
        return new WebPushOptions { PublicKey = Base64Url(PublicKey(p)), PrivateKey = Base64Url(p.D!), Subject = "mailto:ops@example.test" };
    }

    private static PushSubscription Subscription()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new PushSubscription
        {
            UserId = Guid.NewGuid(),
            Endpoint = "https://fcm.googleapis.com/fcm/send/abc",
            P256dh = Base64Url(PublicKey(ec.ExportParameters(includePrivateParameters: false))),
            Auth = Base64Url(RandomNumberGenerator.GetBytes(AuthBytes)),
        };
    }

    private static WebPushSender Sender(HttpMessageHandler handler, WebPushOptions? options = null)
        => new(new Factory(handler), Options.Create(options ?? Keys()), NullLogger<WebPushSender>.Instance);

    [Fact]
    public async Task A_created_response_is_Sent_and_the_request_is_VAPID_signed_and_encrypted()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var result = await Sender(handler).SendAsync(Subscription(), "{\"notification\":{\"title\":\"x\"}}");

        result.Outcome.Should().Be(PushSendOutcome.Sent);
        var request = handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.Host.Should().Be("fcm.googleapis.com");
        request.Headers.Authorization!.Scheme.Should().Be("vapid");
        request.Content!.Headers.ContentEncoding.Should().Contain("aes128gcm");
        request.Headers.GetValues("TTL").Should().ContainSingle()
            .Which.Should().Be(((int)PushDeliveryPolicy.TimeToLive.TotalSeconds).ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PushSendOutcome.Gone)]
    [InlineData(HttpStatusCode.Gone, PushSendOutcome.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests, PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.InternalServerError, PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.BadGateway, PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, PushSendOutcome.Transient)]
    [InlineData(HttpStatusCode.BadRequest, PushSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, PushSendOutcome.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, PushSendOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, PushSendOutcome.Unauthorized)]
    public async Task An_error_status_maps_to_its_outcome(HttpStatusCode status, PushSendOutcome expected)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status));

        var result = await Sender(handler).SendAsync(Subscription(), "{}");

        result.Outcome.Should().Be(expected);
        result.StatusCode.Should().Be((int)status);
    }

    [Fact]
    public async Task A_transport_error_is_Transient()
    {
        var sender = Sender(new ThrowingHandler(new HttpRequestException("connection refused")));

        var result = await sender.SendAsync(Subscription(), "{}");

        result.Outcome.Should().Be(PushSendOutcome.Transient);
        result.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task A_timeout_is_Transient()
    {
        var sender = Sender(new ThrowingHandler(new TaskCanceledException("timed out")));

        (await sender.SendAsync(Subscription(), "{}")).Outcome.Should().Be(PushSendOutcome.Transient);
    }

    [Fact]
    public async Task A_malformed_subscription_key_is_Rejected_instead_of_throwing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var subscription = Subscription();
        subscription.P256dh = "not-a-valid-key!";

        var result = await Sender(handler).SendAsync(subscription, "{}");

        result.Outcome.Should().Be(PushSendOutcome.Rejected);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_VAPID_keys_nothing_is_sent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var result = await Sender(handler, new WebPushOptions()).SendAsync(Subscription(), "{}");

        result.Outcome.Should().Be(PushSendOutcome.Unauthorized);
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public void Classify_covers_the_whole_matrix()
    {
        WebPushSender.Classify(404).Should().Be(PushSendOutcome.Gone);
        WebPushSender.Classify(410).Should().Be(PushSendOutcome.Gone);
        WebPushSender.Classify(429).Should().Be(PushSendOutcome.Transient);
        WebPushSender.Classify(503).Should().Be(PushSendOutcome.Transient);
        WebPushSender.Classify(400).Should().Be(PushSendOutcome.Rejected);
        WebPushSender.Classify(413).Should().Be(PushSendOutcome.Rejected);
        WebPushSender.Classify(401).Should().Be(PushSendOutcome.Unauthorized);
        WebPushSender.Classify(403).Should().Be(PushSendOutcome.Unauthorized);
    }
}
