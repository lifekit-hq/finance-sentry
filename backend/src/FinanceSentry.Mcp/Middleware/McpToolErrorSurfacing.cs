using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace FinanceSentry.Mcp.Middleware;

/// <summary>
/// One call-tool filter for every tool in the host: a tool that throws is logged with its full
/// exception and answered with the real reason instead of the SDK's blanket
/// <c>"An error occurred invoking '&lt;tool&gt;'."</c>.
///
/// Why this shape (issue #626): the SDK propagates a failure's message to the caller only when the
/// exception is an <see cref="McpException"/> — every other type collapses to the blanket string,
/// which is what made <c>save_thesis</c> un-diagnosable from the agent side while the server-side
/// cause stayed invisible. The SDK documents a tool-call filter rethrowing
/// <see cref="McpException"/> as the supported way to surface it, so the translation lives here
/// once rather than as a try/catch in each of the 60 tools.
///
/// The caller gets the exception type and message; the stack trace goes to the log only. A message
/// can still carry operational detail (a column name, a constraint), which is the point — this host
/// serves one authenticated owner, and a tool failure the agent cannot read is a tool failure
/// nobody fixes.
/// </summary>
public static class McpToolErrorSurfacing
{
    /// <summary>The name reported when a malformed request carries no tool name.</summary>
    private const string UnknownToolName = "unknown";

    /// <summary>
    /// Registers the host's tools together with the filter that makes their failures legible.
    /// Both transports call this one method rather than composing the pair themselves, so a
    /// transport cannot end up serving tools whose errors are still the blanket string.
    /// </summary>
    public static IMcpServerBuilder WithFinanceSentryTools(this IMcpServerBuilder builder, Assembly toolAssembly)
        => builder.WithToolErrorSurfacing().WithToolsFromAssembly(toolAssembly);

    public static IMcpServerBuilder WithToolErrorSurfacing(this IMcpServerBuilder builder)
    {
        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
            async (context, cancellationToken) =>
            {
                var toolName = context.Params?.Name ?? UnknownToolName;
                try
                {
                    return await next(context, cancellationToken);
                }
                catch (Exception ex) when (ex is not McpException && !IsCallerCancellation(ex, cancellationToken))
                {
                    (context.Services ?? context.Server?.Services)?.GetService<ILoggerFactory>()
                        ?.CreateLogger(typeof(McpToolErrorSurfacing))
                        .LogError(ex, "MCP tool {ToolName} failed", toolName);

                    throw new McpException(Describe(toolName, ex), ex);
                }
            }));

        return builder;
    }

    /// <summary>
    /// True only when the caller actually cancelled. Matching on <see cref="OperationCanceledException"/>
    /// alone would also catch an <see cref="System.Net.Http.HttpClient"/> timeout — which throws
    /// <c>TaskCanceledException</c> with the request's own token, not ours — and a timed-out upstream
    /// is exactly the failure a caller needs named.
    /// </summary>
    private static bool IsCallerCancellation(Exception ex, CancellationToken cancellationToken)
        => ex is OperationCanceledException && cancellationToken.IsCancellationRequested;

    /// <summary>
    /// Names the failure the way a caller can act on it: the tool, the exception type, and the
    /// message — plus the innermost message, which is where a provider wraps the detail that
    /// matters (an Npgsql constraint violation under a <c>DbUpdateException</c>, say).
    /// </summary>
    private static string Describe(string toolName, Exception ex)
    {
        var root = ex;
        while (root.InnerException is { } inner)
        {
            root = inner;
        }

        var detail = ReferenceEquals(root, ex)
            ? ex.Message
            : $"{ex.Message} -> {root.GetType().Name}: {root.Message}";

        return $"An error occurred invoking '{toolName}': {ex.GetType().Name}: {detail}";
    }
}
