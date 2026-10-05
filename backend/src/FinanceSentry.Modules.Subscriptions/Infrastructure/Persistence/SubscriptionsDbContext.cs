namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

public class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<DetectedSubscription> DetectedSubscriptions { get; set; } = null!;

    // Read by the Owner query filter on every query this context runs. DetectedSubscription.UserId is a string
    // written as Guid.ToString() (lower-case "D" format) by every writer, so the acting person's id is compared in
    // that same form; null (no person in scope) matches no row.
    private string? CurrentUserKey => currentUser.UserId?.ToString();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var sb = modelBuilder.Entity<DetectedSubscription>();
        sb.ToTable("detected_subscriptions");
        sb.HasKey(s => s.Id);
        sb.HasQueryFilter(OwnerQueryFilter.Name, s => s.UserId == CurrentUserKey);
        sb.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        sb.Property(s => s.UserId).IsRequired().HasMaxLength(450);
        sb.Property(s => s.MerchantNameNormalized).IsRequired().HasMaxLength(200);
        sb.Property(s => s.MerchantNameDisplay).IsRequired().HasMaxLength(200);
        sb.Property(s => s.Cadence).IsRequired().HasMaxLength(10);
        sb.Property(s => s.AverageAmount).HasColumnType("numeric(15,2)").IsRequired();
        sb.Property(s => s.LastKnownAmount).HasColumnType("numeric(15,2)").IsRequired();
        sb.Property(s => s.PreviousAmount).HasColumnType("numeric(15,2)");
        sb.Property(s => s.Currency).IsRequired().HasMaxLength(3);
        sb.Property(s => s.Kind).IsRequired().HasMaxLength(20).HasDefaultValue(SubscriptionKinds.Subscription);
        sb.Property(s => s.Status).IsRequired().HasMaxLength(25).HasDefaultValue(SubscriptionStatus.Active);
        sb.Property(s => s.OccurrenceCount).HasDefaultValue(0);
        sb.Property(s => s.ConfidenceScore).HasDefaultValue(0);
        sb.Property(s => s.Category).HasMaxLength(50);
        sb.Property(s => s.TermCount);
        sb.Property(s => s.IsManual).HasDefaultValue(false);
        sb.Ignore(s => s.RemainingPayments);
        sb.Ignore(s => s.IsTracked);
        sb.Property(s => s.DetectedAt).HasDefaultValueSql("now()");
        sb.Property(s => s.UpdatedAt).HasDefaultValueSql("now()");

        sb.HasIndex(s => new { s.UserId, s.Status })
            .HasDatabaseName("idx_detected_subscription_user_status");

        sb.HasIndex(s => new { s.UserId, s.MerchantNameNormalized })
            .IsUnique()
            .HasDatabaseName("idx_detected_subscription_user_merchant");

        sb.HasIndex(s => new { s.UserId, s.LastChargeDate })
            .HasDatabaseName("idx_detected_subscription_last_charge")
            .HasFilter("\"Status\" = 'active'");
    }
}
