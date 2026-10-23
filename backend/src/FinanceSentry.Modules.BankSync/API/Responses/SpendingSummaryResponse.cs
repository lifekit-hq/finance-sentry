namespace FinanceSentry.Modules.BankSync.API.Responses;

public record SpendingSummaryResponse(decimal Outflow, string Currency, string? TopCategory);
