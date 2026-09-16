namespace FinanceSentry.Modules.Agent.Tests;

using System.Net;
using System.Text;
using FinanceSentry.API.Adapters;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

/// <summary>
/// #635: the Ledger read is only ever the agent's answer to the prompt. A failed turn or the chat
/// widget's silence greeting must come back as "no narrative", never as text to persist.
/// </summary>
public sealed class LedgerNarratorAdapterTests
{
    private const string Prompt = "Write a concise read on CBRS for the portfolio owner.";

    [Fact]
    public async Task NarrateAsync_ReturnsAnswer_WhenAgentReplies()
    {
        var narrator = CreateSut(string.Concat(
            "data: {\"choices\":[{\"delta\":{\"content\":\"CBRS is a small position.\"}}]}\n",
            "data: [DONE]\n"));

        var narrative = await narrator.NarrateAsync(Prompt);

        narrative.Should().Be("CBRS is a small position.");
    }

    [Fact]
    public async Task NarrateAsync_ReturnsNull_WhenGatewayStreamReportsError()
    {
        // The production CBRS stream: every model failed auth, so the gateway sent an error chunk.
        var narrator = CreateSut(string.Concat(
            "data: {\"error\":{\"message\":\"internal error\",\"type\":\"api_error\"}}\n",
            "data: [DONE]\n"));

        var narrative = await narrator.NarrateAsync(Prompt);

        narrative.Should().BeNull();
    }

    [Theory]
    [InlineData("data: [DONE]\n")]
    [InlineData("data: {\"choices\":[{\"delta\":{\"content\":\"NO_REPLY\"}}]}\ndata: [DONE]\n")]
    public async Task NarrateAsync_ReturnsNull_WhenAgentStaysSilent(string sse)
    {
        var narrator = CreateSut(sse);

        var narrative = await narrator.NarrateAsync(Prompt);

        narrative.Should().BeNull("the chat greeting substituted for silence is not a read");
    }

    private static LedgerNarratorAdapter CreateSut(string sse)
    {
        var options = Options.Create(new AgentOptions { OpenClaw = { BaseUrl = "http://gateway:18789" } });
        var http = new HttpClient(new StubHandler(sse)) { BaseAddress = new Uri("http://gateway:18789/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(OpenClawAgentConversationService.HttpClientName)).Returns(http);

        var conversation = new OpenClawAgentConversationService(
            factory.Object, options, NullLogger<OpenClawAgentConversationService>.Instance);
        return new LedgerNarratorAdapter(conversation, options, new ServiceCollection().BuildServiceProvider());
    }

    private sealed class StubHandler(string sse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
            });
    }
}
