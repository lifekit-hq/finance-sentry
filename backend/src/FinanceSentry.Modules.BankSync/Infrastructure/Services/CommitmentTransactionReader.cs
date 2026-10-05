namespace FinanceSentry.Modules.BankSync.Infrastructure.Services;

using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public class CommitmentTransactionReader(
    IBankAccountRepository accounts,
    ITransactionRepository transactions) : ICommitmentTransactionReader
{
    public Task<CommitmentTransaction?> FindAsync(Guid userId, Guid transactionId, CancellationToken ct = default) =>
        ReadAsync(userId, transactionId, CommitmentKeyResolver.Resolve, ct);

    public Task<CommitmentTransaction?> FindInstallmentAsync(Guid userId, Guid transactionId, CancellationToken ct = default) =>
        ReadAsync(userId, transactionId, InstallmentCommitmentKey.Resolve, ct);

    private async Task<CommitmentTransaction?> ReadAsync(
        Guid userId, Guid transactionId, Func<string?, string?, decimal, int?, string> keyOf, CancellationToken ct)
    {
        var transaction = await transactions.GetByIdAsync(transactionId, ct);
        if (transaction is null || transaction.UserId != userId || !transaction.IsActive)
            return null;

        var key = keyOf(transaction.MerchantName, transaction.Description, transaction.Amount, transaction.Mcc);

        var charges = (await transactions.GetByUserIdAsync(userId, ct))
            .Where(t => t.IsActive
                     && !t.IsPending
                     && t.Amount != 0m
                     && (t.TransactionType == null || t.TransactionType == "debit")
                     && keyOf(t.MerchantName, t.Description, t.Amount, t.Mcc) == key)
            .Append(transaction)
            .ToList();
        var latest = charges.MaxBy(t => t.TransactionDate)!;

        var account = await accounts.GetByIdAsync(latest.AccountId, ct);
        if (account is null)
            return null;

        var dates = charges.Select(t => DateOnly.FromDateTime(t.TransactionDate)).Distinct().Order().ToList();

        return new CommitmentTransaction(
            key,
            string.IsNullOrWhiteSpace(transaction.MerchantName) ? transaction.Description : transaction.MerchantName,
            latest.Amount,
            account.Currency,
            DateOnly.FromDateTime(latest.TransactionDate),
            dates.Count,
            CadenceOf(dates));
    }

    private static string? CadenceOf(List<DateOnly> dates)
    {
        if (dates.Count < 2)
            return null;

        var gaps = dates.Zip(dates.Skip(1), (a, b) => b.DayNumber - a.DayNumber).Order().ToList();
        var middle = gaps.Count / 2;
        var median = gaps.Count % 2 == 1 ? gaps[middle] : (gaps[middle - 1] + gaps[middle]) / 2.0;
        return median > AnnualCadenceMinMedianDays ? SubscriptionCadences.Annual : SubscriptionCadences.Monthly;
    }

    private const int AnnualCadenceMinMedianDays = 200;
}
