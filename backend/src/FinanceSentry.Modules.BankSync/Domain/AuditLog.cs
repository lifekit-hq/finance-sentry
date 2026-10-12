namespace FinanceSentry.Modules.BankSync.Domain;

using FinanceSentry.Core.Auth;

/// <summary>
/// Immutable audit log record — records every data access event.
/// Constitution V compliance: all data access must be logged.
/// Never stores sensitive data (no tokens, no account numbers beyond last 4).
/// </summary>
public class AuditLog : IUserOwned
{
    public Guid AuditId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>
    /// Action performed. Allowed values:
    /// READ_ACCOUNT, READ_TRANSACTIONS, WRITE_ACCOUNT, DELETE_ACCOUNT,
    /// CREDENTIAL_ACCESS, SYNC_TRIGGERED
    /// </summary>
    public string Action { get; private set; } = string.Empty;

    public string ResourceType { get; private set; } = string.Empty;
    public Guid? ResourceId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTime PerformedAt { get; private set; }
    public string? CorrelationId { get; private set; }

    private AuditLog() { } // EF Core

    public static AuditLog Create(
        Guid userId,
        string action,
        string resourceType,
        Guid? resourceId,
        string? ipAddress = null,
        string? userAgent = null,
        string? correlationId = null)
    {
        return new AuditLog
        {
            AuditId = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            PerformedAt = DateTime.UtcNow,
            CorrelationId = correlationId
        };
    }
}

/// <summary>Well-known audit action constants.</summary>
public static class AuditActions
{
    public const string ReadAccount = "READ_ACCOUNT";
    public const string ReadTransactions = "READ_TRANSACTIONS";
    public const string WriteAccount = "WRITE_ACCOUNT";
    public const string DeleteAccount = "DELETE_ACCOUNT";
    public const string CredentialAccess = "CREDENTIAL_ACCESS";
    public const string SyncTriggered = "SYNC_TRIGGERED";
}
