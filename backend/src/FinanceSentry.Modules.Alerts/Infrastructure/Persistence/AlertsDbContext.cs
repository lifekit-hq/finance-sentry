namespace FinanceSentry.Modules.Alerts.Infrastructure.Persistence;

using FinanceSentry.Modules.Alerts.Domain;
using Microsoft.EntityFrameworkCore;

public class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : DbContext(options)
{
    public DbSet<Alert> Alerts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("alerts");
        base.OnModelCreating(modelBuilder);

        var ab = modelBuilder.Entity<Alert>();
        ab.ToTable("alerts");
        ab.HasKey(a => a.Id);
        ab.Property(a => a.Id).HasDefaultValueSql("gen_random_uuid()");
        ab.Property(a => a.UserId).IsRequired();
        ab.Property(a => a.Type).IsRequired().HasMaxLength(30);
        ab.Property(a => a.Severity).IsRequired().HasMaxLength(10);
        ab.Property(a => a.Title).IsRequired().HasMaxLength(200);
        ab.Property(a => a.Message).IsRequired().HasMaxLength(1000);
        ab.Property(a => a.ReferenceLabel).HasMaxLength(200);
        ab.Property(a => a.IsRead).HasDefaultValue(false);
        ab.Property(a => a.IsResolved).HasDefaultValue(false);
        ab.Property(a => a.IsDismissed).HasDefaultValue(false);
        ab.Property(a => a.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        ab.Property(a => a.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        ab.Property(a => a.AcknowledgementDecision).HasMaxLength(10);
        ab.Property(a => a.OccurrenceCount).IsRequired().HasDefaultValue(1);
        ab.Property(a => a.LastOccurredAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        ab.HasIndex(a => new { a.UserId, a.CreatedAt })
            .HasDatabaseName("idx_alert_user_created")
            .IsDescending(false, true)
            .HasFilter("\"IsDismissed\" = false");

        ab.HasIndex(a => new { a.UserId, a.Type, a.ReferenceId })
            .IsUnique()
            .HasDatabaseName("idx_alert_dedup")
            .HasFilter("\"IsResolved\" = false AND \"IsDismissed\" = false");

        ab.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("idx_alert_purge")
            .HasFilter("\"IsResolved\" = true OR \"IsDismissed\" = true");
    }
}
