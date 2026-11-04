namespace FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Modules.Subscriptions.Domain;
using Microsoft.EntityFrameworkCore;

public class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    private const int MoneyPrecision = 15;
    private const int MoneyScale = 2;
    private const int ChangePercentPrecision = 9;
    private const int ChangePercentScale = 2;

    public DbSet<DetectedSubscription> DetectedSubscriptions { get; set; } = null!;
    public DbSet<SubscriptionPriceChange> SubscriptionPriceChanges { get; set; } = null!;
    public DbSet<MerchantBrand> MerchantBrands { get; set; } = null!;
    public DbSet<MerchantIcon> MerchantIcons { get; set; } = null!;

    // Read by the Owner query filter on every query this context runs. The UserId of DetectedSubscription and
    // SubscriptionPriceChange is a string
    // written as Guid.ToString() (lower-case "D" format) by every writer, so the acting person's id is compared in
    // that same form; null (no person in scope) matches no row.
    private string? CurrentUserKey => currentUser.UserId?.ToString();

    // Not an OwnerScopedDbContext: that base scopes IUserOwned entities, whose UserId is a Guid, and this table keys the
    // person by string. The Owner filter below stays hand-written; the column conventions are the shared ones.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        PersistenceConventions.Apply(configurationBuilder);
        configurationBuilder.HaveGeneratedGuidKeyDefault().HaveDecimalPrecision(MoneyPrecision, MoneyScale);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var sb = modelBuilder.Entity<DetectedSubscription>();
        sb.ToTable("detected_subscriptions");
        sb.HasKey(s => s.Id);
        sb.HasQueryFilter(OwnerQueryFilter.Name, s => s.UserId == CurrentUserKey);
        sb.Property(s => s.UserId).IsRequired().HasMaxLength(450);
        sb.Property(s => s.MerchantNameNormalized).IsRequired().HasMaxLength(200);
        sb.Property(s => s.MerchantNameDisplay).IsRequired().HasMaxLength(200);
        sb.Property(s => s.Cadence).IsRequired().HasMaxLength(10);
        sb.Property(s => s.AverageAmount).IsRequired();
        sb.Property(s => s.LastKnownAmount).IsRequired();
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

        // A change belongs to its subscription but carries the owner itself, so the Owner filter and the person's
        // export and erasure need no join. No foreign key: a subscription row can be deleted or re-keyed without
        // taking its history with it.
        var pc = modelBuilder.Entity<SubscriptionPriceChange>();
        pc.ToTable("subscription_price_changes");
        pc.HasKey(c => c.Id);
        pc.HasQueryFilter(OwnerQueryFilter.Name, c => c.UserId == CurrentUserKey);
        pc.Property(c => c.UserId).IsRequired().HasMaxLength(450);
        pc.Property(c => c.OldAmount).IsRequired();
        pc.Property(c => c.NewAmount).IsRequired();
        pc.Property(c => c.Currency).IsRequired().HasMaxLength(3);
        pc.Property(c => c.ChangePercent).IsRequired().HasPrecision(ChangePercentPrecision, ChangePercentScale);
        pc.Property(c => c.DetectedAt).HasDefaultValueSql("now()");
        pc.Ignore(c => c.IsIncrease);

        pc.HasIndex(c => new { c.SubscriptionId, c.ChargeDate })
            .HasDatabaseName("idx_subscription_price_change_subscription_date");

        pc.HasIndex(c => c.UserId)
            .HasDatabaseName("idx_subscription_price_change_user");

        // Shared reference data, not per-user: no owner filter (see MerchantBrand).
        var mb = modelBuilder.Entity<MerchantBrand>();
        mb.ToTable("merchant_brands");
        mb.HasKey(b => b.Id);
        mb.Property(b => b.Keyword).IsRequired().HasMaxLength(100);
        mb.Property(b => b.Domain).IsRequired().HasMaxLength(253);
        mb.HasIndex(b => b.Keyword).IsUnique().HasDatabaseName("idx_merchant_brand_keyword_unique");

        var mi = modelBuilder.Entity<MerchantIcon>();
        mi.ToTable("merchant_icons");
        mi.HasKey(i => i.Domain);
        mi.Property(i => i.Domain).HasMaxLength(253);
        mi.Property(i => i.ContentType).HasMaxLength(50);
        mi.Ignore(i => i.IsNegative);
    }
}
