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

        return new CommitmentTransaction(
            CommitmentKeyResolver.Resolve(transaction.MerchantName, transaction.Description, transaction.Amount, transaction.Mcc),
            string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName,
            transaction.Amount,
            account.Currency,
            DateOnly.FromDateTime(transaction.TransactionDate));
    }
}
