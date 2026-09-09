namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily sentinel (044/US1): fires a PriceHike alert when a recurring subscription or installment's
/// most recent charge is significantly above the price it used to be billed at. Reads
/// detected_subscriptions (014) via ISubscriptionHygieneSummaryReader so recurrence is never
/// re-derived — including the pre-step baseline detection records when a merchant reprices.
/// </summary>
public sealed class PriceHikeDetectionJob(
    ISubscriptionHygieneSummaryReader subscriptions,
    IAlertGeneratorService alerts,
    IConfiguration config,
    ILogger<PriceHikeDetectionJob> logger)
{
    private const decimal DefaultPriceHikeThreshold = 0.15m;
    private const int MinOccurrences = 3;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var threshold = config.GetValue("HygieneSentinels:PriceHikeThreshold", DefaultPriceHikeThreshold);

        IReadOnlyList<SubscriptionHygieneSummary> all;
        try
        {
            all = await subscriptions.GetAllActiveAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PriceHikeDetectionJob: failed to read subscription summaries");
            return;
        }

        foreach (var sub in all)
        {
            if (sub.OccurrenceCount < MinOccurrences) continue;

            var baseline = sub.HikeBaseline;
            if (baseline <= 0) continue;

            var hikeFraction = (sub.LastKnownAmount - baseline) / baseline;
            if (hikeFraction <= threshold) continue;

            try
            {
                await alerts.GeneratePriceHikeAlertAsync(
                    sub.UserId, sub.Id, sub.MerchantNameDisplay,
                    baseline, sub.LastKnownAmount, sub.Currency, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "PriceHikeDetectionJob: alert failed for user {UserId} merchant {Merchant}",
                    sub.UserId, sub.MerchantNameDisplay);
            }
        }
    }
}
