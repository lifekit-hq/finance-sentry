namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily tick that feeds <see cref="SubscriptionDetectionAlgorithm"/>: read each user's posted
/// debits with their account currency, hand them to the algorithm, persist what it detected.
/// The rules for what counts as a subscription live in the algorithm, not here.
/// </summary>
public sealed class SubscriptionDetectionJob(
    BankSyncDbContext db,
    ISubscriptionDetectionResultService resultService,
    ILogger<SubscriptionDetectionJob> logger)
{
    // How far back a charge can be and still be evidence. Bounds the algorithm's reach: two prior
    // charges at an annual subscription's old price do not fit in it, which is why annual
    // repricing cannot produce a hike baseline.
    private const int LookbackMonths = 13;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        await ProcessAccountsAsync(ct);
    }

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
                t => t.AccountId, a => a.Id, (t, a) => new SubscriptionDetectionAlgorithm.TxRow(
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
                results.AddRange(SubscriptionDetectionAlgorithm.DetectSubscriptions(regularTxs));
                results.AddRange(SubscriptionDetectionAlgorithm.DetectInstallments(installmentTxs));

                await resultService.UpsertDetectedSubscriptionsAsync(userId, results, ct);
                await resultService.MarkStaleAsPotentiallyCancelledAsync(userId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Heuristic subscription detection failed for user {UserId}", userId);
            }
        }
    }
}
