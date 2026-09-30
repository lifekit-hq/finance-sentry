namespace FinanceSentry.Core.Auth;

/// <summary>
/// Named ASP.NET Core authorization policies, one per <see cref="Permissions"/> value. Each is satisfied by an
/// authenticated user whose principal carries that permission claim.
/// </summary>
public static class AuthPolicies
{
    public const string RequireConnectionsManage = "RequireConnectionsManage";
    public const string RequireAiUse = "RequireAiUse";
    public const string RequireMcpConnect = "RequireMcpConnect";
    public const string RequireMcpService = "RequireMcpService";

    /// <summary>Operations access (<see cref="Permissions.OpsAdmin"/>); the name predates permissions.</summary>
    public const string RequireOwner = "RequireOwner";

    public const string RequireUsersManage = "RequireUsersManage";

    /// <summary>Policy name to the permission claim value it requires.</summary>
    public static readonly IReadOnlyDictionary<string, string> PermissionByPolicy = new Dictionary<string, string>
    {
        [RequireConnectionsManage] = Permissions.ConnectionsManage,
        [RequireAiUse] = Permissions.AiUse,
        [RequireMcpConnect] = Permissions.McpConnect,
        [RequireMcpService] = Permissions.McpService,
        [RequireOwner] = Permissions.OpsAdmin,
        [RequireUsersManage] = Permissions.UsersManage,
    };
}
