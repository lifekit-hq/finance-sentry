namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Wealth.Domain;
using Microsoft.EntityFrameworkCore;

public class WealthDbContext(DbContextOptions<WealthDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<NetWorthSnapshot> NetWorthSnapshots { get; set; } = null!;

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var e = modelBuilder.Entity<NetWorthSnapshot>();
        e.ToTable("net_worth_snapshots");
        e.HasKey(s => s.Id);
        e.Property(s => s.Id).HasDefaultValueSql("gen_random_uuid()");
        e.Property(s => s.UserId).IsRequired();
        e.Property(s => s.SnapshotDate).IsRequired();
        e.Property(s => s.BankingTotal).HasColumnType("numeric(18,2)").IsRequired();
        e.Property(s => s.BrokerageTotal).HasColumnType("numeric(18,2)").IsRequired();
        e.Property(s => s.CryptoTotal).HasColumnType("numeric(18,2)").IsRequired();
        e.Property(s => s.TotalNetWorth).HasColumnType("numeric(18,2)").IsRequired();
        e.Property(s => s.Currency).IsRequired().HasMaxLength(3);
        e.Property(s => s.TakenAt).HasDefaultValueSql("now()");
        e.Property(s => s.StaleSleeves).HasColumnName("stale_sleeves").HasMaxLength(64);
        e.Property(s => s.IsApproximate).HasColumnName("is_approximate").HasDefaultValue(false).IsRequired();
        e.Property(s => s.CashTotal).HasColumnName("cash_total").HasColumnType("numeric(18,2)");
        e.Property(s => s.BrokerageInvested).HasColumnName("brokerage_invested").HasColumnType("numeric(18,2)");
        e.Property(s => s.CryptoInvested).HasColumnName("crypto_invested").HasColumnType("numeric(18,2)");

        e.HasIndex(s => new { s.UserId, s.SnapshotDate })
            .IsDescending(false, true)
            .HasDatabaseName("idx_net_worth_snapshot_user_date");

        e.HasIndex(s => new { s.UserId, s.SnapshotDate })
            .IsUnique()
            .HasDatabaseName("idx_net_worth_snapshot_user_date_unique");

        e.HasQueryFilter(OwnerQueryFilter.Name, s => s.UserId == CurrentUserId);
    }
}
