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

        var account = await accounts.GetByIdAsync(transaction.AccountId, ct);
        if (account is null)
            return null;

        var key = CommitmentKeyResolver.Resolve(transaction.MerchantName, transaction.Description, transaction.Amount, transaction.Mcc);
        var date = DateOnly.FromDateTime(transaction.TransactionDate);

        var chargeCount = (await transactions.GetByUserIdAsync(userId, ct))
            .Where(t => t.IsActive
                     && !t.IsPending
                     && t.Amount != 0m
                     && (t.TransactionType == null || t.TransactionType == "debit")
                     && CommitmentKeyResolver.Resolve(t.MerchantName, t.Description, t.Amount, t.Mcc) == key)
            .Select(t => DateOnly.FromDateTime(t.TransactionDate))
            .Where(d => d <= date)
            .Distinct()
            .Count();

        return new CommitmentTransaction(
            key,
            string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName,
            transaction.Amount,
            account.Currency,
            date,
            Math.Max(1, chargeCount));
    }
}
