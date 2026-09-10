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

    /// <summary>
    /// Raises a Warning alert when a recurring subscription or installment's latest charge is
    /// significantly above the historical average (044). <paramref name="subscriptionId"/> is the
    /// dedup key so a daily sentinel never duplicates while the price remains elevated.
    /// </summary>
    Task GeneratePriceHikeAlertAsync(
        Guid userId,
        Guid subscriptionId,
        string merchantName,
        decimal baselineAmount,
        decimal currentAmount,
        string currency,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert when the same merchant charges the same amount multiple times within
    /// the detection window on the same account (044/US2). Deduped per
    /// (accountId, normalized merchant key, amount) so a daily sentinel never re-fires while the
    /// alert is active; <paramref name="merchantName"/> is the raw statement name shown to the user.
    /// </summary>
    Task GenerateDuplicateChargeAlertAsync(
        Guid userId,
        Guid accountId,
        string merchantKey,
        string merchantName,
        decimal chargeAmount,
        string currency,
        int chargeCount,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert when month-to-date spend in a category exceeds the 6-month baseline
    /// by more than the configured multiplier (044/US3). Deduped per (userId, category).
    /// </summary>
    Task GenerateCategorySpikeAlertAsync(
        Guid userId,
        string category,
        decimal currentMonthSpend,
        decimal baselineSpend,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert when a specific cross-currency conversion (a matched debit+credit
    /// transfer pair, e.g. EUR→UAH via white card) lost more than the configured percentage to
    /// the FX spread (044/US4). Deduped per (userId, <paramref name="debitTransactionId"/>) —
    /// each concrete conversion alerts at most once.
    /// </summary>
    Task GenerateFxSpreadAlertAsync(
        Guid userId,
        Guid debitTransactionId,
        string fromCurrency,
        string toCurrency,
        decimal impliedRate,
        decimal marketRate,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert proposing a concrete rebalance order list when IPS bands are breached
    /// (432). <paramref name="orderCount"/> is the number of order lines; <paramref name="orderSummary"/>
    /// is the human-readable formatted list. Silenced 24 hours so the daily job doesn't re-propose
    /// while a prior one is still open or was recently dismissed.
    /// </summary>
    Task GenerateRebalanceProposalAlertAsync(
        Guid userId,
        int orderCount,
        string orderSummary,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert proposing deployment of idle cash that exceeds the configured buffer
    /// (432 US2). <paramref name="excessUsd"/> is the dollar amount above the min-cash-buffer threshold.
    /// Silenced 24 hours so the daily job doesn't re-propose while a prior one is open.
    /// </summary>
    Task GenerateCashSweepProposalAlertAsync(
        Guid userId,
        decimal idleCashUsd,
        decimal minBufferUsd,
        decimal excessUsd,
        CancellationToken ct = default);
}
