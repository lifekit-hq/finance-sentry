using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// Builds the <see cref="RequestContext{TParams}"/> a call-tool filter receives. The SDK insists on
/// a real <see cref="McpServer"/> behind the context, so one is created over a transport that never
/// carries a message — the filters under test read the tool name and the request's services, and
/// nothing is ever written back to the client.
/// </summary>
internal static class TestRequestContext
{
    public static RequestContext<CallToolRequestParams> ForToolCall(string toolName, IServiceProvider services)
    {
        var server = McpServer.Create(
            new SilentTransport(),
            new McpServerOptions(),
            NullLoggerFactory.Instance,
            services);

        return new RequestContext<CallToolRequestParams>(
            server,
            new JsonRpcRequest { Method = RequestMethods.ToolsCall, Id = new RequestId(1) },
            new CallToolRequestParams { Name = toolName })
        {
            Services = services,
        };
    }

    private sealed class SilentTransport : ITransport
    {
        private readonly Channel<JsonRpcMessage> channel = Channel.CreateUnbounded<JsonRpcMessage>();

        public string? SessionId => "test";

        public ChannelReader<JsonRpcMessage> MessageReader => this.channel.Reader;

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            this.channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
