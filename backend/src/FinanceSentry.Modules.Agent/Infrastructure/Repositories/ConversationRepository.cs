namespace FinanceSentry.Modules.Agent.Infrastructure.Repositories;

using FinanceSentry.Modules.Agent.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core repository for agent conversations. Every query filters by the caller's <c>userId</c> so a
/// caller can only ever touch their own threads (FR-008) — there is no method that omits the owner.
/// Reads also run under the Owner query filter; every caller is a request acting for the signed-in person.
/// </summary>
public sealed class ConversationRepository(AgentDbContext db) : IConversationRepository
{
    private readonly AgentDbContext _db = db;

    public async Task<Conversation> CreateAsync(Guid userId, string? title, string modelId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            ModelId = modelId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task<Conversation?> GetWithMessagesAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Where(c => c.Id == conversationId && c.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
        {
            return null;
        }

        conversation.Messages = await _db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        return conversation;
    }

    public async Task<IReadOnlyList<Conversation>> ListAsync(Guid userId, CancellationToken ct)
    {
        return await _db.Conversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task AppendMessageAsync(Guid userId, Guid conversationId, Message message, CancellationToken ct)
    {
        // Ownership guard: only append when the conversation belongs to the caller.
        var owned = await _db.Conversations
            .AnyAsync(c => c.Id == conversationId && c.UserId == userId, ct);
        if (!owned)
        {
            throw new InvalidOperationException($"Conversation {conversationId} not found for the caller.");
        }

        message.Id = message.Id == Guid.Empty ? Guid.NewGuid() : message.Id;
        message.ConversationId = conversationId;
        message.CreatedAt = message.CreatedAt == default ? DateTimeOffset.UtcNow : message.CreatedAt;
        _db.Messages.Add(message);

        var conversation = await _db.Conversations.FirstAsync(c => c.Id == conversationId, ct);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid conversationId, CancellationToken ct)
    {
        var conversation = await _db.Conversations
            .Where(c => c.Id == conversationId && c.UserId == userId)
            .FirstOrDefaultAsync(ct);

        if (conversation is null)
        {
            return false;
        }

        _db.Conversations.Remove(conversation);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
