namespace FinanceSentry.Core.Interfaces;

public interface IAlertGeneratorService
{
    Task GenerateLowBalanceAlertAsync(
        Guid userId,
        Guid accountId,
        string accountName,
        decimal balance,
        decimal threshold,
        CancellationToken ct = default);

    Task ResolveLowBalanceAlertAsync(
        Guid userId,
        Guid accountId,
        CancellationToken ct = default);

    Task GenerateSyncFailureAlertAsync(
        Guid userId,
        string provider,
        Guid? accountId,
        string? accountName,
        string? errorCode,
        CancellationToken ct = default);

    Task ResolveSyncFailureAlertAsync(
        Guid userId,
        string provider,
        Guid? accountId,
        CancellationToken ct = default);

    Task GenerateUnusualSpendAlertAsync(
        Guid userId,
        string category,
        decimal currentMonthSpend,
        decimal averageMonthlySpend,
        CancellationToken ct = default);

    Task DeleteAlertsForAccountAsync(
        Guid accountId,
        CancellationToken ct = default);

    Task GenerateThesisBreakAlertAsync(
        Guid userId,
        Guid thesisId,
        string ticker,
        string reason,
        CancellationToken ct = default);

    Task ResolveThesisBreakAlertAsync(
        Guid userId,
        Guid thesisId,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a market-structure Alert for a held ticker (e.g. an unusual move at/above the alert bar).
    /// <paramref name="referenceId"/> is a deterministic per-ticker id so dedup/resolve is stable.
    /// </summary>
    Task GenerateMarketStructureAlertAsync(
        Guid userId,
        Guid referenceId,
        string ticker,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a market-structure freshness Alert when the Radar data is stale or an ingestion run failed.
    /// </summary>
    Task GenerateMarketStructureFreshnessAlertAsync(
        Guid userId,
        Guid referenceId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a policy-violation Alert (022) naming the rule, observed value, and limit. When
    /// <paramref name="isOverride"/> is true, this records an explicit override of a Refused
    /// verdict rather than a fresh violation (FR-007) — always Info severity, never silent.
    /// </summary>
    Task GeneratePolicyViolationAlertAsync(
        Guid userId,
        string ruleKey,
        string subject,
        decimal observedValue,
        decimal limitValue,
        bool isOverride = false,
        CancellationToken ct = default);

    Task ResolvePolicyViolationAlertAsync(
        Guid userId,
        string ruleKey,
        string subject,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a top-tier opportunity-candidate Alert (019). <paramref name="referenceId"/> is the
    /// candidate id so repeated re-scores of the same candidate don't spam duplicate alerts within
    /// the silence window.
    /// </summary>
    Task GenerateOpportunityAlertAsync(
        Guid userId,
        Guid referenceId,
        string ticker,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a heads-up Alert that a bank connection's consent is about to expire, so the user can
    /// reconnect proactively instead of after data goes stale. <paramref name="referenceId"/> is the
    /// connection id so a daily detector doesn't spam duplicates within the silence window.
    /// </summary>
    Task GenerateConsentExpiringAlertAsync(
        Guid userId,
        Guid referenceId,
        string providerName,
        DateTime expiresAt,
        CancellationToken ct = default);

    /// <summary>
    /// Raises an operational Alert that a scheduled job has failed <paramref name="consecutiveCount"/>
    /// times in a row (US4 / FR-009). <paramref name="referenceId"/> is a stable per-job id so the streak
    /// dedups within the silence window; the caller (the Hangfire failure filter) guarantees one call per
    /// streak and clears the streak on the next success so a later failure can re-alert.
    /// </summary>
    Task GenerateJobFailureAlertAsync(
        Guid userId,
        Guid referenceId,
        string jobName,
        int consecutiveCount,
        string? lastError,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a weekly performance-brief Info alert (412) summarising the book's TWR versus SPY.
    /// Silenced for 6 days so the weekly cron doesn't repeat on a re-run.
    /// </summary>
    Task GeneratePerformanceBriefAlertAsync(
        Guid userId,
        string headline,
        string body,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert when the 30-day cash-flow projection shows the account going negative
    /// (041). <paramref name="accountId"/> is the dedup key so the daily sentinel never duplicates
    /// while the shortfall persists.
    /// </summary>
    Task GenerateCashShortfallAlertAsync(
        Guid userId,
        Guid accountId,
        string accountName,
        DateOnly shortfallDate,
        decimal shortfallAmount,
        string currency,
        CancellationToken ct = default);

    Task ResolveCashShortfallAlertAsync(
        Guid userId,
        Guid accountId,
        CancellationToken ct = default);
}
