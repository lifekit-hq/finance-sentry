namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class InvalidPriceBarRequestException(string field, string? value, string allowed)
    : ApiException(400, "INVALID_PRICE_BAR_REQUEST", $"Unsupported chart {field} '{value}'; use {allowed}.");
