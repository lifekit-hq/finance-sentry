namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class InvalidPriceTargetException(string reason)
    : ApiException(400, "INVALID_PRICE_TARGET", $"Invalid price target: {reason}");
