namespace FinanceSentry.Modules.Alerts.Application.Services;

using System.Security.Cryptography;
using System.Text;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;

public class AlertGeneratorService(IAlertRepository alerts) : IAlertGeneratorService
{
    private static readonly TimeSpan LowBalanceSilenceWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan SyncFailureSilenceWindow = TimeSpan.FromHours(12);
    private static readonly TimeSpan UnusualSpendSilenceWindow = TimeSpan.FromDays(7);
    private static readonly TimeSpan ThesisBrokenSilenceWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan MarketStructureSilenceWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan MarketStructureFreshnessSilenceWindow = TimeSpan.FromHours(12);
    private static readonly TimeSpan PolicyViolationSilenceWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan OpportunitySilenceWindow = TimeSpan.FromHours(24);
    // Reminders repeat only every 3 days while inside the pre-expiry window, so the detector can run
    // daily without spamming the same connection.
    private static readonly TimeSpan ConsentExpiringSilenceWindow = TimeSpan.FromDays(3);
    // Backstop only — the failure filter already guarantees one call per streak. Short enough that a
    // genuine success-then-new-streak re-alerts, long enough to absorb an accidental double-call.
    private static readonly TimeSpan JobFailureSilenceWindow = TimeSpan.FromMinutes(15);
    // 6 days: weekly cron fires each Monday; prevents a re-run or drift from double-alerting.
    private static readonly TimeSpan PerformanceBriefSilenceWindow = TimeSpan.FromDays(6);
    // Active-alert dedup is the primary guard; 24h HasRecent prevents re-alert on the same day after manual resolve.
    private static readonly TimeSpan CashShortfallSilenceWindow = TimeSpan.FromHours(24);

    private readonly IAlertRepository _alerts = alerts;

    public async Task GenerateLowBalanceAlertAsync(
        Guid userId, Guid accountId, string accountName,
        decimal balance, decimal threshold, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.LowBalance, accountId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - LowBalanceSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.LowBalance, accountId, accountName, quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.LowBalance,
            Severity = AlertSeverity.Warning,
            Title = $"Low balance on {accountName}",
            Message = $"Your {accountName} balance ({balance:C}) has dropped below your {threshold:C} threshold.",
            ReferenceId = accountId,
            ReferenceLabel = accountName,
        }, ct);
    }

    public async Task ResolveLowBalanceAlertAsync(
        Guid userId, Guid accountId, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.LowBalance, accountId, ct);
        if (existing is null) return;
        await _alerts.ResolveAsync(existing.Id, ct);
    }

    public async Task GenerateSyncFailureAlertAsync(
        Guid userId, string provider, Guid? accountId, string? accountName,
        string? errorCode, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.SyncFailure, accountId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - SyncFailureSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.SyncFailure, accountId, accountName, quietSince, ct))
            return;

        var label = accountName ?? provider;
        var detail = errorCode is null ? string.Empty : $" (error: {errorCode})";

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.SyncFailure,
            Severity = AlertSeverity.Error,
            Title = $"Sync failed for {label}",
            Message = $"We couldn't sync your {provider} account{detail}. Please reconnect or check your credentials.",
            ReferenceId = accountId,
            ReferenceLabel = accountName,
        }, ct);
    }

    public async Task ResolveSyncFailureAlertAsync(
        Guid userId, string provider, Guid? accountId, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.SyncFailure, accountId, ct);
        if (existing is null) return;
        await _alerts.ResolveAsync(existing.Id, ct);
    }

    public Task DeleteAlertsForAccountAsync(Guid accountId, CancellationToken ct = default)
        => _alerts.DeleteByReferenceIdAsync(accountId, ct);

    public async Task GenerateUnusualSpendAlertAsync(
        Guid userId, string category, decimal currentMonthSpend,
        decimal averageMonthlySpend, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.UnusualSpend, null, ct);
        if (existing is not null && existing.ReferenceLabel == category) return;

        var quietSince = DateTimeOffset.UtcNow - UnusualSpendSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.UnusualSpend, null, category, quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.UnusualSpend,
            Severity = AlertSeverity.Info,
            Title = $"Unusual spend in {category}",
            Message = $"Your {category} spend this month ({currentMonthSpend:C}) is more than 2× your 3-month average ({averageMonthlySpend:C}).",
            ReferenceId = null,
            ReferenceLabel = category,
        }, ct);
    }

    public async Task GenerateThesisBreakAlertAsync(
        Guid userId, Guid thesisId, string ticker, string reason, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.ThesisBroken, thesisId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - ThesisBrokenSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.ThesisBroken, thesisId, ticker, quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.ThesisBroken,
            Severity = AlertSeverity.Warning,
            Title = $"Thesis broken: {ticker}",
            Message = $"Your investment thesis on {ticker} appears broken: {reason}",
            ReferenceId = thesisId,
            ReferenceLabel = ticker,
        }, ct);
    }

    public async Task ResolveThesisBreakAlertAsync(
        Guid userId, Guid thesisId, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.ThesisBroken, thesisId, ct);
        if (existing is null) return;
        await _alerts.ResolveAsync(existing.Id, ct);
    }

    public async Task GenerateMarketStructureAlertAsync(
        Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.MarketStructure, referenceId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - MarketStructureSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.MarketStructure, referenceId, ticker, quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.MarketStructure,
            Severity = AlertSeverity.Warning,
            Title = $"Unusual move: {ticker}",
            Message = $"Market structure flagged {ticker}: {reason}",
            ReferenceId = referenceId,
            ReferenceLabel = ticker,
        }, ct);
    }

    public async Task GenerateMarketStructureFreshnessAlertAsync(
        Guid userId, Guid referenceId, string reason, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.MarketStructure, referenceId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - MarketStructureFreshnessSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.MarketStructure, referenceId, "freshness", quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.MarketStructure,
            Severity = AlertSeverity.Error,
            Title = "Radar data is stale",
            Message = $"Market-structure data may be unreliable: {reason}",
            ReferenceId = referenceId,
            ReferenceLabel = "freshness",
        }, ct);
    }

    public async Task GeneratePolicyViolationAlertAsync(
        Guid userId, string ruleKey, string subject, decimal observedValue, decimal limitValue,
        bool isOverride = false, CancellationToken ct = default)
    {
        var referenceId = ViolationReferenceId(ruleKey, subject);

        if (!isOverride)
        {
            var existing = await _alerts.FindActiveAsync(userId, AlertType.PolicyViolation, referenceId, ct);
            if (existing is not null) return;

            var quietSince = DateTimeOffset.UtcNow - PolicyViolationSilenceWindow;
            if (await _alerts.HasRecentAsync(userId, AlertType.PolicyViolation, referenceId, subject, quietSince, ct))
                return;
        }

        var title = isOverride ? $"Risk rule override: {subject}" : $"Policy violation: {subject}";
        var message = isOverride
            ? $"{ruleKey} would have refused {subject} (observed {observedValue}, limit {limitValue}) — proceeded via explicit override."
            : $"{ruleKey} breached for {subject}: observed {observedValue}, limit {limitValue}.";

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.PolicyViolation,
            Severity = isOverride ? AlertSeverity.Info : AlertSeverity.Warning,
            Title = title,
            Message = message,
            ReferenceId = referenceId,
            ReferenceLabel = subject,
        }, ct);
    }

    public async Task ResolvePolicyViolationAlertAsync(
        Guid userId, string ruleKey, string subject, CancellationToken ct = default)
    {
        var referenceId = ViolationReferenceId(ruleKey, subject);
        var existing = await _alerts.FindActiveAsync(userId, AlertType.PolicyViolation, referenceId, ct);
        if (existing is null) return;
        await _alerts.ResolveAsync(existing.Id, ct);
    }

    public async Task GenerateOpportunityAlertAsync(
        Guid userId, Guid referenceId, string ticker, string reason, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.Opportunity, referenceId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - OpportunitySilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.Opportunity, referenceId, ticker, quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.Opportunity,
            Severity = AlertSeverity.Info,
            Title = $"Top-tier candidate: {ticker}",
            Message = $"{ticker} scored top-tier on conviction scoring: {reason}",
            ReferenceId = referenceId,
            ReferenceLabel = ticker,
        }, ct);
    }

    public async Task GenerateConsentExpiringAlertAsync(
        Guid userId, Guid referenceId, string providerName, DateTime expiresAt, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.ConsentExpiring, referenceId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - ConsentExpiringSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.ConsentExpiring, referenceId, providerName, quietSince, ct))
            return;

        var days = Math.Max(0, (int)Math.Ceiling((expiresAt - DateTime.UtcNow).TotalDays));
        var window = days == 0 ? "today" : days == 1 ? "in 1 day" : $"in {days} days";

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.ConsentExpiring,
            Severity = AlertSeverity.Warning,
            Title = $"{providerName} bank connection expires {window}",
            Message = $"Your {providerName} open-banking consent expires {window} ({expiresAt:yyyy-MM-dd}). Reconnect it to keep balances and transactions syncing.",
            ReferenceId = referenceId,
            ReferenceLabel = providerName,
        }, ct);
    }

    public async Task GenerateJobFailureAlertAsync(
        Guid userId, Guid referenceId, string jobName, int consecutiveCount, string? lastError,
        CancellationToken ct = default)
    {
        // No FindActive gate here: each streak should produce a fresh alert row (a fresh Telegram
        // message). The failure filter guarantees one call per streak; HasRecent is a light backstop.
        var quietSince = DateTimeOffset.UtcNow - JobFailureSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.JobFailure, referenceId, jobName, quietSince, ct))
            return;

        var detail = string.IsNullOrWhiteSpace(lastError) ? string.Empty : $" Last error: {lastError}";

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.JobFailure,
            Severity = AlertSeverity.Error,
            Title = $"Job failing: {jobName}",
            Message = $"Scheduled job '{jobName}' has failed {consecutiveCount} times in a row.{detail}",
            ReferenceId = referenceId,
            ReferenceLabel = jobName,
        }, ct);
    }

    public async Task GeneratePerformanceBriefAlertAsync(
        Guid userId, string headline, string body, CancellationToken ct = default)
    {
        var quietSince = DateTimeOffset.UtcNow - PerformanceBriefSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.PerformanceBrief, null, "weekly", quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.PerformanceBrief,
            Severity = AlertSeverity.Info,
            Title = headline,
            Message = body,
            ReferenceId = null,
            ReferenceLabel = "weekly",
        }, ct);
    }

    public async Task GenerateCashShortfallAlertAsync(
        Guid userId, Guid accountId, string accountName,
        DateOnly shortfallDate, decimal shortfallAmount, string currency,
        CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.CashShortfall, accountId, ct);
        if (existing is not null) return;

        var quietSince = DateTimeOffset.UtcNow - CashShortfallSilenceWindow;
        if (await _alerts.HasRecentAsync(userId, AlertType.CashShortfall, accountId, accountName, quietSince, ct))
            return;

        await _alerts.AddAsync(new Alert
        {
            UserId = userId,
            Type = AlertType.CashShortfall,
            Severity = AlertSeverity.Warning,
            Title = $"Cash shortfall projected: {accountName}",
            Message = $"{accountName} is projected to run short by {shortfallAmount:F2} {currency} on {shortfallDate:yyyy-MM-dd} based on your upcoming recurring payments.",
            ReferenceId = accountId,
            ReferenceLabel = accountName,
        }, ct);
    }

    public async Task ResolveCashShortfallAlertAsync(
        Guid userId, Guid accountId, CancellationToken ct = default)
    {
        var existing = await _alerts.FindActiveAsync(userId, AlertType.CashShortfall, accountId, ct);
        if (existing is null) return;
        await _alerts.ResolveAsync(existing.Id, ct);
    }

    /// <summary>Deterministic pseudo-GUID from (ruleKey, subject) so find/resolve are stable across runs.</summary>
    private static Guid ViolationReferenceId(string ruleKey, string subject)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{ruleKey}:{subject.ToUpperInvariant()}"));
        return new Guid(bytes);
    }
}
