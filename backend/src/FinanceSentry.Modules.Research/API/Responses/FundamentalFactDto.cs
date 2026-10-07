namespace FinanceSentry.Modules.Research.API.Responses;

public record FundamentalFactDto(
    string Ticker,
    string Concept,
    string Label,
    string Unit,
    decimal Value,
    DateOnly PeriodEnd,
    string? FiscalPeriod,
    int? FiscalYear,
    string Form,
    string Taxonomy);
