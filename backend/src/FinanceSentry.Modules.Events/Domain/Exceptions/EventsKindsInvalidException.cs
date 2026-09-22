namespace FinanceSentry.Modules.Events.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public sealed class EventsKindsInvalidException(IReadOnlyCollection<string> unknownKinds)
    : ApiException(
        400,
        "EVENTS_KINDS_INVALID",
        $"Unknown event kinds: {string.Join(", ", unknownKinds)}. Valid kinds: {string.Join(", ", EventKind.All)}.");
