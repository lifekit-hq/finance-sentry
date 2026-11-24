namespace FinanceSentry.Modules.Radar.Domain;

using FinanceSentry.Core.Interfaces;

/// <summary>
/// Income received over one window, in USD: <c>Gross</c> is what was paid, <c>Withholding</c> the tax withheld from
/// it (a refund reduces it) and <c>Net</c> the difference, which is what reached the account.
/// </summary>
public sealed record IncomeWindow(decimal Gross, decimal Withholding, decimal Net)
{
    public static IncomeWindow Zero { get; } = new(0m, 0m, 0m);
}

/// <summary>
/// One holding's income. <c>Ttm</c> is the trailing 12 months and <c>Ytd</c> the calendar year to date, each with its
/// withholding netted and shown. <c>ForwardAnnualUsd</c> is the stored dividend yield times the position's USD value
/// (null with no yield); <c>YieldOnCost</c> is the trailing-12-month net income over the position's verified cost
/// basis (null without one); <c>NextExDividend</c> is the next ex-dividend day on the calendar.
/// </summary>
public sealed record HoldingIncome(
    string Symbol,
    IReadOnlyList<IncomeReceiptKind> Kinds,
    IncomeWindow Ttm,
    IncomeWindow Ytd,
    decimal? ForwardAnnualUsd,
    decimal? YieldOnCost,
    DateOnly? NextExDividend);

/// <summary>
/// The book's income received (finance-framework §6 C2a counts it as growth; this shows it as a stream): dividends,
/// interest and staking rewards by holding, trailing 12 months and year to date, with the forward estimate and the
/// yield on cost. <c>ForwardAnnualUsd</c> and <c>YieldOnCost</c> are null when no holding gives the input, never a
/// zero. <c>UnvaluedReceipts</c> counts rows left out for want of a USD value. <c>ApproximateReceipts</c> counts the
/// rows kept in the figures that were converted without a published rate (today's rate, the seed table or 1:1), and
/// <c>FxReasons</c> carries the typed reason codes for them (empty when none). A view over rows already collected:
/// the parts stay separate and nothing is scored.
/// </summary>
public sealed record BookIncome(
    DateOnly AsOf,
    IncomeWindow Ttm,
    IncomeWindow Ytd,
    decimal? ForwardAnnualUsd,
    decimal? YieldOnCost,
    int UnvaluedReceipts,
    int ApproximateReceipts,
    IReadOnlyList<string> FxReasons,
    IReadOnlyList<HoldingIncome> Holdings)
{
    public static BookIncome Empty(DateOnly asOf) => new(asOf, IncomeWindow.Zero, IncomeWindow.Zero, null, null, 0, 0, [], []);
}
