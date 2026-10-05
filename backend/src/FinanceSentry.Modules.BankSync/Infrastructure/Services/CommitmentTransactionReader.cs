namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public class CommitmentTransactionReader(
    IBankAccountRepository accounts,
    ITransactionRepository transactions) : ICommitmentTransactionReader
{
    public async Task<CommitmentTransaction?> FindAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var transaction = await transactions.GetByIdAsync(transactionId, ct);
        if (transaction is null || transaction.UserId != userId || !transaction.IsActive)
            return null;

        var key = CommitmentKeyResolver.Resolve(transaction.MerchantName, transaction.Description, transaction.Amount, transaction.Mcc);

        var charges = (await transactions.GetByUserIdAsync(userId, ct))
            .Where(t => t.IsActive
                     && !t.IsPending
                     && t.Amount != 0m
                     && (t.TransactionType == null || t.TransactionType == "debit")
                     && CommitmentKeyResolver.Resolve(t.MerchantName, t.Description, t.Amount, t.Mcc) == key)
            .Append(transaction)
            .ToList();
        var latest = charges.MaxBy(t => t.TransactionDate)!;

        var account = await accounts.GetByIdAsync(latest.AccountId, ct);
        if (account is null)
            return null;

        return new CommitmentTransaction(
            key,
            string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName,
            latest.Amount,
            account.Currency,
            DateOnly.FromDateTime(latest.TransactionDate),
            charges.Select(t => DateOnly.FromDateTime(t.TransactionDate)).Distinct().Count());
    }
}
