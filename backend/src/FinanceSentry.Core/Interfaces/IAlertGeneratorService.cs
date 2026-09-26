namespace FinanceSentry.Core.Interfaces;

/// <summary>How much of the dedup discipline applies to one alert emission.</summary>
public enum AlertDedup
{
    /// <summary>An open alert on the same reference suppresses a new one; then the silence window.</summary>
    ActiveThenSilence,

    /// <summary>Silence window only — each occurrence deserves its own row once the window has passed.</summary>
    SilenceOnly,

    /// <summary>Always record: the caller has already decided this event must be seen.</summary>
    Always,

    /// <summary>
    /// Once per reference, ever: any alert already raised on the reference — open, dismissed or
    /// resolved — suppresses a new one. No silence window applies.
    /// </summary>
    OncePerReference,
}

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
    /// <paramref name="dedup"/> is the caller's choice: by default an open alert on the ticker suppresses
    /// a new one; <see cref="AlertDedup.SilenceOnly"/> lets each move past the silence window through,
    /// resolving the still-open earlier alert on the ticker first.
    /// </summary>
    Task GenerateMarketStructureAlertAsync(
        Guid userId,
        Guid referenceId,
        string ticker,
        string reason,
        CancellationToken ct = default,
        AlertDedup dedup = AlertDedup.ActiveThenSilence);

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

    /// <summary>Resolves the Opportunity alert for a candidate once it expires or is closed.</summary>
    Task ResolveOpportunityAlertAsync(
        Guid userId,
        Guid referenceId,
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

    /// <summary>Resolves the consent-expiring Alert once the connection is re-authenticated or unlinked.</summary>
    Task ResolveConsentExpiringAlertAsync(
        Guid userId,
        Guid referenceId,
        CancellationToken ct = default);

    /// <summary>
    /// Raises an operational Alert that a scheduled job has failed <paramref name="consecutiveCount"/>
    /// times in a row (US4 / FR-009). <paramref name="referenceId"/> is a stable per-job id so the streak
    /// dedups within the silence window; the caller (the Hangfire failure filter) guarantees one call per
    /// streak and clears the streak on the next success so a later failure can re-alert. A newer streak
    /// supersedes a still-open (unread, undismissed) earlier alert for the same job — the old row is
    /// resolved and a fresh one inserted — so re-alerting never collides with <c>idx_alert_dedup</c>.
    /// </summary>
    Task GenerateJobFailureAlertAsync(
        Guid userId,
        Guid referenceId,
        string jobName,
        int consecutiveCount,
        string? lastError,
        CancellationToken ct = default);

    /// <summary>Resolves the job-failure Alert once the job's next run succeeds.</summary>
    Task ResolveJobFailureAlertAsync(
        Guid userId,
        Guid referenceId,
        CancellationToken ct = default);

    /// <summary>Resolves the market-structure freshness Alert once the Radar feed catches back up.</summary>
    Task ResolveMarketStructureFreshnessAlertAsync(
        Guid userId,
        Guid referenceId,
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

    /// <summary>Resolves the price-hike Alert once the charge falls back to (or below) baseline.</summary>
    Task ResolvePriceHikeAlertAsync(
        Guid userId,
        Guid subscriptionId,
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

    /// <summary>Resolves the category-spike Alert once month-to-date spend falls back under baseline.</summary>
    Task ResolveCategorySpikeAlertAsync(
        Guid userId,
        string category,
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

    /// <summary>Resolves the open rebalance-proposal Alert once drift no longer needs rebalancing.</summary>
    Task ResolveRebalanceProposalAlertAsync(
        Guid userId,
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

    /// <summary>Resolves the open cash-sweep-proposal Alert once idle cash no longer exceeds the buffer.</summary>
    Task ResolveCashSweepProposalAlertAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Raises an Info alert that a holding or watchlist ticker has earnings or an ex-dividend date
    /// coming up. <paramref name="eventType"/> is <see cref="EarningsAheadEventType.Earnings"/> or
    /// <see cref="EarningsAheadEventType.ExDividend"/> — the values the earnings-ahead job reads from
    /// Yahoo quoteSummary. Deduped per (ticker, event type, event date), so the daily detector never
    /// re-alerts the same event as the date approaches; ~0.1 fires/day by design (rare, low-noise).
    /// </summary>
    Task GenerateEarningsAheadAlertAsync(
        Guid userId,
        string ticker,
        string eventType,
        DateOnly eventDate,
        bool isEstimate,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the earnings-ahead Alert for one exact (ticker, event type, event date) once that date
    /// is in the past — the reference is deterministic, so this recomputes the same id the generator
    /// used and resolves it directly, no threshold guesswork needed.
    /// </summary>
    Task ResolveEarningsAheadAlertAsync(
        Guid userId,
        string ticker,
        string eventType,
        DateOnly eventDate,
        CancellationToken ct = default);

    /// <summary>
    /// Raises an Info alert that a 10-K, 10-Q or 8-K has landed on a holding or thesis-proxy ticker.
    /// Deduped per (ticker, EDGAR accession number) — that pair is unique forever, so the same filing
    /// never alerts twice however many times the hourly detector re-reads the submissions feed.
    /// </summary>
    Task GenerateFilingLandedAlertAsync(
        Guid userId,
        string ticker,
        string form,
        DateOnly filingDate,
        string accessionNumber,
        string documentUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert that a held name or thesis keyword clustered in the news (N1,
    /// ledger-heartbeat design): two or more distinct sources within a 2h window, a thesis-attached
    /// source hit, or a material-class keyword (guidance, downgrade, investigation, M&amp;A, halted,
    /// recall, acquisition). Deduped per (ticker, <paramref name="day"/>) — article-level ContentHash
    /// dedup already collapses re-ingested items at the news layer, so a feed that returns the same
    /// items every 30 minutes produces one alert here, not 48. The loudest signal in the design
    /// (~0.3-1 fires/day); noise controls are the feature, not a refinement.
    /// </summary>
    Task GenerateNewsClusterAlertAsync(
        Guid userId,
        string ticker,
        string reason,
        DateOnly day,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert that a monthly budget's month-to-date spend has reached 90% of its
    /// limit (C2, ledger-heartbeat design). Deduped per (<paramref name="budgetId"/>,
    /// <paramref name="year"/>, <paramref name="month"/>) — a budget crosses 90% at most once per
    /// month regardless of how many times the daily hygiene run re-checks it, a mid-month limit
    /// edit, or a refund that later lets spend cross back over the line. Once raised, the alert is
    /// never raised again for that month, even after the user dismisses or resolves it.
    /// </summary>
    Task GenerateBudgetNearLimitAlertAsync(
        Guid userId,
        Guid budgetId,
        string category,
        decimal spentUsd,
        decimal limitUsd,
        int year,
        int month,
        CancellationToken ct = default);

    /// <summary>
    /// Raises a Warning alert that a monthly budget's month-to-date spend has reached or passed
    /// 100% of its limit (C2, ledger-heartbeat design). A distinct alert from
    /// <see cref="GenerateBudgetNearLimitAlertAsync"/> — reaching 100% later in the same month
    /// after a 90% alert already fired is its own event and still gets through. Deduped the same
    /// way, per (<paramref name="budgetId"/>, <paramref name="year"/>, <paramref name="month"/>).
    /// </summary>
    Task GenerateBudgetExceededAlertAsync(
        Guid userId,
        Guid budgetId,
        string category,
        decimal spentUsd,
        decimal limitUsd,
        int year,
        int month,
        CancellationToken ct = default);
}

/// <summary>
/// The small, Alerts-owned vocabulary for <see cref="IAlertGeneratorService.GenerateEarningsAheadAlertAsync"/>.
/// Mirrors the Research module's Yahoo-sourced event-type strings without a hard reference to it —
/// alert types cross module boundaries as plain string contracts throughout this codebase.
/// </summary>
public static class EarningsAheadEventType
{
    public const string Earnings = "earnings";
    public const string ExDividend = "ex_dividend";
}
