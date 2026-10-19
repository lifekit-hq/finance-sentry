namespace FinanceSentry.Modules.Wealth.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class ManualHoldingNotFoundException()
    : ApiException(404, "MANUAL_HOLDING_NOT_FOUND", "Manual holding not found.");
