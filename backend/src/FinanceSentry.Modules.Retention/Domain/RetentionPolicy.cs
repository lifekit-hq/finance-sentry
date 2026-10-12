namespace FinanceSentry.Modules.Retention.Domain;

/// <summary>
/// One table's retention decision (feature 024). The compiled <c>RetentionPolicyRegistry</c> holds
/// exactly one of these per persistent table — the single, reviewable source of truth (FR-001).
/// Identifiers are stored with their exact database case because table naming is inconsistent across
/// modules (e.g. snake-case <c>companion_events</c> vs Pascal-case <c>SyncJobs</c>); the purge engine quotes
/// them verbatim.
/// </summary>
/// <param name="Schema">Postgres schema, exact case (e.g. <c>bank_sync</c>).</param>
/// <param name="Table">Table name, exact case (e.g. <c>companion_events</c>, <c>SyncJobs</c>).</param>
/// <param name="TimestampColumn">Cutoff column, exact case; null only for <see cref="RetentionAction.Keep"/>.</param>
/// <param name="Action">Purge, Downsample, or Keep.</param>
/// <param name="WindowDays">Age threshold in days; null only for <see cref="RetentionAction.Keep"/>.</param>
/// <param name="Enforcer">Generic engine, or a named bespoke job.</param>
/// <param name="BespokeJobName">Recurring-job id when <paramref name="Enforcer"/> is <see cref="RetentionEnforcer.Bespoke"/>.</param>
/// <param name="BatchSize">Delete batch size for the generic engine.</param>
/// <param name="Notes">Reviewer-facing rationale.</param>
public sealed record RetentionPolicy(
    string Schema,
    string Table,
    string? TimestampColumn,
    RetentionAction Action,
    int? WindowDays,
    RetentionEnforcer Enforcer = RetentionEnforcer.Generic,
    string? BespokeJobName = null,
    int BatchSize = 5000,
    string? Notes = null)
{
    /// <summary>Qualified identifier for logging/keys, e.g. <c>bank_sync.SyncJobs</c>.</summary>
    public string QualifiedName => $"{Schema}.{Table}";

    /// <summary>Double-quoted, exact-case SQL reference, e.g. <c>"bank_sync"."SyncJobs"</c>.</summary>
    public string QuotedTable => $"\"{Schema}\".\"{Table}\"";

    /// <summary>Double-quoted, exact-case timestamp column, e.g. <c>"CreatedAt"</c>.</summary>
    public string QuotedTimestamp => $"\"{TimestampColumn}\"";

    /// <summary>True when the generic engine should actively purge this table.</summary>
    public bool IsGenericPurge => Action == RetentionAction.Purge && Enforcer == RetentionEnforcer.Generic;
}
