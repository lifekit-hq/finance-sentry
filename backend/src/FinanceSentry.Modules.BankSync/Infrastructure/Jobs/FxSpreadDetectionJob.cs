namespace FinanceSentry.Modules.BankSync.Infrastructure.Jobs;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Daily sentinel (044/US4): fires an FxSpread alert when a specific cross-currency conversion
/// between a user's own accounts loses more than the configured threshold to the FX spread.
///
/// Detection strategy: reuse BankSync's existing transfer matching
/// (<see cref="ITransferDetectionService"/>) to pair individual debit and credit legs — amount
/// proximity in USD, date proximity, and a transfer type/category/description signal — then
/// compute the implied conversion rate per matched pair (credit amount ÷ debit amount) and
/// compare it to the <see cref="CurrencyConverter"/> market rate. Unrelated same-day flows
/// (a salary credit next to a rent debit) never pair because they carry no transfer signal.
/// No external feed is required — rates come from the process-level rate table (refreshed by
/// the FX job).
/// </summary>
public sealed class FxSpreadDetectionJob(
    BankSyncDbContext db,
    ITransferDetectionService transferDetection,
    IAlertGeneratorService alerts,
    IOptions<HygieneSentinelsOptions> options,
    ILogger<FxSpreadDetectionJob> logger)
{
    // The transfer matcher's default cross-currency tolerance (5%) exists to REJECT pairs that
    // deviate from the market rate — but a costly conversion deviates by exactly the spread we
    // are hunting. Widen the amount tolerance for this sentinel so a pair losing up to ~30% to
    // FX still matches; the transfer type/category/description signal remains the gate that
    // keeps unrelated flows from pairing.
    private const decimal PairingAmountTolerance = 0.30m;

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var threshold = options.Value.FxSpreadThreshold;
        var since = DateTime.UtcNow.AddDays(-options.Value.FxSpreadLookbackDays);

        // This is the one sentinel that judges a rate against a rate, so the reference has to be
        // a real one. CurrencyConverter seeds itself with hardcoded constants (EUR 1.08, UAH
        // 0.024) that are live until the refresh job first ticks and stay put through any feed
        // outage after that — measured against them, a perfectly fair conversion reads as a
        // multi-percent loss and the user gets told their bank gouged them. Standing down defers
        // rather than drops while the outage stays shorter than the lookback window — the next
        // tick re-examines it. An outage past MaxRateAge + FxSpreadLookbackDays does lose the
        // conversions that age out meanwhile: a fair trade against alerting on fiction, and the
        // skip is logged so the outage is visible.
        var maxRateAge = TimeSpan.FromHours(options.Value.FxSpreadMaxRateAgeHours);
        if (!CurrencyConverter.AreRatesFresh(maxRateAge))
        {
            logger.LogWarning(
                "FxSpreadDetectionJob: skipped — FX rates last refreshed {UpdatedAt} (max age {MaxAge}); "
                + "a spread measured against the offline seed table would be fiction",
                CurrencyConverter.RatesUpdatedAtUtc?.ToString("O") ?? "never", maxRateAge);
            return;
        }

        ActiveAccountSnapshot accounts;
        try
        {
            accounts = await ActiveAccountSnapshot.ReadAsync(db, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FxSpreadDetectionJob: failed to read accounts");
            return;
        }

        var multiCurrencyUsers = accounts.Accounts
            .GroupBy(a => a.UserId)
            .Where(g => g.Select(a => a.Currency).Distinct().Count() >= 2)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (multiCurrencyUsers.Count == 0) return;

        IReadOnlyList<Transaction> transactions;
        try
        {
            var accountIds = accounts.Accounts
                .Where(a => multiCurrencyUsers.ContainsKey(a.UserId))
                .Select(a => a.AccountId)
                .ToList();

            // Settled legs only. A hold's amount is provisional, and a cross-currency conversion
            // is precisely where the bank revises it on settlement — so an implied rate divided
            // out of a hold is a rate nobody was charged. The hold also outlives its settled twin
            // here: PendingReconciler retires a hold by matching it to a posted row on amount, and
            // an FX hold settles at a different amount, so both rows stay active and pair
            // independently — two alerts for one conversion, under two debit ids the dedup key
            // cannot join.
            //
            // Accepted gap: a Monobank hold settles in place and keeps its original date, so one
            // clearing more than FxSpreadLookbackDays after it was made becomes eligible only once
            // it has already aged out of the window. Widening the select to
            // (PostedDate ?? TransactionDate) does NOT close it — no adapter ever sets PostedDate
            // to a later settlement time (Monobank writes the transaction date even for a hold,
            // TrueLayer writes null and then the replacement posted row's own date), so that read
            // selects the same rows. Closing it needs a real settled-at stamp from ingest.
            transactions = await db.Transactions
                .AsNoTracking()
                .Where(t => accountIds.Contains(t.AccountId)
                         && t.TransactionDate >= since
                         && t.IsActive
                         && !t.IsPending)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FxSpreadDetectionJob: failed to read transactions");
            return;
        }

        var txByUser = transactions
            .GroupBy(t => t.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Transaction>)g.ToList());

        foreach (var (userId, userAccounts) in multiCurrencyUsers)
        {
            if (!txByUser.TryGetValue(userId, out var userTransactions)) continue;

            var currencyByAccount = userAccounts.ToDictionary(a => a.AccountId, a => a.Currency);

            var pairs = transferDetection.DetectTransferPairs(
                userTransactions, currencyByAccount, PairingAmountTolerance);

            foreach (var (debit, credit) in pairs)
            {
                if (!currencyByAccount.TryGetValue(debit.AccountId, out var fromCurrency)
                    || !currencyByAccount.TryGetValue(credit.AccountId, out var toCurrency))
                {
                    continue;
                }

                // Same-currency transfers carry no FX conversion — nothing to measure.
                if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase)) continue;

                var marketRate = ComputeMarketRate(fromCurrency, toCurrency);
                if (marketRate <= 0) continue;

                // Implied rate is a ratio of the two NATIVE leg amounts of ONE conversion —
                // this is a rate, not a cross-currency sum, so no USD normalisation applies.
                var debitAmount = Math.Abs(debit.Amount);
                if (debitAmount == 0) continue;
                var impliedRate = Math.Abs(credit.Amount) / debitAmount;

                var spread = (marketRate - impliedRate) / marketRate;
                if (spread <= threshold) continue;

                try
                {
                    // Dedup per (UserId, debit transaction id) — each concrete conversion can
                    // alert exactly once; a new costly conversion is a new alert.
                    await alerts.GenerateFxSpreadAlertAsync(
                        userId, debit.Id, fromCurrency, toCurrency, impliedRate, marketRate, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "FxSpreadDetectionJob: alert failed for user {UserId} pair {From}→{To} (debit {DebitId})",
                        userId, fromCurrency, toCurrency, debit.Id);
                }
            }
        }
    }

    /// <summary>
    /// Returns the number of <paramref name="toCurrency"/> units per 1 <paramref name="fromCurrency"/>
    /// unit at the current market rate, or 0 when either currency is unknown.
    /// </summary>
    private static decimal ComputeMarketRate(string fromCurrency, string toCurrency)
    {
        if (!CurrencyConverter.IsKnown(fromCurrency) || !CurrencyConverter.IsKnown(toCurrency))
            return 0m;

        var fromUsd = CurrencyConverter.ToUsd(1m, fromCurrency);
        var toUsd = CurrencyConverter.ToUsd(1m, toCurrency);
        return toUsd > 0m ? fromUsd / toUsd : 0m;
    }
}
