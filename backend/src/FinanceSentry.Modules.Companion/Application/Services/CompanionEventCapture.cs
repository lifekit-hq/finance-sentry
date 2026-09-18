namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class CompanionEventCapture(
    IMaterialAlertReader alerts,
    IAnalystActionFeedReader analystActions,
    IBrokerageHoldingsReader holdings,
    IBankingTotalsReader banking,
    IBankingAccountsReader accounts,
    INotificationSettingRepository settings,
    ICompanionEventRepository events,
    ICompanionCaptureStateRepository captureState,
    IMaterialityPolicy policy,
    IOptions<CompanionOptions> options,
    ILogger<CompanionEventCapture> logger) : ICompanionEventCapture
{
    private const string AlertSource = "alerts";
    private const string AnalystSource = "analyst-actions";
    private const int BatchLimit = 200;

    private readonly CompanionOptions _options = options.Value;

    public async Task<int> CaptureAsync(CancellationToken ct = default)
    {
        var written = await CaptureAlertsAsync(ct);
        written += await CaptureAnalystActionsAsync(ct);
        if (written > 0)
        {
            logger.LogInformation("Companion capture wrote {Count} new events", written);
        }

        return written;
    }

    private async Task<int> CaptureAlertsAsync(CancellationToken ct)
    {
        var floor = DateTimeOffset.UtcNow.AddMinutes(-_options.InitialLookbackMinutes);
        var watermark = await captureState.GetWatermarkAsync(AlertSource, floor, ct);
        var rows = await alerts.GetNewSinceAsync(watermark, BatchLimit, ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        var written = 0;
        var maxSeen = watermark;
        var accountsByUser = new Dictionary<Guid, IReadOnlyList<BankingAccountSummary>>();
        foreach (var a in rows)
        {
            maxSeen = a.CreatedAt > maxSeen ? a.CreatedAt : maxSeen;
            var kind = policy.ClassifyAlert(a.Type);
            if (kind is null)
            {
                continue;
            }

            var mode = (await settings.GetOrDefaultAsync(a.UserId, ct)).Mode;
            var staleness = kind == CompanionEventKind.SyncFailure
                ? await SourceStalenessAsync(a, accountsByUser, ct)
                : null;
            var evt = new CompanionEvent
            {
                UserId = a.UserId,
                Kind = kind.Value,
                Subject = Trim(a.ReferenceLabel ?? a.Title, 128),
                Severity = Trim(a.Severity, 16),
                Summary = Trim(a.Title, 500),
                DedupKey = policy.AlertDedupKey(a.AlertId),
                ReferenceId = a.ReferenceId ?? a.AlertId,
                SourceModule = "alerts",
                Disposition = policy.DispositionFor(mode, kind.Value, staleness),
                OccurredAt = a.CreatedAt,
            };

            if (await events.InsertIfNewAsync(evt, ct))
            {
                written++;
            }
        }

        await captureState.SetWatermarkAsync(AlertSource, maxSeen, ct);
        return written;
    }

    private async Task<int> CaptureAnalystActionsAsync(CancellationToken ct)
    {
        var floor = DateTimeOffset.UtcNow.AddMinutes(-_options.InitialLookbackMinutes);
        var watermark = await captureState.GetWatermarkAsync(AnalystSource, floor, ct);
        var rows = await analystActions.GetNewSinceAsync(watermark, BatchLimit, ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        // Held tickers per user — analyst actions are only surfaced on names a user actually holds.
        var userIds = await banking.GetActiveUserIdsAsync(ct);
        var heldByUser = new Dictionary<Guid, HashSet<string>>();
        foreach (var userId in userIds)
        {
            var held = (await holdings.GetHoldingsAsync(userId, ct))
                .Select(h => h.Symbol.Trim().ToUpperInvariant())
                .ToHashSet(StringComparer.Ordinal);
            heldByUser[userId] = held;
        }

        var written = 0;
        var maxSeen = watermark;
        foreach (var a in rows)
        {
            maxSeen = a.IngestedAt > maxSeen ? a.IngestedAt : maxSeen;
            var ticker = a.Ticker.Trim().ToUpperInvariant();

            foreach (var (userId, held) in heldByUser)
            {
                if (!held.Contains(ticker))
                {
                    continue;
                }

                var mode = (await settings.GetOrDefaultAsync(userId, ct)).Mode;
                var target = a.NewTarget is { } t ? $" (target ${t:0.##})" : string.Empty;
                var evt = new CompanionEvent
                {
                    UserId = userId,
                    Kind = CompanionEventKind.AnalystAction,
                    Subject = ticker,
                    Severity = "info",
                    Summary = Trim($"{a.Firm} {a.ActionType} {ticker}{target}", 500),
                    DedupKey = policy.AnalystDedupKey(userId, a.ActionId),
                    ReferenceId = a.ActionId,
                    SourceModule = "research",
                    Disposition = policy.DispositionFor(mode, CompanionEventKind.AnalystAction),
                    OccurredAt = a.IngestedAt,
                };

                if (await events.InsertIfNewAsync(evt, ct))
                {
                    written++;
                }
            }
        }

        await captureState.SetWatermarkAsync(AnalystSource, maxSeen, ct);
        return written;
    }

    /// <summary>
    /// Time since the alert's referenced bank account last synced successfully. Null when the alert
    /// carries no account reference (provider-level sync failures: crypto, brokerage, research feeds)
    /// or the account has never synced — the policy treats null as "not proven stale".
    /// </summary>
    private async Task<TimeSpan?> SourceStalenessAsync(
        MaterialAlertRecord alert,
        Dictionary<Guid, IReadOnlyList<BankingAccountSummary>> accountsByUser,
        CancellationToken ct)
    {
        if (alert.ReferenceId is not { } accountId)
        {
            return null;
        }

        if (!accountsByUser.TryGetValue(alert.UserId, out var summaries))
        {
            summaries = await accounts.GetAccountSummariesAsync(alert.UserId, ct);
            accountsByUser[alert.UserId] = summaries;
        }

        var lastOk = summaries.FirstOrDefault(s => s.AccountId == accountId)?.LastSuccessfulSyncTimestamp;
        if (lastOk is not { } last)
        {
            return null;
        }

        var lastUtc = last.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(last, DateTimeKind.Utc)
            : last.ToUniversalTime();
        return DateTimeOffset.UtcNow - new DateTimeOffset(lastUtc);
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..max];
}
