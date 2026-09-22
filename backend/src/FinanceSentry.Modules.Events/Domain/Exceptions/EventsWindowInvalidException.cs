namespace FinanceSentry.Modules.Events.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public sealed class EventsWindowInvalidException(string detail)
    : ApiException(400, "EVENTS_WINDOW_INVALID", $"Invalid events window: {detail}");
