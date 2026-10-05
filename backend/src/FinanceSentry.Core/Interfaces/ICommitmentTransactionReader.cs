namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// Reads one of the person's transactions in the shape a recurring commitment is tracked by, so a
/// subscription or installment the user adds by picking a transaction is keyed exactly as the
/// detection job keys that transaction's charges and starts from the latest of them.
/// </summary>
public interface ICommitmentTransactionReader
{
    /// <summary>
    /// The transaction, or null when it does not exist or is not the person's own. Runs on the
    /// request path, so it reads under the Owner query filter.
    /// </summary>
    Task<CommitmentTransaction?> FindAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>
    /// The same read for an installment: keyed by merchant and the picked charge's amount, so the
    /// charge count, latest charge and cadence cover only charges of that amount.
    /// </summary>
    Task<CommitmentTransaction?> FindInstallmentAsync(Guid userId, Guid transactionId, CancellationToken ct = default);
}

/// <param name="Key">
/// The detected-commitment key the detection job files this transaction's charges under; a row
/// stored with it is advanced by every later charge that resolves to the same key.
/// </param>
/// <param name="DisplayName">The merchant name as the bank reported it, falling back to the description.</param>
/// <param name="Amount">Native amount of the latest charge under <paramref name="Key"/>, in its account's currency.</param>
/// <param name="Currency">ISO code of the account the latest charge belongs to.</param>
/// <param name="Date">
/// Date of the latest charge under <paramref name="Key"/>, which may be later than the picked one:
/// a row started from it is already current instead of waiting for the next detection run.
/// </param>
/// <param name="ChargeCount">Charges the person has made under <paramref name="Key"/> up to <paramref name="Date"/> (one per day).</param>
/// <param name="Cadence">
/// <see cref="SubscriptionCadences"/> value read from the median gap between those charges, or null
/// when there are fewer than two and the history says nothing about it.
/// </param>
public record CommitmentTransaction(
    string Key,
    string DisplayName,
    decimal Amount,
    string Currency,
    DateOnly Date,
    int ChargeCount,
    string? Cadence = null);

/// <summary>One posted debit as the detection job sees it, filed under its commitment key.</summary>
/// <param name="Amount">Native amount in <paramref name="Currency"/>, the charging account's currency.</param>
public record CommitmentCharge(string Key, DateOnly Date, decimal Amount, string? Currency);
