namespace FinanceSentry.Modules.Agent.Infrastructure;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Agent.Domain;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Persistence for the in-app finance agent (feature 040). Schema <c>agent</c>, history table
/// <c>__ef_migrations_history_agent</c>. Holds conversations and their messages — financial context,
/// so it inherits retention/backup (024).
/// </summary>
public class AgentDbContext(DbContextOptions<AgentDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<Conversation> Conversations { get; set; } = null!;

    public DbSet<Message> Messages { get; set; } = null!;

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("agent");
        base.OnModelCreating(modelBuilder);

        var conversation = modelBuilder.Entity<Conversation>();
        conversation.ToTable("agent_conversations");
        conversation.HasKey(x => x.Id);
        conversation.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        conversation.Property(x => x.UserId).IsRequired();
        conversation.Property(x => x.Title).HasMaxLength(200);
        conversation.Property(x => x.ModelId).IsRequired().HasMaxLength(80);
        conversation.Property(x => x.CreatedAt).IsRequired();
        conversation.Property(x => x.UpdatedAt).IsRequired();
        conversation.HasIndex(x => new { x.UserId, x.UpdatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("idx_agent_conversations_user_updated");
        conversation.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
        conversation.HasMany(x => x.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        var message = modelBuilder.Entity<Message>();
        message.ToTable("agent_messages");
        message.HasKey(x => x.Id);
        message.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        message.Property(x => x.ConversationId).IsRequired();
        message.Property(x => x.Role).IsRequired().HasConversion<string>().HasMaxLength(20);
        message.Property(x => x.Content).IsRequired();
        message.Property(x => x.ToolCallsJson).HasColumnType("jsonb");
        message.Property(x => x.ToolResultsJson).HasColumnType("jsonb");
        message.Property(x => x.CreatedAt).IsRequired();
        message.HasIndex(x => new { x.ConversationId, x.CreatedAt })
            .HasDatabaseName("idx_agent_messages_conversation_created");

        // A message carries no UserId of its own; it belongs to whoever owns its conversation.
        message.HasQueryFilter(
            OwnerQueryFilter.Name,
            x => Conversations.Any(c => c.Id == x.ConversationId && c.UserId == CurrentUserId));
    }
}
