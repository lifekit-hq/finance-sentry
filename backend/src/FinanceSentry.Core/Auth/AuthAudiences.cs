namespace FinanceSentry.Core.Auth;

/// <summary>JWT <c>aud</c> values: which host a token is issued for.</summary>
public static class AuthAudiences
{
    /// <summary>The app's access token, accepted by the API host.</summary>
    public const string App = "app";

    /// <summary>MCP access and service tokens, accepted by the MCP host.</summary>
    public const string Mcp = "mcp";
}
