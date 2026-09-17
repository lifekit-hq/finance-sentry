namespace FinanceSentry.Modules.Companion.Tests;

using System.Net;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>
/// Outbound wake payload — ids/refs only, no secrets/detail (feature 031, US2, T028) — plus the hook
/// contract: bearer auth from configuration and an <c>Idempotency-Key</c> that survives retries.
/// </summary>
public sealed class WebhookPayloadTests
{
    // Placeholder only — the real token is runtime configuration and never appears in code or tests.
    private const string PlaceholderToken = "unit-test-placeholder-token";

    private sealed record CapturedRequest(Uri? Uri, string? Body, string? Authorization, string? IdempotencyKey);

    private sealed class CapturingHandler(params HttpStatusCode[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses = new(responses.Length == 0 ? [HttpStatusCode.OK] : responses);

        public List<CapturedRequest> Requests { get; } = [];

        public CapturedRequest? Last => Requests.Count == 0 ? null : Requests[^1];

        public string? Body => Last?.Body;

        public Uri? Uri => Last?.Uri;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues(WebhookAgentWakeDispatcher.IdempotencyKeyHeader, out var keys);
            Requests.Add(new CapturedRequest(
                request.RequestUri, body, request.Headers.Authorization?.ToString(), keys?.SingleOrDefault()));
            var status = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return new HttpResponseMessage(status);
        }
    }

    private sealed class FakeHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private static CompanionEvent SampleEvent() => new()
    {
        Kind = CompanionEventKind.ThesisBreak,
        Subject = "MU",
        Severity = "critical",
        Summary = "SUPER-SECRET internal detail that must never leave FS",
        OccurredAt = DateTimeOffset.Parse("2026-07-22T10:00:00Z"),
    };

    [Fact]
    public async Task Configured_url_posts_ids_and_refs_only()
    {
        var handler = new CapturingHandler();
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions { AgentTriggerUrl = "http://agent.local/trigger" }),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);

        var evt = SampleEvent();
        var result = await dispatcher.WakeAsync(evt);

        result.Should().Be(WakeResult.Sent);
        handler.Uri!.ToString().Should().Be("http://agent.local/trigger");
        handler.Body.Should().Contain(evt.Id.ToString())
            .And.Contain("ThesisBreak").And.Contain("MU").And.Contain("critical").And.Contain("occurredAt");
        handler.Body.Should().NotContain("SUPER-SECRET", "the wake carries no full detail or secrets");
    }

    [Fact]
    public async Task Configured_token_is_sent_as_bearer_and_never_in_the_body()
    {
        var handler = new CapturingHandler();
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions
            {
                AgentTriggerUrl = "http://agent.local/trigger",
                AgentTriggerToken = PlaceholderToken,
            }),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);

        var evt = SampleEvent();
        var result = await dispatcher.WakeAsync(evt);

        result.Should().Be(WakeResult.Sent);
        handler.Last!.Authorization.Should().Be($"Bearer {PlaceholderToken}");
        handler.Last.IdempotencyKey.Should().Be(evt.Id.ToString());
        handler.Last.Body.Should().NotContain(PlaceholderToken, "the token travels only in the header");
    }

    [Fact]
    public async Task No_token_sends_no_authorization_header()
    {
        var handler = new CapturingHandler();
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions { AgentTriggerUrl = "http://agent.local/trigger", AgentTriggerToken = " " }),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);

        await dispatcher.WakeAsync(SampleEvent());

        handler.Last!.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task Retry_of_a_failed_wake_carries_the_same_idempotency_key()
    {
        var handler = new CapturingHandler(HttpStatusCode.BadGateway, HttpStatusCode.OK);
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions
            {
                AgentTriggerUrl = "http://agent.local/trigger",
                AgentTriggerToken = PlaceholderToken,
            }),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);
        var evt = SampleEvent();

        var first = await dispatcher.WakeAsync(evt);
        var second = await dispatcher.WakeAsync(evt);

        first.Should().Be(WakeResult.Failed);
        second.Should().Be(WakeResult.Sent);
        handler.Requests.Should().HaveCount(2);
        handler.Requests.Select(r => r.IdempotencyKey).Should().AllBe(evt.Id.ToString(),
            "the receiver dedups the relay's retries on the event id");
        handler.Requests.Select(r => r.Authorization).Should().AllBe($"Bearer {PlaceholderToken}");
    }

    [Fact]
    public async Task Digest_wake_is_authenticated_and_carries_no_event_key()
    {
        var handler = new CapturingHandler();
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions
            {
                AgentTriggerUrl = "http://agent.local/trigger",
                AgentTriggerToken = PlaceholderToken,
            }),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);

        var result = await dispatcher.WakeDigestAsync(Guid.NewGuid(), 3);

        result.Should().Be(WakeResult.Sent);
        handler.Last!.Authorization.Should().Be($"Bearer {PlaceholderToken}");
        handler.Last.IdempotencyKey.Should().BeNull();
        handler.Last.Body.Should().Contain("Digest");
    }

    [Fact]
    public async Task No_url_is_not_configured_and_posts_nothing()
    {
        var handler = new CapturingHandler();
        var dispatcher = new WebhookAgentWakeDispatcher(
            new FakeHttpFactory(handler),
            Options.Create(new CompanionOptions { AgentTriggerUrl = null }),
            NullLogger<WebhookAgentWakeDispatcher>.Instance);

        var result = await dispatcher.WakeAsync(SampleEvent());

        result.Should().Be(WakeResult.NotConfigured);
        handler.Body.Should().BeNull();
    }
}
