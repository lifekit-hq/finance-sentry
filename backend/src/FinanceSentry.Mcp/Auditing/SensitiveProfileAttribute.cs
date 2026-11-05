namespace FinanceSentry.Mcp.Auditing;

/// <summary>
/// Marks an MCP tool that returns declared profile data (income, goals, career events). Every call - from an MCP
/// client or from the in-app agent - is put on the audit trail BEFORE the tool runs, recording the tool's name, whose
/// data it was, who asked, and <see cref="FieldNames"/>: the fields the tool can disclose, by name. The tool does not
/// remember to do anything; the MCP host's call filter and <c>McpToolBridge</c> read this attribute.
/// <para>
/// On a class it covers every tool method in it. Field names are identifiers (see <c>AuditEntry</c>); a name that is
/// not one makes the call fail rather than record something else.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SensitiveProfileAttribute(params string[] fieldNames) : Attribute
{
    public IReadOnlyList<string> FieldNames { get; } = fieldNames;
}
