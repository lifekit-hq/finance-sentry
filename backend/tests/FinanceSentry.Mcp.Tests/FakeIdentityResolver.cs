using FinanceSentry.Mcp.Abstractions;

namespace FinanceSentry.Mcp.Tests;

/// <summary>
/// Test double for the authenticated MCP identity every tool scopes its data to.
/// Returns null by default; set <see cref="ResolvedUserId"/> to act as a user.
/// </summary>
public sealed class FakeIdentityResolver : IIdentityResolver
{
    public Guid? ResolvedUserId { get; init; }
    public string? ResolvedEmail { get; init; }

    public Guid? GetUserId() => ResolvedUserId;
    public string? GetEmail() => ResolvedEmail;
    public bool IsConfigured => ResolvedUserId.HasValue;
}
