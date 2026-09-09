namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class SubscriptionDetectionJob(
    BankSyncDbContext db,
    ISubscriptionDetectionResultService resultService,
    ILogger<SubscriptionDetectionJob> logger)
{
    private const int LookbackMonths = 13;
    // Three consistent monthly charges is enough signal — newer open-banking connections
    // often only have a few months of history, so requiring 4 hid real subscriptions
    // (Netcup, OpenAI, Claude) whose window only holds 3 charges.
    private const int MinOccurrences = 3;
    private const double MaxAmountCv = 0.10;
    private const int MonthlyMinDays = 28;
    private const int MonthlyMaxDays = 35;
    private const int AnnualMinDays = 351;
    private const int AnnualMaxDays = 379;
    // A price change (plan upgrade, VAT shift) moves the charge amount in one step;
    // adjacent amounts within this relative distance chain into the same cluster,
    // while a different plan (e.g. Claude Pro €22 vs Max €110) breaks the chain.
    private const double AmountClusterTolerance = 0.15;
    // Repricing at most doubles (or halves) a charge — a promotional year ending, a VAT
    // shift, an inflation pass-through. A larger jump is a different plan (Claude Pro €22
    // → Max €110), not one service at a new price, so it must not become a hike baseline.
    private const double MaxPriceStepRatio = 2.0;
    // One charge is not a price. A prorated or promotional first month sits within the step
    // ratio and has a coefficient of variation of zero by construction, so accepting a
    // single displaced charge as the baseline would alert on the merchant's own onboarding.
    private const int MinBaselineCharges = 2;

    private static readonly string[] UnidentifiableNormalizedNames =
    [
        "unknown",
        "transfer",
        "top-up",
        "topup",
        "recharge",
        "withdrawal",
        "atm",
        "cash",
    ];

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await ProcessAccountsAsync(ct);
    }

    public sealed record TxRow(
        Guid UserId, string? MerchantName, string? Description, decimal Amount,
        DateTime TransactionDate, string? MerchantCategory, int? Mcc, string? Currency);

    private async Task ProcessAccountsAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-LookbackMonths);

        var transactions = await db.Transactions
            .AsNoTracking()
            .Where(t => t.IsActive
                     && !t.IsPending
                     && t.Amount != 0m   // skip €0.00 auth holds / reversals that skew amount stability
                     && t.TransactionDate >= cutoff
                     && (t.TransactionType == null || t.TransactionType == "debit"))
            .Join(db.BankAccounts.Where(a => a.IsActive),
                t => t.AccountId, a => a.Id, (t, a) => new TxRow(
                    t.UserId, t.MerchantName, t.Description, t.Amount,
                    t.TransactionDate, t.MerchantCategory, t.Mcc, a.Currency))
            .ToListAsync(ct);

        foreach (var userGroup in transactions.GroupBy(t => t.UserId))
        {
            var userId = userGroup.Key.ToString();

            try
            {
                var txs = userGroup.ToList();
                var installmentTxs = txs.Where(t => InstallmentPlanRecognizer.IsInstallmentTransaction(t.Description, t.Mcc)).ToList();
                var regularTxs = txs.Where(t => !InstallmentPlanRecognizer.IsInstallmentTransaction(t.Description, t.Mcc)).ToList();

                var results = new List<DetectedSubscriptionData>();
                results.AddRange(DetectSubscriptions(regularTxs));
                results.AddRange(DetectInstallments(installmentTxs));

                await resultService.UpsertDetectedSubscriptionsAsync(userId, results, ct);
                await resultService.MarkStaleAsPotentiallyCancelledAsync(userId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heuristic subscription detection failed for user {UserId}", userId);
            }
        }
    }

    // Recurring-service detection: group by merchant, price the merchant off the amount
    // cluster holding its most recent charge (so a discontinued plan's old price — e.g.
    // Claude Pro €22 next to Max €110 — can't poison the stability gates) plus, if that
    // price has just replaced another, the one it displaced. Then require a consistent
    // monthly/annual cadence over the charges, a stable current amount, and at least
    // MinOccurrences charges. A recurring transfer to a masked card number is a
    // loan/mortgage repayment, not a service — it's emitted as an installment so it stays
    // out of the spend summary.
    public static IEnumerable<DetectedSubscriptionData> DetectSubscriptions(IReadOnlyList<TxRow> transactions)
    {
        var results = new List<DetectedSubscriptionData>();
        var byMerchant = transactions.GroupBy(NormalizeForDetection);

        foreach (var merchantGroup in byMerchant)
        {
            var normalized = merchantGroup.Key;
            var series = SplitAtPriceStep(merchantGroup);
            var sorted = series.Current.Concat(series.Displaced).OrderBy(t => t.TransactionDate).ToList();

            if (sorted.Count < MinOccurrences) continue;
            if (IsUnidentifiableMerchant(normalized)) continue;

            var dates = sorted.Select(t => t.TransactionDate).ToList();
            var intervals = new List<int>();
            for (var i = 1; i < dates.Count; i++)
                intervals.Add((int)(dates[i] - dates[i - 1]).TotalDays);

            if (intervals.Count == 0) continue;

            var median = Median(intervals);

            string cadence;
            if (median >= MonthlyMinDays && median <= MonthlyMaxDays)
                cadence = "monthly";
            else if (median >= AnnualMinDays && median <= AnnualMaxDays)
                cadence = "annual";
            else
                continue;

            // Stability is judged on the current price alone — a displaced pre-hike cluster
            // is a different price by construction and would always fail the CV gate.
            var mean = series.Current.Average(t => (double)t.Amount);
            if (mean <= 0) continue;
            if (CoefficientOfVariation(series.Current, mean) > MaxAmountCv) continue;

            var lastTransaction = sorted.Last();
            var lastChargeDate = DateOnly.FromDateTime(lastTransaction.TransactionDate);
            var nextExpectedDate = lastChargeDate.AddDays((int)median);

            var displayName = normalized.StartsWith("mobile top-up ", StringComparison.Ordinal)
                ? $"Mobile top-up {normalized[^4..]}"
                : MerchantNameNormalizer.GetDisplayName(sorted.Select(t => t.MerchantName ?? t.Description));
            var topCategory = sorted
                .Select(t => t.MerchantCategory)
                .Where(c => c != null)
                .GroupBy(c => c)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;

            results.Add(new DetectedSubscriptionData(
                MerchantNameNormalized: normalized,
                MerchantNameDisplay: displayName,
                Cadence: cadence,
                AverageAmount: (decimal)mean,
                LastKnownAmount: lastTransaction.Amount,
                PreviousAmount: series.Displaced.Count == 0
                    ? null
                    : Math.Round(series.Displaced.Average(t => t.Amount), 2),
                Currency: lastTransaction.Currency ?? "USD",
                LastChargeDate: lastChargeDate,
                NextExpectedDate: nextExpectedDate,
                OccurrenceCount: sorted.Count,
                ConfidenceScore: sorted.Count,
                Category: topCategory,
                Kind: MaskedPan.IsLikely(normalized)
                    ? SubscriptionKinds.Installment
                    : SubscriptionKinds.Subscription));
        }

        return results;
    }

    /// <summary>Merchant key for recurring-service grouping.</summary>
    public static string NormalizeForDetection(TxRow transaction) =>
        MerchantNameNormalizer.NormalizeDetectionKey(transaction.MerchantName, transaction.Description);

    /// <summary>
    /// A merchant's charges at its current price, plus the charges at the price that one
    /// displaced — empty when the merchant has only ever billed one price, or when the
    /// other amounts belong to a different plan rather than an earlier price of this one.
    /// </summary>
    public sealed record PriceSeries(IReadOnlyList<TxRow> Current, IReadOnlyList<TxRow> Displaced);

    /// <summary>
    /// Splits a merchant's charges into amount clusters (adjacent sorted amounts within
    /// <see cref="AmountClusterTolerance"/> chain together), takes the cluster holding the
    /// most recent charge — the current price of whatever is still being billed — and, while
    /// that price is still new, hands back the price it replaced alongside it.
    ///
    /// Without the second half, a real single-step hike is invisible: it exceeds the cluster
    /// tolerance, so the new price starts its own cluster of one, falls under
    /// <see cref="MinOccurrences"/>, and the subscription drops off the list the very month
    /// it goes up. The displaced cluster restores both the occurrence evidence and the
    /// pre-hike baseline the price-hike sentinel compares against.
    ///
    /// It is only a repricing when the merchant billed exactly two prices — a third cluster
    /// means an outlier or a third plan, and picking the nearest of several would let one
    /// stray charge shadow the price actually being replaced — and when those two form a
    /// clean chronological step (concurrent plans interleave in time), the step is within
    /// <see cref="MaxPriceStepRatio"/>, and the old price was billed at least
    /// <see cref="MinBaselineCharges"/> times and was itself stable. Once the new price has
    /// <see cref="MinOccurrences"/> charges of its own it stands alone and the old price
    /// stops being news.
    /// </summary>
    public static PriceSeries SplitAtPriceStep(IEnumerable<TxRow> transactions)
    {
        var byAmount = transactions.OrderBy(t => t.Amount).ToList();
        if (byAmount.Count == 0) return new PriceSeries(byAmount, []);

        var clusters = new List<List<TxRow>> { new() { byAmount[0] } };
        for (var i = 1; i < byAmount.Count; i++)
        {
            var previous = (double)byAmount[i - 1].Amount;
            var current = (double)byAmount[i].Amount;
            if (previous > 0 && (current - previous) / previous <= AmountClusterTolerance)
                clusters[^1].Add(byAmount[i]);
            else
                clusters.Add([byAmount[i]]);
        }

        var currentCluster = clusters.MaxBy(c => c.Max(t => t.TransactionDate))!;
        var noStep = new PriceSeries(currentCluster, []);
        if (currentCluster.Count >= MinOccurrences || clusters.Count != 2) return noStep;

        var displaced = clusters.Single(c => c != currentCluster);
        return IsRepricing(currentCluster, displaced) ? new PriceSeries(currentCluster, displaced) : noStep;
    }

    private static bool IsRepricing(IReadOnlyList<TxRow> current, IReadOnlyList<TxRow> displaced)
    {
        if (displaced.Count < MinBaselineCharges) return false;
        if (displaced.Max(t => t.TransactionDate) >= current.Min(t => t.TransactionDate)) return false;

        var before = displaced.Average(t => (double)t.Amount);
        var after = current.Average(t => (double)t.Amount);
        if (before <= 0 || after <= 0) return false;

        var ratio = after / before;
        if (ratio > MaxPriceStepRatio || ratio < 1 / MaxPriceStepRatio) return false;

        return CoefficientOfVariation(displaced, before) <= MaxAmountCv;
    }

    /// <summary>Spread of a cluster's amounts around a mean its caller has already proven positive.</summary>
    private static double CoefficientOfVariation(IReadOnlyList<TxRow> transactions, double mean)
    {
        var variance = transactions.Sum(t => Math.Pow((double)t.Amount - mean, 2)) / transactions.Count;
        return Math.Sqrt(variance) / mean;
    }

    // Installment detection: one plan per (merchant, rounded monthly amount) — the same
    // shop can carry several concurrent розстрочки (e.g. two Алло plans at ₴2,339.95 and
    // ₴2,999.95) and merchant-only grouping merges them into one row with polluted stats.
    // No cadence/CV/min-count gates — a single "- monomarket" repayment is a real
    // installment. A full payoff ("Повне погашення") carries its own amount, so it's
    // matched by merchant: it completes every plan with no payments after it, while a
    // plan that keeps charging past the payoff date is a separate, still-active plan.
    public static IEnumerable<DetectedSubscriptionData> DetectInstallments(IReadOnlyList<TxRow> transactions)
    {
        var results = new List<DetectedSubscriptionData>();

        var payoffDatesByMerchant = transactions
            .Where(t => InstallmentPlanRecognizer.IsInstallmentPayoff(t.Description))
            .GroupBy(t => InstallmentPlanRecognizer.ExtractMerchant(t.Description ?? string.Empty))
            .ToDictionary(g => g.Key, g => g.Max(t => t.TransactionDate));

        var byPlan = transactions
            .Where(t => !InstallmentPlanRecognizer.IsInstallmentPayoff(t.Description))
            .GroupBy(t => (
                Merchant: InstallmentPlanRecognizer.ExtractMerchant(t.Description ?? string.Empty),
                Amount: InstallmentPlanRecognizer.RoundPlanAmount(t.Amount)));

        foreach (var group in byPlan)
        {
            var (merchant, roundedAmount) = group.Key;
            if (string.IsNullOrWhiteSpace(merchant)) continue;

            var payments = group.OrderBy(t => t.TransactionDate).ToList();
            var lastPayment = payments[^1];
            var lastPaymentDate = lastPayment.TransactionDate;

            var completed = payoffDatesByMerchant.TryGetValue(merchant, out var payoff)
                && payoff >= lastPaymentDate;

            var lastChargeDate = DateOnly.FromDateTime(lastPaymentDate);

            results.Add(new DetectedSubscriptionData(
                MerchantNameNormalized: InstallmentPlanRecognizer.PlanKey(merchant, roundedAmount),
                MerchantNameDisplay: merchant,
                Cadence: "monthly",
                AverageAmount: Math.Round(payments.Average(t => t.Amount), 2),
                LastKnownAmount: lastPayment.Amount,
                Currency: lastPayment.Currency ?? "USD",
                LastChargeDate: lastChargeDate,
                NextExpectedDate: lastChargeDate.AddMonths(1),
                OccurrenceCount: payments.Count,
                ConfidenceScore: 100,
                Category: null,
                Kind: SubscriptionKinds.Installment,
                IsCompleted: completed));
        }

        return results;
    }

    public static bool IsUnidentifiableMerchant(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return true;

        // Keys produced by NormalizeForDetection's mobile top-up special case are
        // deliberately identifiable despite containing "top-up".
        if (normalized.StartsWith("mobile top-up ", StringComparison.Ordinal)) return false;

        foreach (var marker in UnidentifiableNormalizedNames)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static double Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }
}
