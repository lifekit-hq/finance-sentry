namespace FinanceSentry.Modules.Research.Domain.Fundamentals;

/// <summary>
/// One period's free cash flow (#1376): operating cash flow less capex, in the filer's unit. A figure is
/// null with <see cref="Reason"/> whenever either side is missing - never zero-filled, so a filer with no
/// capex line does not read as "capex was nil". <see cref="YoyChange"/> compares with the same period a
/// year earlier ((current - prior) / |prior|); null when that period has no free cash flow or it was zero.
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
    string? Reason);

/// <summary>
/// One fiscal year's return on equity (parent net income / year-end parent equity): the proxy for ROIC,
/// which needs debt, cash and tax lines the fundamentals chain does not carry. Null with
/// <see cref="Reason"/> when an input is missing or equity is not positive.
/// </summary>
public sealed record ReturnOnEquityPeriod(
    DateOnly PeriodEnd,
    int? FiscalYear,
    decimal? NetIncome,
    decimal? StockholdersEquity,
    decimal? ReturnOnEquity,
    string? Reason);
