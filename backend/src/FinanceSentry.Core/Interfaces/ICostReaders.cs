namespace FinanceSentry.Core.Interfaces;

using FinanceSentry.Core.Utils;

/// <summary>What a cost of investing was: a trade commission, another fee or transaction tax, the cost of converting currency, or tax withheld from income.</summary>
public enum CostKind
{
    Commission,
    Fee,
    FxConversion,
    Withholding,
}

/// <summary>
/// One cost row a provider reported, valued in USD on the day it was charged. <paramref name="Usd"/> is positive for money
/// paid and negative for a refund or rebate, so a sum nets them. <paramref name="Fx"/> is how the USD rate was obtained
/// (framework R4); a row converted without a published rate stays in the figure and is counted as approximate.
/// </summary>
public sealed record CostItem(CostKind Kind, DateOnly Date, decimal Usd, FxStatus Fx = FxStatus.Exact);

/// <summary>
/// The cost rows a sleeve's provider reported after a date, and how many were left out for want of a USD value (a
/// commission paid in a coin with no price) - counted, never read as a zero.
/// </summary>
public sealed record CostReading(IReadOnlyList<CostItem> Items, int UnvaluedRows)
{
    public static CostReading Empty { get; } = new([], 0);
}

/// <summary>
/// Reads the brokerage sleeve's costs dated after <c>from</c>: trade commissions and transaction taxes, fee rows, the
/// cost of currency conversions and the tax withheld from income. Runs with no person in scope, so implementations opt
/// out of the Owner query filter and keep their own user predicate.
/// </summary>
public interface IBrokerageCostReader
{
    Task<CostReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}

/// <summary>
/// Reads the crypto sleeve's trading fees dated after <c>from</c>, valued in USD at the fill's own time. Runs with no
/// person in scope, like <see cref="IBrokerageCostReader"/>.
/// </summary>
public interface ICryptoCostReader
{
    Task<CostReading> ReadAsync(Guid userId, DateOnly from, CancellationToken ct = default);
}
