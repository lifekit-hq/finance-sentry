using System.Reflection;
using FinanceSentry.Core.Auditing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FinanceSentry.Mcp.Auditing;

/// <summary>
/// The one place a read of declared profile data through MCP or the in-app agent is put on the audit trail. Shared by
/// the MCP host's call-tool filter and the agent's <c>McpToolBridge</c>, so the two paths record the same event.
/// </summary>
public static class SensitiveProfileReads
{
    /// <summary>The attribute governing <paramref name="tool"/>: its own, else its class's.</summary>
    public static SensitiveProfileAttribute? Find(MethodInfo tool)
        => tool.GetCustomAttribute<SensitiveProfileAttribute>()
            ?? tool.DeclaringType?.GetCustomAttribute<SensitiveProfileAttribute>();

    /// <summary>Every sensitive tool in <paramref name="assembly"/> by its MCP name.</summary>
    public static IReadOnlyDictionary<string, SensitiveProfileAttribute> CatalogOf(Assembly assembly)
    {
        var catalog = new Dictionary<string, SensitiveProfileAttribute>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null))
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var tool = method.GetCustomAttribute<McpServerToolAttribute>();
                if (tool is not null && Find(method) is { } marker)
                    catalog[tool.Name ?? method.Name] = marker;
            }
        }

        return catalog;
    }

    /// <summary>
    /// Records that the current person's profile data is about to be read through <paramref name="toolName"/>. A call
    /// with no person in scope reads nothing and records nothing. Throws when the trail cannot be written - the caller
    /// must not run the tool then.
    /// </summary>
    public static async Task RecordReadAsync(
        IServiceProvider services, string toolName, SensitiveProfileAttribute marker, CancellationToken cancellationToken)
    {
        var actor = services.GetRequiredService<IAuditActor>();
        if (actor.UserId is not { } subject)
            return;

        await services.GetRequiredService<IAuditTrail>().RecordAsync(
            new AuditEntry(subject, subject, actor.Kind, AuditAction.Read, toolName, resourceId: null, marker.FieldNames),
            cancellationToken);
    }
}
