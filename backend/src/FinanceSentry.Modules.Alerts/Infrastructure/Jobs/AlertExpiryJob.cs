namespace FinanceSentry.Modules.Alerts.Infrastructure.Jobs;

using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily sweep that resolves open point-event alerts once they age past their type's TTL, measured
/// from <see cref="Alert.CreatedAt"/> (finance-sentry#419 S4, design report §3.2). It sets the same
/// resolved state the user's "Accept" acknowledgement produces, so it feeds the existing 90-day purge
/// (<see cref="AlertPurgeJob"/>) unchanged. It resolves, never deletes.
///
/// Types not in <see cref="Ttls"/> never expire — they either have a working resolve-on-cleared-condition
/// path (§3.1: <c>CashShortfall</c>, <c>LowBalance</c>, <c>ThesisBroken</c>, <c>PolicyViolation</c>) or are
/// Error-severity operational alerts that must be cleared by the condition or the user, never by age
/// (<c>SyncFailure</c>, <c>JobFailure</c>, the freshness flavour of <c>MarketStructure</c>).
/// </summary>
public sealed class AlertExpiryJob(
    IAlertRepository alerts,
    TimeProvider clock,
    ILogger<AlertExpiryJob> logger)
{
    /// <summary>
    /// ReferenceLabel the freshness flavour of <see cref="AlertType.MarketStructure"/> is generated
    /// with (<c>AlertGeneratorService.GenerateMarketStructureFreshnessAlertAsync</c>) — the way that
    /// producer already distinguishes itself from a ticker-move alert of the same type.
    /// </summary>
    private const string MarketStructureFreshnessLabel = "freshness";

    /// <summary>
    /// Rule of thumb for future point-event types: Info-severity expires in 14 days, Warning in 30,
    /// Error never. A type with an observable clear gets a resolve-on-cleared-condition path (§3.1)
    /// and uses this table only as a floor. TTLs below are the design report's table as written;
    /// everything not listed here never expires.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, TimeSpan> Ttls = new Dictionary<string, TimeSpan>
    {
        [AlertType.NewsCluster] = TimeSpan.FromDays(3),
        [AlertType.FilingLanded] = TimeSpan.FromDays(14),
        [AlertType.MarketStructure] = TimeSpan.FromDays(7), // ticker-move flavour only; see GetExpiresAt
        [AlertType.FxSpread] = TimeSpan.FromDays(30),
        [AlertType.DuplicateCharge] = TimeSpan.FromDays(30),
        [AlertType.PriceHike] = TimeSpan.FromDays(60),
        [AlertType.PerformanceBrief] = TimeSpan.FromDays(14),
        [AlertType.EarningsAhead] = TimeSpan.FromDays(2), // floor; the exact event date already resolves it (§3.1)
    };

    /// <summary>
    /// <see cref="AlertType.BudgetBreach"/> is month-scoped by construction (reference key carries
    /// {yyyy}-{MM}) and expires at the end of the month <em>following</em> the one it was raised in,
    /// not a fixed duration from <see cref="Alert.CreatedAt"/>.
    /// </summary>
    private static readonly IReadOnlyCollection<string> ExpirableTypes =
        [.. Ttls.Keys, AlertType.BudgetBreach];

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var candidates = await alerts.GetOpenAlertsByTypesAsync(ExpirableTypes, ct);
        var now = clock.GetUtcNow();
        var expiredCount = 0;

        foreach (var alert in candidates)
        {
            if (alert.IsResolved) continue;

            var expiresAt = GetExpiresAt(alert);
            if (expiresAt is null || now < expiresAt.Value) continue;

            await alerts.ResolveAsync(alert.Id, ct);
            expiredCount++;
        }

        logger.LogInformation("AlertExpiryJob resolved {Count} alerts past their TTL", expiredCount);
    }

    private static DateTimeOffset? GetExpiresAt(Alert alert)
    {
        if (alert.Type == AlertType.MarketStructure)
        {
            return alert.ReferenceLabel == MarketStructureFreshnessLabel
                ? null
                : alert.CreatedAt + Ttls[AlertType.MarketStructure];
        }

        if (alert.Type == AlertType.BudgetBreach)
        {
            return EndOfFollowingMonth(alert.CreatedAt);
        }

        return Ttls.TryGetValue(alert.Type, out var ttl) ? alert.CreatedAt + ttl : null;
    }

    private static DateTimeOffset EndOfFollowingMonth(DateTimeOffset createdAt)
    {
        var startOfCreationMonth = new DateTimeOffset(createdAt.Year, createdAt.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return startOfCreationMonth.AddMonths(2);
    }
}
