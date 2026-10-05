namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Reads one of the person's transactions in the shape a recurring commitment is tracked by, so a
/// subscription or installment the user adds by picking a transaction is keyed exactly as the
/// detection job keys that transaction's charges.
/// </summary>
public interface ICommitmentTransactionReader
{
    /// <summary>
    /// The transaction, or null when it does not exist or is not the person's own. Runs on the
    /// request path, so it reads under the Owner query filter.
    /// </summary>
    Task<CommitmentTransaction?> FindAsync(Guid userId, Guid transactionId, CancellationToken ct = default);
}

/// <param name="Key">
/// The detected-commitment key the detection job files this transaction's charges under; a row
/// stored with it is advanced by every later charge that resolves to the same key.
/// </param>
/// <param name="DisplayName">The merchant name as the bank reported it, falling back to the description.</param>
/// <param name="Amount">Native amount in the account's own currency.</param>
/// <param name="Currency">ISO code of the account the transaction belongs to.</param>
public record CommitmentTransaction(
    string Key,
    string DisplayName,
    decimal Amount,
    string Currency,
    DateOnly Date);

/// <summary>One posted debit as the detection job sees it, filed under its commitment key.</summary>
/// <param name="Amount">Native amount in <paramref name="Currency"/>, the charging account's currency.</param>
public record CommitmentCharge(string Key, DateOnly Date, decimal Amount, string? Currency);
