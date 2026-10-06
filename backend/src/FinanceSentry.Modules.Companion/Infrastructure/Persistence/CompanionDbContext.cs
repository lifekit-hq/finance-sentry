namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Domain;
using Microsoft.EntityFrameworkCore;

public class CompanionDbContext(DbContextOptions<CompanionDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public const string Schema = "companion";

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    public DbSet<CompanionNotificationSetting> NotificationSettings => Set<CompanionNotificationSetting>();

    public DbSet<CompanionEvent> Events => Set<CompanionEvent>();

    public DbSet<CompanionCaptureState> CaptureState => Set<CompanionCaptureState>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CompanionNotificationSetting>(e =>
        {
            e.ToTable("companion_notification_settings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
        });

        modelBuilder.Entity<CompanionEvent>(e =>
        {
            e.ToTable("companion_events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DedupKey).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Disposition, x.OccurredAt });
            e.HasIndex(x => new { x.UserId, x.CapturedAt });
            e.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Subject).HasMaxLength(128);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.Property(x => x.SourceModule).HasMaxLength(32);
            e.Property(x => x.LastError).HasMaxLength(1000);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.ToTable("push_subscriptions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
            e.Property(x => x.Endpoint).HasMaxLength(PushSubscriptionLimits.EndpointMaxLength);
            e.Property(x => x.P256dh).HasMaxLength(PushSubscriptionLimits.P256dhMaxLength);
            e.Property(x => x.Auth).HasMaxLength(PushSubscriptionLimits.AuthMaxLength);
            e.Property(x => x.DeviceLabel).HasMaxLength(PushSubscriptionLimits.DeviceLabelMaxLength);
        });

        modelBuilder.Entity<PushDelivery>(e =>
        {
            e.ToTable("push_deliveries");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EventId, x.SubscriptionId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne<CompanionEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PushSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<CompanionCaptureState>(e =>
        {
            e.ToTable("companion_capture_state");
            e.HasKey(x => x.Source);
            e.Property(x => x.Source).HasMaxLength(64);
        });
    }
}
