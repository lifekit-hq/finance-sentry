namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Daily sentinel (044/US2): fires a DuplicateCharge alert when the same merchant charges the same
/// amount more than once within the configured window on the same account. Reads transactions directly
/// from BankSyncDbContext — no external feed needed. Only debits count: a charge + its refund at the
/// same merchant/amount is a round-trip, not a duplicate. Merchant names are normalized
/// (via <see cref="MerchantNameNormalizer"/>) before grouping so case/whitespace/suffix variants of
/// the same merchant still group together; the alert itself names the merchant the way the statement
/// spelled it. Charges the normalizer cannot name are skipped rather than fused under its sentinel key.
/// </summary>
public sealed class DuplicateChargeDetectionJob(
    BankSyncDbContext db,
    IAlertGeneratorService alerts,
    IConfiguration config,
    ILogger<DuplicateChargeDetectionJob> logger)
{
    private const int DefaultDuplicateWindowDays = 5;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var windowDays = config.GetValue("HygieneSentinels:DuplicateWindowDays", DefaultDuplicateWindowDays);
        var since = DateTime.UtcNow.AddDays(-windowDays);

        IReadOnlyList<TransactionRow> rows;
        Dictionary<Guid, string> currencyByAccount;
        try
        {
            // Liveness policy (aligned across all 044 sentinels): only transactions on active
            // accounts participate — a disconnected account's history must not raise new alerts.
            currencyByAccount = await db.BankAccounts
                .AsNoTracking()
                .Where(a => a.IsActive)
                .Select(a => new { a.Id, a.Currency })
                .ToDictionaryAsync(a => a.Id, a => a.Currency, ct);

            var activeAccountIds = currencyByAccount.Keys.ToList();

            // Debit-only: adapters store amounts positive with TransactionType carrying the
            // direction ("debit"/"credit"); a signed negative amount is also a debit. A refund
            // (credit) at the same merchant/amount must never count toward a duplicate.
            rows = await db.Transactions
                .AsNoTracking()
                .Where(t => t.MerchantName != null
                         && t.TransactionDate >= since
                         && t.IsActive
                         && !t.IsPending
                         && activeAccountIds.Contains(t.AccountId)
                         && (t.Amount < 0 || t.TransactionType == "debit"))
                .Select(t => new TransactionRow(t.UserId, t.AccountId, t.MerchantName!, t.Amount))
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DuplicateChargeDetectionJob: failed to read transactions");
            return;
        }

        // Group on the NORMALIZED merchant (spec US2): raw statement strings vary in case,
        // whitespace and boilerplate suffixes for the same merchant.
        var groups = rows
            .GroupBy(r => (
                r.AccountId,
                MerchantKey: MerchantNameNormalizer.Normalize(r.MerchantName),
                Amount: Math.Round(Math.Abs(r.Amount), 2)));

        foreach (var group in groups)
        {
            if (group.Count() < 2) continue;

            var (accountId, merchantKey, amount) = group.Key;

            // Every unnameable merchant normalizes to one sentinel key, so this group can hold two
            // unrelated charges that merely share an amount — a duplicate claim we cannot support.
            if (merchantKey == MerchantNameNormalizer.UnknownKey) continue;

            var userId = group.First().UserId;
            var currency = currencyByAccount.TryGetValue(accountId, out var c) ? c : "USD";
            // The alert names the merchant the way the statement does; the key is for dedup only.
            var merchantName = MerchantNameNormalizer.GetDisplayName(group.Select(r => r.MerchantName));

            try
            {
                await alerts.GenerateDuplicateChargeAlertAsync(
                    userId, accountId, merchantKey, merchantName, amount, currency, group.Count(), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "DuplicateChargeDetectionJob: alert failed for account {AccountId} merchant {Merchant}",
                    accountId, merchantKey);
            }
        }
    }

    private sealed record TransactionRow(Guid UserId, Guid AccountId, string MerchantName, decimal Amount);
}
