namespace FinanceSentry.Modules.Agent.Tests;

using System.Net;
using System.Text;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class AnthropicLlmClientTests
{
    [Fact]
    public async Task StreamAsync_HandlesMultiLineData_Comments_AndIdRetryFields()
    {
        var sse = string.Concat(
            ": ping comment\n\n",
            "event: content_block_start\n",
            "id: 1\n",
            "retry: 2000\n",
            "data: {\"type\":\"content_block_start\",\"index\":0,\n",
            "data: \"content_block\":{\"type\":\"text\"}}\n\n",
            "event: content_block_delta\n",
            "data: {\"type\":\"content_block_delta\",\"index\":0,\n",
            "data: \"delta\":{\"type\":\"text_delta\",\"text\":\"Hi there\"}}\n\n",
            "event: message_delta\n",
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n",
            "event: message_stop\n",
            "data: {\"type\":\"message_stop\"}\n\n");

        var chunks = await DrainAsync(sse);

        chunks.OfType<LlmTextDelta>().Select(c => c.Text).Should().Equal("Hi there");
        var done = chunks.OfType<LlmMessageCompleted>().Should().ContainSingle().Subject;
        done.StopReason.Should().Be("end_turn");
    }

    private static async Task<List<LlmStreamChunk>> DrainAsync(string sse)
    {
        var http = new HttpClient(new StubHandler(sse)) { BaseAddress = new Uri("http://anthropic.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AnthropicLlmClient.HttpClientName)).Returns(http);
        var options = new AgentOptions();
        options.Anthropic.ApiKey = "sk-ant-test";
        var sut = new AnthropicLlmClient(
            factory.Object, Options.Create(options), NullLogger<AnthropicLlmClient>.Instance);

        var chunks = new List<LlmStreamChunk>();
        await foreach (var chunk in sut.StreamAsync("sys", [LlmMessage.UserText("hi")], [], CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            });
    }
}
