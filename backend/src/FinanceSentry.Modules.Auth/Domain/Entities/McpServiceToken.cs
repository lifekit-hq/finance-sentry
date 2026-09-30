namespace FinanceSentry.Modules.Auth.Domain.Entities;

/// <summary>
/// A long-lived, revocable MCP service credential for first-party headless clients
/// (e.g. the OpenClaw gateway) that cannot perform the interactive OAuth refresh flow.
/// The row's Id is the token's jti; the MCP HTTP host checks it on every request
/// so the token can be revoked without rotating the signing secret.
/// </summary>
public class McpServiceToken
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string UserId { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    private McpServiceToken() { }

    public McpServiceToken(Guid id, string userId, string label, DateTime expiresAt)
    {
        Id = id;
        UserId = userId;
        Label = label;
        ExpiresAt = expiresAt;
    }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;

    public void Revoke() => RevokedAt = DateTime.UtcNow;
}
