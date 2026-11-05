namespace FinanceSentry.Core.Auditing;

/// <summary>What was done to the audited resource.</summary>
public enum AuditAction
{
    Create,
    Update,
    Delete,

    /// <summary>Profile data left the app through an MCP tool or the in-app agent.</summary>
    Read,
}
