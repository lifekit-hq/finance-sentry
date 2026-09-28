namespace FinanceSentry.Modules.Wealth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

public sealed record BackfillNetWorthHistoryCommand(Guid UserId) : ICommand<BackfillNetWorthHistoryResult>;

public sealed record BackfillNetWorthHistoryResult(
    int InsertedCount,
    DateOnly? AnchorSnapshotDate,
    DateOnly? EarliestBackfilledDate);

/// <summary>
/// Reconstructs banking-only net worth history for the gap before a user's earliest real
/// <see cref="NetWorthSnapshot"/>, by walking each active bank account's balance backward
/// from its current value through its own transaction history one day at a time:
/// balance_on(d-1) = balance_on(d) - ownEffect(d). ownEffect flips sign for a credit
/// (liability) account, since a debit there grows the amount owed instead of shrinking a
/// balance (see <see cref="AccountBalanceMath.IsLiability"/> and §1 of
/// docs/money-semantics.md). Only banking is reconstructable this way — brokerage and
/// crypto positions leave no transaction ledger to walk here — so every inserted row
/// carries BrokerageTotal = CryptoTotal = 0 and IsApproximate = true. Currency conversion
/// uses <see cref="CurrencyConverter.ToUsd"/>'s current rate table, not the historical
/// rate on the reconstructed date; that, plus the banking-only scope, is why the segment
/// is approximate rather than measured. Each account's reconstructable range is bounded
/// by its own earliest synced transaction — an account connected after the gap started
/// contributes only from that date onward, never fabricated further back.
///
/// Idempotent: a row is inserted only for a date strictly before the earliest real
/// snapshot, and only into a (UserId, SnapshotDate) slot not already occupied by a real or
/// previously backfilled row — see <see cref="INetWorthSnapshotRepository.InsertMissingAsync"/>.
/// A re-run never overwrites an existing row.
/// </summary>
public sealed class BackfillNetWorthHistoryCommandHandler(
    INetWorthSnapshotRepository snapshotRepository,
    IBankingAccountsReader accountsReader,
    IBankingTransactionReader transactionReader)
    : ICommandHandler<BackfillNetWorthHistoryCommand, BackfillNetWorthHistoryResult>
{
    private readonly INetWorthSnapshotRepository _snapshotRepository =
        snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
    private readonly IBankingAccountsReader _accountsReader =
        accountsReader ?? throw new ArgumentNullException(nameof(accountsReader));
    private readonly IBankingTransactionReader _transactionReader =
        transactionReader ?? throw new ArgumentNullException(nameof(transactionReader));

    public async Task<BackfillNetWorthHistoryResult> Handle(
        BackfillNetWorthHistoryCommand command, CancellationToken cancellationToken)
    {
        var anchor = await _snapshotRepository.GetEarliestByUserIdAsync(command.UserId, cancellationToken);
        if (anchor is null)
            return new BackfillNetWorthHistoryResult(0, null, null);

        var accounts = (await _accountsReader.GetActiveAccountSnapshotsAsync(command.UserId, cancellationToken))
            .Where(a => a.CurrentBalance.HasValue)
            .ToList();
        if (accounts.Count == 0)
            return new BackfillNetWorthHistoryResult(0, anchor.SnapshotDate, null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var transactions = await _transactionReader.GetTransactionsAsync(
            command.UserId, DateOnly.MinValue, today, cancellationToken);

        var settledTransactions = transactions.Where(t => !t.IsPending).ToList();

        var byAccountDay = settledTransactions
            .GroupBy(t => (t.AccountId, Date: DateOnly.FromDateTime(t.EffectiveDate)))
            .ToDictionary(g => g.Key, g => g.ToList());

        var earliestTxDateByAccount = settledTransactions
            .GroupBy(t => t.AccountId)
            .ToDictionary(g => g.Key, g => g.Min(t => DateOnly.FromDateTime(t.EffectiveDate)));

        var balanceByAccountDay = new Dictionary<(Guid AccountId, DateOnly Date), decimal>();
        DateOnly? globalFloor = null;

        foreach (var account in accounts)
        {
            if (!earliestTxDateByAccount.TryGetValue(account.AccountId, out var floor))
                continue; // no transaction history at all for this account — nothing to walk back with

            var balance = account.CurrentBalance!.Value;
            balanceByAccountDay[(account.AccountId, today)] = balance;

            var day = today;
            while (day > floor)
            {
                var ownEffect = byAccountDay.TryGetValue((account.AccountId, day), out var dayTransactions)
                    ? dayTransactions.Sum(t => SignedNativeEffect(account.AccountType, t))
                    : 0m;

                balance -= ownEffect;
                day = day.AddDays(-1);
                balanceByAccountDay[(account.AccountId, day)] = balance;
            }

            globalFloor = globalFloor is null || floor < globalFloor.Value ? floor : globalFloor;
        }

        if (globalFloor is null || globalFloor.Value >= anchor.SnapshotDate)
            return new BackfillNetWorthHistoryResult(0, anchor.SnapshotDate, null);

        var rows = new List<NetWorthSnapshot>();
        for (var date = anchor.SnapshotDate.AddDays(-1); date >= globalFloor.Value; date = date.AddDays(-1))
        {
            var includedAccounts = accounts
                .Where(a => balanceByAccountDay.ContainsKey((a.AccountId, date)))
                .ToList();
            if (includedAccounts.Count == 0)
                continue;

            var bankingTotalUsd = includedAccounts.Sum(a =>
                AccountBalanceMath.SignedForNetTotal(
                    a.AccountType,
                    CurrencyConverter.ToUsd(balanceByAccountDay[(a.AccountId, date)], a.Currency)));

            rows.Add(new NetWorthSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = command.UserId,
                SnapshotDate = date,
                BankingTotal = bankingTotalUsd,
                BrokerageTotal = 0m,
                CryptoTotal = 0m,
                TotalNetWorth = bankingTotalUsd,
                Currency = "USD",
                TakenAt = DateTimeOffset.UtcNow,
                IsApproximate = true,
            });
        }

        if (rows.Count == 0)
            return new BackfillNetWorthHistoryResult(0, anchor.SnapshotDate, null);

        var inserted = await _snapshotRepository.InsertMissingAsync(rows, cancellationToken);
        var earliestBackfilled = rows.Min(r => r.SnapshotDate);

        return new BackfillNetWorthHistoryResult(inserted, anchor.SnapshotDate, earliestBackfilled);
    }

    /// <summary>
    /// The transaction's effect on the account's own stored balance field — not the
    /// net-worth sign, which <see cref="AccountBalanceMath.SignedForNetTotal"/> applies
    /// separately at the aggregation boundary. For checking/savings a credit grows the
    /// balance; for a credit (liability) account a debit grows the amount owed instead.
    /// </summary>
    private static decimal SignedNativeEffect(string? accountType, BankingTransactionSummary transaction)
    {
        var isCredit = string.Equals(transaction.TransactionType, "credit", StringComparison.OrdinalIgnoreCase);
        var growsBalance = AccountBalanceMath.IsLiability(accountType) ? !isCredit : isCredit;
        return growsBalance ? transaction.Amount : -transaction.Amount;
    }
}
