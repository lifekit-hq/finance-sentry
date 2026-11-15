namespace FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// One period's free cash flow (#1376): operating cash flow less capex, in the filer's unit. A figure is
/// null with <see cref="Reason"/> whenever either side is missing - never zero-filled, so a filer with no
/// capex line does not read as "capex was nil". <see cref="YoyChange"/> compares with the same period a
/// year earlier ((current - prior) / |prior|); null with <see cref="YoyReason"/> when that period is not in
/// the history read, has no free cash flow, or it was zero.
/// </summary>
public sealed record FreeCashFlowPeriod(
    string Basis,
    DateOnly PeriodEnd,
    string? FiscalPeriod,
    int? FiscalYear,
    string Unit,
    decimal? OperatingCashFlow,
    decimal? Capex,
    decimal? FreeCashFlow,
    decimal? YoyChange,
    string? Reason,
    string? YoyReason = null);

/// <summary>
/// One fiscal year's return on equity (parent net income / year-end parent equity), labelled by the year of its
/// period end (EDGAR's fiscal year is the filing's, so a repeated comparative would carry the wrong one): the
/// cruder sibling of <see cref="ReturnOnInvestedCapitalPeriod"/>, which stays computable when debt or tax lines are
/// missing. Null with <see cref="Reason"/> when an input is missing or equity is not positive.
/// </summary>
public sealed record ReturnOnEquityPeriod(
    DateOnly PeriodEnd,
    int? FiscalYear,
    decimal? NetIncome,
    decimal? StockholdersEquity,
    decimal? ReturnOnEquity,
    string? Reason);

/// <summary>
/// One fiscal year's return on invested capital (#1376): NOPAT / year-end invested capital, where
/// NOPAT = operating income x (1 - effective tax rate), the rate being income tax / pre-tax income (bounded to
/// 0-100%), and invested capital = debt + parent stockholders' equity - cash and cash equivalents (marketable
/// securities are not netted, operating leases are not added). Every input is shown so the figure can be
/// checked; the whole row is null with <see cref="Reason"/> (naming what is missing or why it cannot be formed)
/// rather than filled with zeros. <see cref="Debt"/> is borrowings as the filer tags them: the total when it
/// reports one, otherwise the long-term line plus the current line when that is reported too.
/// </summary>
public sealed record ReturnOnInvestedCapitalPeriod(
    DateOnly PeriodEnd,
    int? FiscalYear,
    string? Unit,
    decimal? OperatingIncome,
    decimal? EffectiveTaxRate,
    decimal? Nopat,
    decimal? Debt,
    decimal? StockholdersEquity,
    decimal? Cash,
    decimal? InvestedCapital,
    decimal? ReturnOnInvestedCapital,
    string? Reason);
