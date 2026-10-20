namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion.Domain;
using Microsoft.EntityFrameworkCore;

public class CompanionDbContext(DbContextOptions<CompanionDbContext> options, ICurrentUser currentUser)
    : OwnerScopedDbContext(options, currentUser)
{
    public const string Schema = "companion";

    public DbSet<CompanionNotificationSetting> NotificationSettings => Set<CompanionNotificationSetting>();

    public DbSet<CompanionEvent> Events => Set<CompanionEvent>();

    public DbSet<CompanionCaptureState> CaptureState => Set<CompanionCaptureState>();

    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();

    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();

    public DbSet<ProblemReport> ProblemReports => Set<ProblemReport>();

    public DbSet<LedgerCorrection> LedgerCorrections => Set<LedgerCorrection>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<CompanionNotificationSetting>(e =>
        {
            e.ToTable("companion_notification_settings");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.Mode).HasMaxLength(16);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
        });

        modelBuilder.Entity<CompanionEvent>(e =>
        {
            e.ToTable("companion_events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.DedupKey).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Disposition, x.OccurredAt });
            e.HasIndex(x => new { x.UserId, x.CapturedAt });
            e.Property(x => x.Kind).HasMaxLength(24);
            e.Property(x => x.Disposition).HasMaxLength(24);
            e.Property(x => x.Subject).HasMaxLength(128);
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.Property(x => x.SourceModule).HasMaxLength(32);
            e.Property(x => x.AppPath).HasMaxLength(500);
            e.Property(x => x.LastError).HasMaxLength(1000);
        });

        modelBuilder.Entity<PushSubscription>(e =>
        {
            e.ToTable("push_subscriptions");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => x.UserId);
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
            e.HasIndex(x => new { x.AlertId, x.SubscriptionId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne<CompanionEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PushSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasMaxLength(16);
        });

        modelBuilder.Entity<ProblemReport>(e =>
        {
            e.ToTable("problem_reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.Property(x => x.Role).HasMaxLength(16);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Device).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Text).HasMaxLength(ProblemReportLimits.TextMaxLength);
            e.Property(x => x.RoutePattern).HasMaxLength(ProblemReportLimits.RouteMaxLength);
            e.Property(x => x.AppVersion).HasMaxLength(ProblemReportLimits.VersionMaxLength);
            e.Property(x => x.Client).HasMaxLength((2 * ProblemReportLimits.ClientPartMaxLength) + 1);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.LastError).HasMaxLength(300);
        });

        modelBuilder.Entity<LedgerCorrection>(e =>
        {
            e.ToTable("ledger_corrections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.UserId, x.CorrectedAt });
            e.Property(x => x.AgentSaid).HasMaxLength(LedgerCorrectionLimits.AgentSaidMaxLength);
            e.Property(x => x.WhatWasWrong).HasMaxLength(LedgerCorrectionLimits.WhatWasWrongMaxLength);
            e.Property(x => x.OwnerCorrection).HasMaxLength(LedgerCorrectionLimits.OwnerCorrectionMaxLength);
        });

        modelBuilder.Entity<CompanionCaptureState>(e =>
        {
            e.ToTable("companion_capture_state");
            e.HasKey(x => x.Source);
            e.Property(x => x.Source).HasMaxLength(64);
        });
    }
}
