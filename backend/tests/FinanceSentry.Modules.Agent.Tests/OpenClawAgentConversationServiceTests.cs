namespace FinanceSentry.Modules.Agent.Tests;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class OpenClawAgentConversationServiceTests
{
    private static readonly IServiceProvider EmptyScope = new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Provider_Resolves_OpenClaw_WhenOnlyGatewayConfigured()
    {
        var options = new AgentOptions();
        options.OpenClaw.BaseUrl = "http://gw:18789";

        options.Provider.Should().Be(AgentProvider.OpenClaw);
        options.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Provider_PrefersAnthropic_WhenBothConfigured()
    {
        var options = new AgentOptions { Anthropic = { ApiKey = "sk-ant" } };
        options.OpenClaw.BaseUrl = "http://gw:18789";

        options.Provider.Should().Be(AgentProvider.Anthropic);
    }

    [Fact]
    public void ProviderOverride_ForcesOpenClaw_EvenWithAnthropicKey()
    {
        var options = new AgentOptions { Anthropic = { ApiKey = "sk-ant" }, ProviderOverride = "openclaw" };
        options.OpenClaw.BaseUrl = "http://gw:18789";

        options.Provider.Should().Be(AgentProvider.OpenClaw);
    }

    [Fact]
    public async Task RunAsync_StreamsDeltas_AndAssemblesCompletion()
    {
        var sse = string.Concat(
            "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"Your net worth \"}}]}\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"is $1.8M.\"}}]}\n",
            "data: [DONE]\n");
        var handler = new StubHandler(_ => Sse(sse));
        var service = CreateSut(handler);

        var events = await DrainAsync(service, "what's my net worth?");

        events.OfType<AgentTextEvent>().Select(e => e.Delta)
            .Should().ContainInOrder("Your net worth ", "is $1.8M.");
        events.OfType<AgentToolEvent>().Should().BeEmpty("OpenClaw runs its own tools server-side");
        var completion = events.OfType<AgentCompletionEvent>().Should().ContainSingle().Subject;
        completion.FinalText.Should().Be("Your net worth is $1.8M.");
        completion.IsSilenceFallback.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_SwapsBareNoReplySentinel_ForGreeting()
    {
        // Ledger stays silent on trivial openers ("hi") by emitting NO_REPLY — the browser widget must never
        // render the raw sentinel.
        var sse = string.Concat(
            "data: {\"choices\":[{\"delta\":{\"content\":\"NO\"}}]}\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"_REPLY\"}}]}\n",
            "data: [DONE]\n");
        var service = CreateSut(new StubHandler(_ => Sse(sse)));

        var events = await DrainAsync(service, "hi");

        var text = string.Concat(events.OfType<AgentTextEvent>().Select(e => e.Delta));
        text.Should().NotContain("NO_REPLY");
        text.Should().StartWith("Hey Denys");
        var completion = events.OfType<AgentCompletionEvent>().Should().ContainSingle().Subject;
        completion.FinalText.Should().Be(text);
        completion.IsSilenceFallback.Should().BeTrue("the greeting is a stand-in, not an answer");
    }

    [Fact]
    public async Task RunAsync_StripsLeadingNoReply_WhenAgentSelfCorrects()
    {
        var sse = string.Concat(
            "data: {\"choices\":[{\"delta\":{\"content\":\"NO_REPLY\"}}]}\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\" Morning, Denys.\"}}]}\n",
            "data: [DONE]\n");
        var service = CreateSut(new StubHandler(_ => Sse(sse)));

        var events = await DrainAsync(service, "hi");

        var text = string.Concat(events.OfType<AgentTextEvent>().Select(e => e.Delta));
        text.Should().Be("Morning, Denys.");
    }

    [Fact]
    public async Task RunAsync_PreservesRealReply_ContainingNoReplySubstring()
    {
        // A genuine reply that merely mentions the sentinel mid-sentence must pass through untouched.
        var sse = string.Concat(
            "data: {\"choices\":[{\"delta\":{\"content\":\"You'll see \"}}]}\n",
            "data: {\"choices\":[{\"delta\":{\"content\":\"NO_REPLY when I stay quiet.\"}}]}\n",
            "data: [DONE]\n");
        var service = CreateSut(new StubHandler(_ => Sse(sse)));

        var events = await DrainAsync(service, "what does NO_REPLY mean?");

        var completion = events.OfType<AgentCompletionEvent>().Should().ContainSingle().Subject;
        completion.FinalText.Should().Be("You'll see NO_REPLY when I stay quiet.");
    }

    [Fact]
    public async Task RunAsync_RoutesToLedgerAgent_WithBearer_AndFullHistory()
    {
        var handler = new StubHandler(_ => Sse("data: [DONE]\n"));
        var service = CreateSut(handler, token: "gw-secret", model: "openclaw/finance");

        await DrainAsync(service, "latest question", history:
        [
            new LlmMessage("user", [new LlmTextBlock("earlier question")]),
            new LlmMessage("assistant", [new LlmTextBlock("earlier answer")]),
        ]);

        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/chat/completions");
        handler.LastRequest.Headers.Authorization!.ToString().Should().Be("Bearer gw-secret");

        var body = JsonNode.Parse(handler.LastBody!)!;
        body["model"]!.GetValue<string>().Should().Be("openclaw/finance");
        body["stream"]!.GetValue<bool>().Should().BeTrue();
        var messages = body["messages"]!.AsArray();
        messages.Should().HaveCount(3, "full FS history is replayed each turn (independent thread)");
        messages[2]!["content"]!.GetValue<string>().Should().Be("latest question");
        // No system message and no OpenAI 'user' session key — the Ledger agent owns persona; FS owns memory.
        body["system"].Should().BeNull();
        body["user"].Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_MapsHttpFailure_ToLlmUnavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream down"),
        });
        var service = CreateSut(handler);

        var events = await DrainAsync(service, "hello");

        var error = events.OfType<AgentErrorEvent>().Should().ContainSingle().Subject;
        error.Code.Should().Be("llm_unavailable");
        events.OfType<AgentCompletionEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MapsInBandStreamError_ToLlmUnavailable()
    {
        // #635: once the 200 headers are out, the gateway reports a failed turn (every model failed auth)
        // as an `error` chunk then [DONE]. It must surface as an error, not as silence and a greeting.
        var sse = string.Concat(
            "data: {\"error\":{\"message\":\"internal error\",\"type\":\"api_error\"}}\n",
            "data: [DONE]\n");
        var service = CreateSut(new StubHandler(_ => Sse(sse)));

        var events = await DrainAsync(service, "Write a concise read on CBRS");

        var error = events.OfType<AgentErrorEvent>().Should().ContainSingle().Subject;
        error.Code.Should().Be("llm_unavailable");
        events.OfType<AgentTextEvent>().Should().BeEmpty();
        events.OfType<AgentCompletionEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_MapsStreamErrorAfterPartialText_ToLlmUnavailable()
    {
        var sse = string.Concat(
            "data: {\"choices\":[{\"delta\":{\"content\":\"CBRS is \"}}]}\n",
            "data: {\"error\":{\"message\":\"internal error\",\"type\":\"api_error\"}}\n",
            "data: [DONE]\n");
        var service = CreateSut(new StubHandler(_ => Sse(sse)));

        var events = await DrainAsync(service, "Write a concise read on CBRS");

        events.OfType<AgentErrorEvent>().Should().ContainSingle();
        events.OfType<AgentCompletionEvent>().Should().BeEmpty("a truncated answer is not a completed one");
    }

    private static OpenClawAgentConversationService CreateSut(
        StubHandler handler, string? token = null, string model = "openclaw/finance")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://gateway:18789/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OpenClawAgentConversationService.HttpClientName)).Returns(http);

        var options = Options.Create(new AgentOptions
        {
            OpenClaw = { BaseUrl = "http://gateway:18789", Token = token, Model = model },
        });

        return new OpenClawAgentConversationService(
            factory.Object, options, NullLogger<OpenClawAgentConversationService>.Instance);
    }

    private static async Task<List<AgentStreamEvent>> DrainAsync(
        OpenClawAgentConversationService service, string message, IReadOnlyList<LlmMessage>? history = null)
    {
        var messages = new List<LlmMessage>(history ?? []) { LlmMessage.UserText(message) };
        var events = new List<AgentStreamEvent>();
        await foreach (var evt in service.RunAsync(messages, EmptyScope, CancellationToken.None))
        {
            events.Add(evt);
        }

        return events;
    }

    private static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
