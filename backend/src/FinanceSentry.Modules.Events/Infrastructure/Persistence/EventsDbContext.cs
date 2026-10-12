namespace FinanceSentry.Modules.Events.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Events.Domain;
using Microsoft.EntityFrameworkCore;

public class EventsDbContext(DbContextOptions<EventsDbContext> options, ICurrentUser currentUser)
    : OwnerScopedDbContext(options, currentUser)
{
    public const string Schema = "events";

    public DbSet<EventVerdict> Verdicts => Set<EventVerdict>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<EventVerdict>(e =>
        {
            e.ToTable("event_verdicts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.CompanionEventId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.AlertId });
            e.Property(x => x.Verdict).HasMaxLength(EventVerdict.MaxVerdictLength);
        });
    }
}
