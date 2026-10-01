namespace FinanceSentry.Core.Auth;

/// <summary>
/// Feature permissions, carried as claims of type <see cref="ClaimType"/>. A role grants a bundle of them
/// through Identity's role-claim table; a per-person toggle is the same claim in Identity's user-claim table.
/// Both reach the request principal through Identity's claims-principal factory.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";

    /// <summary>Connect and disconnect bank, brokerage and crypto accounts.</summary>
    public const string ConnectionsManage = "connections.manage";

    /// <summary>AI features: agent chat, Ledger narratives and companion wakes.</summary>
    public const string AiUse = "ai.use";

    /// <summary>Personal MCP access (the OAuth flow a client like Claude uses).</summary>
    public const string McpConnect = "mcp.connect";

    /// <summary>Issue and use long-lived MCP service tokens.</summary>
    public const string McpService = "mcp.service";

    /// <summary>Operations: the job dashboard and admin endpoints.</summary>
    public const string OpsAdmin = "ops.admin";

    /// <summary>Manage people and their roles.</summary>
    public const string UsersManage = "users.manage";

    public static readonly IReadOnlyList<string> All =
        [ConnectionsManage, AiUse, McpConnect, McpService, OpsAdmin, UsersManage];

    /// <summary>What the <see cref="AuthRoles.Member"/> role grants. Anything else is a per-person grant.</summary>
    public static readonly IReadOnlyList<string> MemberDefaults = [ConnectionsManage];

    /// <summary>The permissions each role grants; the startup seed converges the role-claim table to this.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ByRole =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [AuthRoles.Owner] = All,
            [AuthRoles.Member] = MemberDefaults,
        };
}
