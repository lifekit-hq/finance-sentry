namespace FinanceSentry.Modules.Agent.Tests;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Agent.Application.Services;
using FinanceSentry.Modules.Agent.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

public sealed class AnthropicMemoLanguageModelTests
{
    private const string Reply = """
        {"model":"claude-sonnet-5","content":[{"type":"text","text":"{\"verdict\":"},{"type":"text","text":"\"Buy\"}"}],
         "usage":{"input_tokens":1234,"output_tokens":56}}
        """;

    [Fact]
    public async Task CompleteAsync_SendsOneNonStreamingTurn_AndReturnsTheTextAndTheUsage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Reply);

        var reply = await Create(handler, "sk-ant-test").CompleteAsync(new MemoModelRequest("sys", "user text", 900, "claude-test"));

        reply.Should().Be(new MemoModelReply("{\"verdict\":\"Buy\"}", "claude-sonnet-5", 1234, 56));
        var body = JsonNode.Parse(handler.LastBody!)!;
        body["model"]!.GetValue<string>().Should().Be("claude-test");
        body["max_tokens"]!.GetValue<int>().Should().Be(900);
        body["system"]!.GetValue<string>().Should().Be("sys");
        body["stream"].Should().BeNull("the memo call is a single non-streaming turn");
        body["tools"].Should().BeNull();
        body["messages"]!.AsArray().Single()!["content"]!.GetValue<string>().Should().Be("user text");
        handler.ApiKey.Should().Be("sk-ant-test");
    }

    [Fact]
    public async Task CompleteAsync_FallsBackToTheAgentModel_WhenTheRequestNamesNone()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Reply);

        await Create(handler, "sk-ant-test").CompleteAsync(new MemoModelRequest("sys", "u", 10));

        JsonNode.Parse(handler.LastBody!)!["model"]!.GetValue<string>().Should().Be(new AgentOptions().ModelId);
    }

    [Fact]
    public async Task CompleteAsync_ThrowsOnAProviderError()
    {
        var run = () => Create(new StubHandler(HttpStatusCode.TooManyRequests, "{}"), "sk-ant-test")
            .CompleteAsync(new MemoModelRequest("sys", "u", 10));

        await run.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Keyless_IsNotConfigured_AndMakesNoHttpCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Reply);
        var sut = Create(handler, apiKey: null);

        sut.IsConfigured.Should().BeFalse();
        var run = () => sut.CompleteAsync(new MemoModelRequest("sys", "u", 10));
        await run.Should().ThrowAsync<AgentNotConfiguredException>();
        handler.LastBody.Should().BeNull();
    }

    private static AnthropicMemoLanguageModel Create(StubHandler handler, string? apiKey)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://anthropic.test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AnthropicLlmClient.HttpClientName)).Returns(http);
        var options = new AgentOptions();
        options.Anthropic.ApiKey = apiKey;
        return new AnthropicMemoLanguageModel(factory.Object, Options.Create(options), NullLogger<AnthropicMemoLanguageModel>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public string? ApiKey { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            ApiKey = request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
