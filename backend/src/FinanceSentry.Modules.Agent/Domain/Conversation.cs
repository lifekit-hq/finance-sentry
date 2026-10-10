namespace FinanceSentry.Modules.Agent.Domain;

using FinanceSentry.Core.Domain;

/// <summary>
/// A chat thread between a single user and Ledger (feature 040). Owner-scoped: every read and write
/// filters by <see cref="UserId"/>; there is no cross-user query path (FR-008).
/// </summary>
public sealed class Conversation : IHasUpdatedAt
{
    public Guid Id { get; set; }

    /// <summary>Owner scope — the authenticated user this thread belongs to. Indexed.</summary>
    public Guid UserId { get; set; }

    /// <summary>Short title derived from the first user message; editable later.</summary>
    public string? Title { get; set; }

    /// <summary>Model used for this thread (e.g. <c>claude-sonnet-5</c>) — for auditability.</summary>
    public string ModelId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Bumped on each new message; ordering key for the conversation list.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Message> Messages { get; set; } = [];
}
