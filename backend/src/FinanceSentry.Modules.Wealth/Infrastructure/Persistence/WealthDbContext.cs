namespace FinanceSentry.Modules.Wealth.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Modules.Wealth.Domain;
using Microsoft.EntityFrameworkCore;

public class WealthDbContext(DbContextOptions<WealthDbContext> options, ICurrentUser currentUser)
    : OwnerScopedDbContext(options, currentUser)
{
    private const int MoneyPrecision = 18;
    private const int MoneyScale = 2;

    public DbSet<NetWorthSnapshot> NetWorthSnapshots { get; set; } = null!;

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
    }
}
