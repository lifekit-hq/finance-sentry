namespace FinanceSentry.Core.Auditing;

/// <summary>Who or what performed the audited action, as the audit trail records it.</summary>
public enum AuditActorKind
{
    /// <summary>The person, signed in to the app.</summary>
    OwnerSession,

    /// <summary>A client the person connected through the MCP OAuth flow (a personal token).</summary>
    McpPersonal,

    /// <summary>A long-lived MCP service token.</summary>
    McpService,

    /// <summary>The in-app agent, acting for the person inside their request.</summary>
    InAppAgent,

    /// <summary>A background job: no person in scope.</summary>
    Job,
}
