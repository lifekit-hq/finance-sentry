namespace FinanceSentry.Modules.Agent.Application.Services;

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Mcp;
using FinanceSentry.Mcp.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

/// <summary>
/// The single adapter between the existing MCP tool surface and the Anthropic Messages API — so the
/// browser Ledger and the OpenClaw Ledger reason over the identical tool contract (US3, parity by
/// construction). It enumerates every <c>[McpServerTool]</c> method in the MCP assembly, converts each
/// to an Anthropic tool schema (name/description/input_schema derived verbatim from the tool metadata),
/// and dispatches a <c>tool_use</c> by invoking that tool in the current authenticated request scope so
/// the tool resolves the caller's user id (FR-008). No user id is ever taken from model output — the
/// <c>userId</c> parameter is stripped from the advertised schema and never bound.
/// </summary>
public sealed class McpToolBridge
{
    private const string UserIdParameterName = "userId";

    // JsonSchemaExporter requires an explicit type-info resolver on the options.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private readonly AgentOptions _options;
    private readonly ILogger<McpToolBridge> _logger;
    private readonly IReadOnlyList<Assembly> _toolAssemblies;
    private readonly Lock _sync = new();
    private IReadOnlyList<LlmTool>? _tools;
    private IReadOnlyDictionary<string, ToolEntry>? _catalog;

    public McpToolBridge(IOptions<AgentOptions> options, ILogger<McpToolBridge> logger)
        : this(options, logger, McpServiceRegistration.McpAssembly)
    {
    }

    // Test seam: dispatch/schema over an explicit tool assembly set instead of the MCP assembly.
    internal McpToolBridge(IOptions<AgentOptions> options, ILogger<McpToolBridge> logger, params Assembly[] toolAssemblies)
    {
        _options = options.Value;
        _logger = logger;
        _toolAssemblies = toolAssemblies is { Length: > 0 } ? toolAssemblies : [McpServiceRegistration.McpAssembly];
    }

    /// <summary>The Anthropic tool catalog (built once, cached).</summary>
    public IReadOnlyList<LlmTool> GetTools()
    {
        EnsureBuilt();
        return _tools!;
    }

    /// <summary>
    /// Invokes the named MCP tool with the model-supplied input, in the given request scope, and returns
    /// its result as a <c>tool_result</c> block. Unknown tool / bad input / tool error ⇒ an
    /// <c>is_error</c> result the model degrades over (FR-014) — never a fabricated value.
    /// </summary>
    public async Task<LlmToolResultBlock> DispatchAsync(
        string toolUseId, string name, JsonElement input, IServiceProvider scope, CancellationToken ct)
    {
        EnsureBuilt();

        if (!_catalog!.TryGetValue(name, out var entry))
        {
            _logger.LogWarning("Agent requested unknown tool {Tool}.", name);
            return new LlmToolResultBlock(toolUseId, $"Unknown tool '{name}'.", IsError: true);
        }

        // Everything the model's tool call does is the in-app agent acting for the person - a write a tool makes is
        // audited as the agent's, and a read of profile data is recorded below before the tool is allowed to run.
        using var asAgent = scope.GetService<IAuditActor>()?.Use(AuditActorKind.InAppAgent);

        if (entry.Sensitive is { } sensitive)
        {
            try
            {
                await SensitiveProfileReads.RecordReadAsync(scope, name, sensitive, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Could not record the agent's read via {Tool} on the audit trail; the call was refused.", name);
                return new LlmToolResultBlock(toolUseId, $"Tool '{name}' was not run: the read could not be recorded on the audit trail.", IsError: true);
            }
        }

        try
        {
            var target = scope.GetRequiredService(entry.DeclaringType);
            var args = BindArguments(entry, input, ct);
            var raw = entry.Method.Invoke(target, args);
            var value = await UnwrapAsync(raw);
            var content = JsonSerializer.Serialize(value, SerializerOptions);
            return new LlmToolResultBlock(toolUseId, content, IsError: false);
        }
        catch (TargetInvocationException tie)
        {
            _logger.LogWarning(tie.InnerException, "Tool {Tool} threw during dispatch.", name);
            return new LlmToolResultBlock(toolUseId, $"Tool '{name}' failed: {tie.InnerException?.Message}", IsError: true);
        }
        catch (ToolArgumentException aex)
        {
            return new LlmToolResultBlock(toolUseId, aex.Message, IsError: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool {Tool} dispatch error.", name);
            return new LlmToolResultBlock(toolUseId, $"Tool '{name}' failed: {ex.Message}", IsError: true);
        }
    }

    private void EnsureBuilt()
    {
        if (_tools is not null)
        {
            return;
        }

        lock (_sync)
        {
            if (_tools is not null)
            {
                return;
            }

            var (tools, catalog) = BuildCatalog();
            _tools = tools;
            _catalog = catalog;
            _logger.LogInformation("Bridged {Count} MCP tools to the Anthropic tool surface.", tools.Count);
        }
    }

    private (IReadOnlyList<LlmTool>, IReadOnlyDictionary<string, ToolEntry>) BuildCatalog()
    {
        var allow = _options.ToolAllowList is { Count: > 0 }
            ? new HashSet<string>(_options.ToolAllowList, StringComparer.Ordinal)
            : null;

        var tools = new List<LlmTool>();
        var catalog = new Dictionary<string, ToolEntry>(StringComparer.Ordinal);

        foreach (var toolType in _toolAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            foreach (var method in toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr is null)
                {
                    continue;
                }

                var name = toolAttr.Name ?? method.Name;
                if (allow is not null && !allow.Contains(name))
                {
                    continue;
                }

                if (catalog.ContainsKey(name))
                {
                    _logger.LogWarning("Duplicate MCP tool name {Tool}; keeping the first.", name);
                    continue;
                }

                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
                var exposed = method.GetParameters()
                    .Where(p => p.ParameterType != typeof(CancellationToken)
                        && !string.Equals(p.Name, UserIdParameterName, StringComparison.Ordinal))
                    .ToArray();

                var schema = BuildInputSchema(exposed);
                tools.Add(new LlmTool(name, description, schema));
                catalog[name] = new ToolEntry(toolType, method, method.GetParameters(), SensitiveProfileReads.Find(method));
            }
        }

        return (tools, catalog);
    }

    private static JsonNode BuildInputSchema(ParameterInfo[] parameters)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in parameters)
        {
            var node = JsonSchemaExporter.GetJsonSchemaAsNode(SerializerOptions, parameter.ParameterType);
            if (node is JsonObject obj)
            {
                var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    obj["description"] = description;
                }

                properties[parameter.Name!] = obj;
            }
            else
            {
                properties[parameter.Name!] = node;
            }

            if (!parameter.HasDefaultValue)
            {
                required.Add(parameter.Name!);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return schema;
    }

    private object?[] BindArguments(ToolEntry entry, JsonElement input, CancellationToken ct)
    {
        var parameters = entry.Parameters;
        var args = new object?[parameters.Length];
        var hasObject = input.ValueKind == JsonValueKind.Object;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            if (parameter.ParameterType == typeof(CancellationToken))
            {
                args[i] = ct;
                continue;
            }

            // The caller's user id is resolved from the request context by the tool itself; it is never
            // bound from model output (FR-008).
            if (string.Equals(parameter.Name, UserIdParameterName, StringComparison.Ordinal))
            {
                args[i] = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                continue;
            }

            if (hasObject && TryGetProperty(input, parameter.Name!, out var element))
            {
                args[i] = JsonSerializer.Deserialize(element.GetRawText(), parameter.ParameterType, SerializerOptions);
            }
            else if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
            }
            else
            {
                throw new ToolArgumentException($"Missing required parameter '{parameter.Name}'.");
            }
        }

        return args;
    }

    private static bool TryGetProperty(JsonElement input, string name, out JsonElement value)
    {
        if (input.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in input.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static async Task<object?> UnwrapAsync(object? raw)
    {
        if (raw is Task task)
        {
            await task.ConfigureAwait(false);
            var resultProperty = task.GetType().GetProperty("Result");
            if (resultProperty is not null && resultProperty.PropertyType != typeof(void)
                && resultProperty.PropertyType.Name != "VoidTaskResult")
            {
                return resultProperty.GetValue(task);
            }

            return null;
        }

        if (raw is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return null;
        }

        return raw;
    }

    private sealed record ToolEntry(
        Type DeclaringType, MethodInfo Method, ParameterInfo[] Parameters, SensitiveProfileAttribute? Sensitive);

    private sealed class ToolArgumentException(string message) : Exception(message);
}
