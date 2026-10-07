namespace FinanceSentry.Modules.Research.API.Responses;

using FinanceSentry.Modules.Research.Domain;

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
    string Taxonomy,
    SourceProvenance? SourceProvenance);
