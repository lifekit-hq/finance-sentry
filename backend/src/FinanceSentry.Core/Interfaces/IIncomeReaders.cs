namespace FinanceSentry.Core.Interfaces;

/// <summary>What an income receipt was: a dividend or distribution, interest, or a staking or earn reward.</summary>
public enum IncomeReceiptKind
{
    Dividend,
    Interest,
    Staking,
}

/// <summary>
/// One income row a provider reported, valued in USD at the day it was paid. <paramref name="Usd"/> is signed as the
/// provider booked it: a dividend is positive, withholding tax (<paramref name="IsWithholding"/>) is negative and a
/// refund of it positive. It is null when the row cannot be valued (a reward with no price), which is counted, never
/// read as a zero.
/// </summary>
public sealed record IncomeReceipt(string Symbol, IncomeReceiptKind Kind, DateOnly Date, decimal? Usd, bool IsWithholding);

/// <summary>
/// Reads the brokerage sleeve's income rows (dividends, distributions, interest and the tax withheld from them) dated
/// after <c>from</c>. Runs with no person in scope, so implementations opt out of the Owner query filter and keep
/// their own user predicate.
/// </summary>
public interface IBrokerageIncomeReader
{
    Task<IReadOnlyList<IncomeReceipt>> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}

/// <summary>
/// Reads the crypto sleeve's income rows (staking and earn rewards, airdrops) dated after <c>from</c>, valued at the
/// venue price taken when the row was read. Runs with no person in scope, like <see cref="IBrokerageIncomeReader"/>.
/// </summary>
public interface ICryptoIncomeReader
{
    Task<IReadOnlyList<IncomeReceipt>> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}
