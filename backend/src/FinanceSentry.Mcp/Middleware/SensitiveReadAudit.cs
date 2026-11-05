using System.Reflection;
using FinanceSentry.Mcp.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace FinanceSentry.Mcp.Middleware;

/// <summary>
/// A call-tool filter that puts every call of a <see cref="SensitiveProfileAttribute"/> tool on the audit trail before
/// the tool runs, for both transports - so a new tool of that kind is audited by being marked, not by remembering. If
/// the trail cannot be written the call fails with a generic message and the tool never runs.
/// </summary>
public static class SensitiveReadAudit
{
    public static IMcpServerBuilder WithSensitiveReadAudit(this IMcpServerBuilder builder, Assembly toolAssembly)
    {
        var catalog = SensitiveProfileReads.CatalogOf(toolAssembly);

        builder.WithRequestFilters(filters => filters.AddCallToolFilter(next =>
            async (context, cancellationToken) =>
            {
                var toolName = context.Params?.Name;
                if (toolName is null || !catalog.TryGetValue(toolName, out var marker))
                    return await next(context, cancellationToken);

                var root = context.Services ?? context.Server?.Services
                    ?? throw new McpException($"'{toolName}' was not run: no service provider to record the read.");

                // A scope of its own: the stdio transport hands the filter the root provider, and the trail's services
                // are scoped. The caller's identity does not live in the scope - the HTTP context and the local session do.
                await using var scope = root.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                try
                {
                    await SensitiveProfileReads.RecordReadAsync(scope.ServiceProvider, toolName, marker, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SensitiveReadAudit))
                        .LogError(ex, "Could not record the read of {ToolName} on the audit trail; the call was refused.", toolName);
                    throw new McpException($"'{toolName}' was not run: the read could not be recorded on the audit trail.");
                }

                return await next(context, cancellationToken);
            }));

        return builder;
    }
}
