namespace FinanceSentry.Core.Measures;

/// <summary>
/// The five flow classes of finance-framework §3.2, with tax kept beside fee. Classification depends on the
/// scope being measured (rule 1), so it happens before a flow reaches the engine: the engine never decides
/// whether a movement is external or internal.
/// </summary>
public enum FlowClass
{
    /// <summary>Money or assets entering the scope by the owner's action: a deposit, a transfer in.</summary>
    ExternalInflow,

    /// <summary>Money or assets leaving the scope by the owner's action: a withdrawal, a transfer out.</summary>
    ExternalOutflow,

    /// <summary>Dividends, interest, staking rewards: part of the return, never a flow (rule 2).</summary>
    Income,

    /// <summary>Commissions, account fees: part of the return, as a negative.</summary>
    Fee,

    /// <summary>Withholding and other taxes: part of the return, as a negative.</summary>
    Tax,

    /// <summary>A move inside the scope (a buy, a sell, a transfer between two in-scope accounts): invisible.</summary>
    Internal,
}

/// <summary>
/// One classified movement in USD, converted at the flow date's rate (rule 5; decision C4: the engine
/// computes in USD and never converts for display). <paramref name="AmountUsd"/> is the size of the movement
/// and is never negative: its direction comes from <paramref name="Class"/>.
/// <para>
/// Timing within a day (rule 7, the Portfolio Performance convention): an inflow is placed at the start of
/// its day and an outflow at the end. A window opens at the close of its first valuation date, so a flow
/// dated that day is already inside the opening value and belongs to the window before it; the flows of a
/// window are those dated after the opening valuation, up to and including the closing one
/// (<see cref="MeasureWindow.FlowsWithin"/>).
/// </para>
/// </summary>
public readonly record struct ClassifiedFlow(DateOnly Date, FlowClass Class, decimal AmountUsd);

/// <summary>
/// The scope's value in USD at the close of <paramref name="Date"/>. A null value is a day with no valuation
/// (a missing price): it is unknown, never zero, and the measures bridge over it instead of reading it as a
/// total loss.
/// </summary>
public readonly record struct ValuePoint(DateOnly Date, decimal? ValueUsd);

/// <summary>Window helpers shared by the measures.</summary>
public static class MeasureWindow
{
    /// <summary>
    /// The flows that belong to the window opening at the close of <paramref name="opening"/> and closing at
    /// the close of <paramref name="closing"/>: dated after the opening day, up to and including the closing day.
    /// </summary>
    public static IReadOnlyList<ClassifiedFlow> FlowsWithin(
        IEnumerable<ClassifiedFlow> flows, DateOnly opening, DateOnly closing)
        => [.. flows.Where(f => f.Date > opening && f.Date <= closing)];

    /// <summary>
    /// Whether the window spans one calendar year or more, the threshold below which no return is annualised
    /// (GIPS 2.A.12).
    /// </summary>
    public static bool IsYearOrMore(DateOnly opening, DateOnly closing) => closing >= opening.AddYears(1);

    internal static bool HasNegativeAmount(IEnumerable<ClassifiedFlow> flows) => flows.Any(f => f.AmountUsd < 0m);
}
