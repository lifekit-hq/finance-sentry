namespace FinanceSentry.Modules.Alerts.Application.Services;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;

public class AlertGeneratorService(IAlertRepository alerts) : IAlertGeneratorService
{
    /// <summary>
    /// The backstop silence window per alert type: how long after the last alert of that type on the
    /// same reference a fresh one stays quiet. Every generator reads its window from here, so adding
    /// an alert type is one row rather than one more hand-rolled dedup block — <see cref="EmitAsync"/>
    /// throws if a type reaches it without a declared window.
    /// </summary>
    private static readonly Dictionary<string, TimeSpan> SilenceWindows = new(StringComparer.Ordinal)
    {
        [AlertType.LowBalance] = TimeSpan.FromHours(24),
        [AlertType.SyncFailure] = TimeSpan.FromHours(12),
        [AlertType.ThesisBroken] = TimeSpan.FromHours(24),
        [AlertType.MarketStructure] = TimeSpan.FromHours(24),
        [AlertType.PolicyViolation] = TimeSpan.FromHours(24),
        [AlertType.Opportunity] = TimeSpan.FromHours(24),
        // Reminders repeat only every 3 days while inside the pre-expiry window, so the detector can
        // run daily without spamming the same connection.
        [AlertType.ConsentExpiring] = TimeSpan.FromDays(3),
        // Backstop only — the failure filter already guarantees one call per streak. Short enough
        // that a genuine success-then-new-streak re-alerts, long enough to absorb a double-call.
        [AlertType.JobFailure] = TimeSpan.FromMinutes(15),
        // 6 days: weekly cron fires each Monday; prevents a re-run or drift from double-alerting.
        [AlertType.PerformanceBrief] = TimeSpan.FromDays(6),
        // Active-alert dedup is the primary guard; 24h prevents re-alert the same day after a
        // manual resolve.
        [AlertType.CashShortfall] = TimeSpan.FromHours(24),
        // 30-day backstop: prevents re-alert after a manual dismiss until the next price move.
        [AlertType.PriceHike] = TimeSpan.FromDays(30),
        // 7-day backstop: no re-alert in the same week after a manual dismiss.
        [AlertType.DuplicateCharge] = TimeSpan.FromDays(7),
        // 7-day backstop while the spike persists within the month.
        [AlertType.CategorySpike] = TimeSpan.FromDays(7),
        // 7-day backstop; the daily detector doesn't spam while the routing pattern persists.
        [AlertType.FxSpread] = TimeSpan.FromDays(7),
        // One proposal per user per day; mirrors LowBalance/ThesisBroken cadence.
        [AlertType.RebalanceProposal] = TimeSpan.FromHours(24),
        [AlertType.CashSweepProposal] = TimeSpan.FromHours(24),
    };

    /// <summary>
    /// Staleness of the radar feed is a different event from a structural move on one ticker, even
    /// though both ride <see cref="AlertType.MarketStructure"/> — it gets its own, shorter window.
    /// </summary>
    private static readonly TimeSpan MarketStructureFreshnessSilenceWindow = TimeSpan.FromHours(12);

    private readonly IAlertRepository _alerts = alerts;

    /// <summary>How much of the dedup discipline applies to one emission.</summary>
    private enum Dedup
    {
        /// <summary>An open alert on the same reference suppresses a new one; then the silence window.</summary>
        ActiveThenSilence,

        /// <summary>Silence window only — each occurrence deserves its own row once the window has passed.</summary>
        SilenceOnly,

        /// <summary>Always record: the caller has already decided this event must be seen.</summary>
        Always,
    }

    public Task GenerateLowBalanceAlertAsync(
        Guid userId, Guid accountId, string accountName,
        decimal balance, decimal threshold, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.LowBalance, AlertSeverity.Warning, accountId, accountName,
            $"Low balance on {accountName}",
            $"Your {accountName} balance ({balance:C}) has dropped below your {threshold:C} threshold."),
            ct);

    public Task ResolveLowBalanceAlertAsync(
        Guid userId, Guid accountId, CancellationToken ct = default)
        => ResolveAsync(userId, AlertType.LowBalance, accountId, ct);

    public Task GenerateSyncFailureAlertAsync(
        Guid userId, string provider, Guid? accountId, string? accountName,
        string? errorCode, CancellationToken ct = default)
    {
        var label = accountName ?? provider;
        var detail = errorCode is null ? string.Empty : $" (error: {errorCode})";

        return EmitAsync(userId, new AlertDraft(
            AlertType.SyncFailure, AlertSeverity.Error, accountId, accountName,
            $"Sync failed for {label}",
            $"We couldn't sync your {provider} account{detail}. Please reconnect or check your credentials."),
            ct);
    }

    public Task ResolveSyncFailureAlertAsync(
        Guid userId, string provider, Guid? accountId, CancellationToken ct = default)
        => ResolveAsync(userId, AlertType.SyncFailure, accountId, ct);

    public Task DeleteAlertsForAccountAsync(Guid accountId, CancellationToken ct = default)
        => _alerts.DeleteByReferenceIdAsync(accountId, ct);

    public Task GenerateThesisBreakAlertAsync(
        Guid userId, Guid thesisId, string ticker, string reason, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.ThesisBroken, AlertSeverity.Warning, thesisId, ticker,
            $"Thesis broken: {ticker}",
            $"Your investment thesis on {ticker} appears broken: {reason}"),
            ct);

    public Task ResolveThesisBreakAlertAsync(
        Guid userId, Guid thesisId, CancellationToken ct = default)
        => ResolveAsync(userId, AlertType.ThesisBroken, thesisId, ct);

    public Task GenerateMarketStructureAlertAsync(
        Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.MarketStructure, AlertSeverity.Warning, referenceId, ticker,
            $"Unusual move: {ticker}",
            $"Market structure flagged {ticker}: {reason}"),
            ct);

    public Task GenerateMarketStructureFreshnessAlertAsync(
        Guid userId, Guid referenceId, string reason, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.MarketStructure, AlertSeverity.Error, referenceId, "freshness",
            "Radar data is stale",
            $"Market-structure data may be unreliable: {reason}")
        {
            SilenceWindow = MarketStructureFreshnessSilenceWindow,
        },
            ct);

    public Task GeneratePolicyViolationAlertAsync(
        Guid userId, string ruleKey, string subject, decimal observedValue, decimal limitValue,
        bool isOverride = false, CancellationToken ct = default)
    {
        var title = isOverride ? $"Risk rule override: {subject}" : $"Policy violation: {subject}";
        var message = isOverride
            ? $"{ruleKey} would have refused {subject} (observed {observedValue}, limit {limitValue}) — proceeded via explicit override."
            : $"{ruleKey} breached for {subject}: observed {observedValue}, limit {limitValue}.";

        return EmitAsync(userId, new AlertDraft(
            AlertType.PolicyViolation,
            isOverride ? AlertSeverity.Info : AlertSeverity.Warning,
            ViolationReferenceId(ruleKey, subject), subject, title, message)
        {
            // An override is a deliberate act by the user: it is recorded every time, never
            // swallowed by an alert the same rule raised earlier.
            Dedup = isOverride ? Dedup.Always : Dedup.ActiveThenSilence,
        },
            ct);
    }

    public Task ResolvePolicyViolationAlertAsync(
        Guid userId, string ruleKey, string subject, CancellationToken ct = default)
        => ResolveAsync(userId, AlertType.PolicyViolation, ViolationReferenceId(ruleKey, subject), ct);

    public Task GenerateOpportunityAlertAsync(
        Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.Opportunity, AlertSeverity.Info, referenceId, ticker,
            $"Top-tier candidate: {ticker}",
            $"{ticker} scored top-tier on conviction scoring: {reason}"),
            ct);

    public Task GenerateConsentExpiringAlertAsync(
        Guid userId, Guid referenceId, string providerName, DateTime expiresAt, CancellationToken ct = default)
    {
        var days = Math.Max(0, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalDays));
        var window = days == 0 ? "today" : days == 1 ? "in 1 day" : $"in {days} days";

        return EmitAsync(userId, new AlertDraft(
            AlertType.ConsentExpiring, AlertSeverity.Warning, referenceId, providerName,
            $"{providerName} bank connection expires {window}",
            $"Your {providerName} open-banking consent expires {window} ({expiresAt:yyyy-MM-dd}). Reconnect it to keep balances and transactions syncing."),
            ct);
    }

    public Task GenerateJobFailureAlertAsync(
        Guid userId, Guid referenceId, string jobName, int consecutiveCount, string? lastError,
        CancellationToken ct = default)
    {
        var detail = string.IsNullOrWhiteSpace(lastError) ? string.Empty : $" Last error: {lastError}";

        return EmitAsync(userId, new AlertDraft(
            AlertType.JobFailure, AlertSeverity.Error, referenceId, jobName,
            $"Job failing: {jobName}",
            $"Scheduled job '{jobName}' has failed {consecutiveCount} times in a row.{detail}")
        {
            // Each streak should produce a fresh alert row (a fresh Telegram message); the failure
            // filter guarantees one call per streak, so an open alert must not suppress the next.
            Dedup = Dedup.SilenceOnly,
        },
            ct);
    }

    public Task GeneratePerformanceBriefAlertAsync(
        Guid userId, string headline, string body, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.PerformanceBrief, AlertSeverity.Info, null, "weekly", headline, body)
        {
            // Every week's brief is its own row — an unread one must not swallow the next.
            Dedup = Dedup.SilenceOnly,
        },
            ct);

    public Task GenerateCashShortfallAlertAsync(
        Guid userId, Guid accountId, string accountName,
        DateOnly shortfallDate, decimal shortfallAmount, string currency,
        CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.CashShortfall, AlertSeverity.Warning, accountId, accountName,
            $"Cash shortfall projected: {accountName}",
            $"{accountName} is projected to run short by {shortfallAmount:F2} {currency} on {shortfallDate:yyyy-MM-dd} based on your upcoming recurring payments."),
            ct);

    public Task ResolveCashShortfallAlertAsync(
        Guid userId, Guid accountId, CancellationToken ct = default)
        => ResolveAsync(userId, AlertType.CashShortfall, accountId, ct);

    public Task GeneratePriceHikeAlertAsync(
        Guid userId, Guid subscriptionId, string merchantName,
        decimal baselineAmount, decimal currentAmount, string currency,
        CancellationToken ct = default)
    {
        var hikePct = (int)Math.Round((currentAmount - baselineAmount) / baselineAmount * 100);

        return EmitAsync(userId, new AlertDraft(
            AlertType.PriceHike, AlertSeverity.Warning, subscriptionId, merchantName,
            $"Price hike: {merchantName}",
            $"{merchantName} now charges {currentAmount:F2} {currency} (+{hikePct}% above the {baselineAmount:F2} {currency} baseline)."),
            ct);
    }

    public Task GenerateDuplicateChargeAlertAsync(
        Guid userId, Guid accountId, string merchantKey, string merchantName,
        decimal chargeAmount, string currency, int chargeCount, CancellationToken ct = default)
        // Both the reference and the label are the normalized key: the label is what the silence
        // window matches on, and the raw name is not stable across runs (a group spelled two ways
        // has no majority spelling). The user reads the raw name in the title and message.
        => EmitAsync(userId, new AlertDraft(
            AlertType.DuplicateCharge, AlertSeverity.Warning,
            DuplicateChargeReferenceId(accountId, merchantKey, chargeAmount), merchantKey,
            $"Possible duplicate charge: {merchantName}",
            $"Charged {chargeCount}× for {chargeAmount:F2} {currency} at {merchantName} within the detection window."),
            ct);

    public Task GenerateCategorySpikeAlertAsync(
        Guid userId, string category, decimal currentMonthSpend, decimal baselineSpend,
        CancellationToken ct = default)
    {
        var spikePct = (int)Math.Round((currentMonthSpend - baselineSpend) / baselineSpend * 100);

        return EmitAsync(userId, new AlertDraft(
            AlertType.CategorySpike, AlertSeverity.Warning,
            CategorySpikeReferenceId(userId, category), category,
            $"Spending spike: {category}",
            $"Your {category} spend this month ({currentMonthSpend:F2}) is +{spikePct}% above the 6-month baseline ({baselineSpend:F2})."),
            ct);
    }

    public Task GenerateFxSpreadAlertAsync(
        Guid userId, Guid debitTransactionId, string fromCurrency, string toCurrency,
        decimal impliedRate, decimal marketRate, CancellationToken ct = default)
    {
        var spreadPct = (int)Math.Round((marketRate - impliedRate) / marketRate * 100);
        var pair = $"{fromCurrency}/{toCurrency}";

        // Dedup key per spec 044/US4: the debit leg uniquely identifies one concrete conversion, so
        // re-runs never re-alert on the same pair while a fresh costly conversion gets its own alert.
        return EmitAsync(userId, new AlertDraft(
            AlertType.FxSpread, AlertSeverity.Warning, debitTransactionId, pair,
            $"FX spread alert: {fromCurrency}→{toCurrency}",
            $"Your {fromCurrency}→{toCurrency} conversion rate ({impliedRate:F4}) was {spreadPct}% below the market rate ({marketRate:F4}). Consider a different routing path."),
            ct);
    }

    public Task GenerateRebalanceProposalAlertAsync(
        Guid userId, int orderCount, string orderSummary, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.RebalanceProposal, AlertSeverity.Warning,
            RebalancePortfolioReferenceId(userId), "portfolio",
            $"Rebalance proposal: {orderCount} order(s)", orderSummary),
            ct);

    public Task GenerateCashSweepProposalAlertAsync(
        Guid userId, decimal idleCashUsd, decimal minBufferUsd, decimal excessUsd, CancellationToken ct = default)
        => EmitAsync(userId, new AlertDraft(
            AlertType.CashSweepProposal, AlertSeverity.Warning,
            CashSweepReferenceId(userId), "cash",
            $"Idle cash exceeds buffer: deploy ≈ ${excessUsd:N0}",
            $"Idle cash ${idleCashUsd:N0} exceeds your minimum buffer ${minBufferUsd:N0}. Consider deploying the ≈ ${excessUsd:N0} excess into your IPS sleeves."),
            ct);

    /// <summary>
    /// The one place an alert is written. Every generator funnels through here so the dedup
    /// discipline — open alert on the same reference wins, then the type's silence window — is
    /// stated once instead of once per alert type.
    /// </summary>
    private async Task EmitAsync(Guid userId, AlertDraft draft, CancellationToken ct)
    {
        if (draft.Dedup == Dedup.ActiveThenSilence)
        {
            var existing = await _alerts.FindActiveAsync(userId, draft.Type, draft.ReferenceId, ct);
            if (existing is not null) return;
        }

        if (draft.Dedup != Dedup.Always)
        {
            var window = draft.SilenceWindow ?? SilenceWindows[draft.Type];
            var quietSince = DateTimeOffset.UtcNow - window;
            if (await _alerts.HasRecentAsync(
                    userId, draft.Type, draft.ReferenceId, draft.ReferenceLabel, quietSince, ct))
                return;
        }

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = draft.Type,
            Severity = draft.Severity,
            Title = draft.Title,
            Message = draft.Message,
            ReferenceId = draft.ReferenceId,
            ReferenceLabel = draft.ReferenceLabel,
        }, ct);
    }

    private async Task ResolveAsync(Guid userId, string type, Guid? referenceId, CancellationToken ct)
    {
        var existing = await _alerts.FindActiveAsync(userId, type, referenceId, ct);
        if (existing is null) return;
        await _alerts.ResolveAsync(existing.Id, ct);
    }

    /// <summary>Deterministic pseudo-GUID from (ruleKey, subject) so find/resolve are stable across runs.</summary>
    private static Guid ViolationReferenceId(string ruleKey, string subject)
        => DerivedReferenceId($"{ruleKey}:{subject.ToUpperInvariant()}");

    private static Guid DuplicateChargeReferenceId(Guid accountId, string merchantKey, decimal amount)
        => DerivedReferenceId($"dupcharge:{accountId:N}:{merchantKey.ToUpperInvariant()}:{amount:F2}");

    private static Guid CategorySpikeReferenceId(Guid userId, string category)
        => DerivedReferenceId($"catspike:{userId:N}:{category.ToUpperInvariant()}");

    /// <summary>Stable per-user synthetic GUID for portfolio rebalance dedup (no natural entity reference).</summary>
    private static Guid RebalancePortfolioReferenceId(Guid userId)
        => DerivedReferenceId($"rebalance:portfolio:{userId}");

    /// <summary>Stable per-user synthetic GUID for cash-sweep dedup.</summary>
    private static Guid CashSweepReferenceId(Guid userId)
        => DerivedReferenceId($"cash:sweep:{userId}");

    /// <summary>
    /// A synthetic reference for alerts with no natural entity id. Not a security primitive — MD5
    /// is used only because it yields exactly the 16 bytes a GUID needs, and the same seed must
    /// keep producing the same reference across releases for find/resolve to stay stable.
    /// </summary>
    private static Guid DerivedReferenceId(string seed)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));

    private sealed record AlertDraft(
        string Type,
        string Severity,
        Guid? ReferenceId,
        string? ReferenceLabel,
        string Title,
        string Message)
    {
        public Dedup Dedup { get; init; } = Dedup.ActiveThenSilence;

        /// <summary>Overrides the type's declared window where one type carries two distinct events.</summary>
        public TimeSpan? SilenceWindow { get; init; }
    }
}
