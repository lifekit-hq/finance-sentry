namespace FinanceSentry.Modules.Research.Domain.Ports;

/// <summary>
/// Cross-module port (#1069): the fired event a Hold recommendation answers - a row in the companion outbox, the same
/// id Ledger records its verdict on. The concrete adapter lives in FinanceSentry.Integration so Modules.Research never
/// depends on Modules.Companion.
/// </summary>
public interface IFiredEventSource
{
    /// <summary>The fired event with this id, only when it belongs to the user; null otherwise.</summary>
    Task<FiredEventReference?> FindAsync(Guid userId, Guid eventId, CancellationToken ct = default);
}

/// <summary>A fired event as a recommendation cites it: its kind, what it is about and when it happened.</summary>
public sealed record FiredEventReference(Guid EventId, string Kind, string Subject, DateTimeOffset OccurredAt);
