namespace FinanceSentry.Modules.Events.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Events.Domain;
using Microsoft.EntityFrameworkCore;

public class EventsDbContext(DbContextOptions<EventsDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string Schema = "events";

    public DbSet<EventVerdict> Verdicts => Set<EventVerdict>();

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<EventVerdict>(e =>
        {
            e.ToTable("event_verdicts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.CompanionEventId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.AlertId });
            e.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
            e.Property(x => x.Verdict).HasMaxLength(EventVerdict.MaxVerdictLength);
        });
    }
}
