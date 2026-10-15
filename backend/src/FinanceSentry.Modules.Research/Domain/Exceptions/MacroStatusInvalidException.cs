namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public sealed class MacroStatusInvalidException(string status)
    : ApiException(
        400,
        "MACRO_STATUS_INVALID",
        $"Unknown macro event status '{status}'. Valid statuses: {MacroEventStatus.Scheduled}, {MacroEventStatus.Released}.");
