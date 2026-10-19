namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Modules.Wealth.Domain;
using Microsoft.EntityFrameworkCore;

public class WealthDbContext(DbContextOptions<WealthDbContext> options, ICurrentUser currentUser)
    : OwnerScopedDbContext(options, currentUser)
{
    /// <summary>Name of the soft-delete filter on <see cref="ManualHolding"/>; the scope-change reader opts out by name.</summary>
    public const string ManualHoldingActiveFilterName = "NotRemoved";

    private const int MoneyPrecision = 18;
    private const int MoneyScale = 2;

    public DbSet<NetWorthSnapshot> NetWorthSnapshots { get; set; } = null!;

    public DbSet<NetWorthSnapshotSource> NetWorthSnapshotSources { get; set; } = null!;

    public DbSet<ManualHolding> ManualHoldings { get; set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.HaveGeneratedGuidKeyDefault().HaveDecimalPrecision(MoneyPrecision, MoneyScale);
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<NetWorthSnapshot>();
        e.ToTable("net_worth_snapshots");
        e.HasKey(s => s.Id);
        e.Property(s => s.UserId).IsRequired();
        e.Property(s => s.SnapshotDate).IsRequired();
        e.Property(s => s.BankingTotal).IsRequired();
        e.Property(s => s.BrokerageTotal).IsRequired();
        e.Property(s => s.CryptoTotal).IsRequired();
        e.Property(s => s.TotalNetWorth).IsRequired();
        e.Property(s => s.Currency).IsRequired().HasMaxLength(3);
        e.Property(s => s.TakenAt).HasDefaultValueSql("now()");
        e.Property(s => s.StaleSleeves).HasColumnName("stale_sleeves").HasMaxLength(64);
        e.Property(s => s.IsApproximate).HasColumnName("is_approximate").HasDefaultValue(false).IsRequired();
        e.Property(s => s.CashTotal).HasColumnName("cash_total");
        e.Property(s => s.BrokerageInvested).HasColumnName("brokerage_invested");
        e.Property(s => s.CryptoInvested).HasColumnName("crypto_invested");
        // Stored as its ordinal (the worst status is the maximum), not under the enum-to-string convention.
        e.Property(s => s.FxStatus).HasColumnName("fx_status").HasConversion<int>();

        e.HasIndex(s => new { s.UserId, s.SnapshotDate })
            .IsDescending(false, true)
            .HasDatabaseName("idx_net_worth_snapshot_user_date");

        e.HasIndex(s => new { s.UserId, s.SnapshotDate })
            .IsUnique()
            .HasDatabaseName("idx_net_worth_snapshot_user_date_unique");

        // One row per registered source per snapshot (framework §5.6, step 2). Amounts take the precision of the
        // snapshot's own totals and are in its Currency; kind and health are stored by name (the enum convention).
        var source = modelBuilder.Entity<NetWorthSnapshotSource>();
        source.ToTable("net_worth_snapshot_sources");
        source.HasKey(x => x.Id);
        source.Property(x => x.SnapshotId).HasColumnName("snapshot_id").IsRequired();
        source.Property(x => x.SourceId).HasColumnName("source_id").IsRequired().HasMaxLength(64);
        source.Property(x => x.Kind).HasColumnName("kind").IsRequired().HasMaxLength(32);
        source.Property(x => x.Total).HasColumnName("total");
        source.Property(x => x.Cash).HasColumnName("cash");
        source.Property(x => x.Invested).HasColumnName("invested");
        source.Property(x => x.Health).HasColumnName("health").IsRequired().HasMaxLength(16);
        source.Property(x => x.Reasons).HasColumnName("reasons").HasColumnType("text[]").IsRequired();
        source.HasOne<NetWorthSnapshot>()
            .WithMany()
            .HasForeignKey(x => x.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        source.HasIndex(x => new { x.SnapshotId, x.SourceId })
            .IsUnique()
            .HasDatabaseName("idx_net_worth_snapshot_sources_snapshot_source");

        // A source row carries no UserId of its own (so it is not IUserOwned); it belongs to whoever owns its snapshot.
        source.HasQueryFilter(
            OwnerQueryFilter.Name,
            x => NetWorthSnapshots.Any(s => s.Id == x.SnapshotId && s.UserId == CurrentUserId));

        // Values the person enters by hand (framework §5.5). Amount is in the row's own Currency; convertibility is
        // stored by name (the enum convention). Reliability is always L3, so it is not a column.
        var manual = modelBuilder.Entity<ManualHolding>();
        manual.ToTable("manual_holdings");
        manual.HasQueryFilter(ManualHoldingActiveFilterName, x => x.RemovedAt == null);
        manual.HasKey(x => x.Id);
        manual.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        manual.Property(x => x.Label).HasColumnName("label").IsRequired().HasMaxLength(ManualHoldingLimits.LabelMaxLength);
        manual.Property(x => x.Amount).HasColumnName("amount");
        manual.Property(x => x.Currency).HasColumnName("currency").IsRequired().HasMaxLength(3);
        manual.Property(x => x.AsOf).HasColumnName("as_of");
        manual.Property(x => x.Convertibility).HasColumnName("convertibility").IsRequired().HasMaxLength(16);
        manual.Property(x => x.CreatedAt).HasColumnName("created_at");
        manual.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        manual.Property(x => x.RemovedAt).HasColumnName("removed_at");
        manual.HasIndex(x => x.UserId).HasDatabaseName("idx_manual_holdings_user");
    }
}
