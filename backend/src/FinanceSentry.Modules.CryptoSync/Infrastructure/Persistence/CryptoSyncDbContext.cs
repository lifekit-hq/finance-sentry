using FinanceSentry.Modules.CryptoSync.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;

public sealed class CryptoSyncDbContext : DbContext
{
    public CryptoSyncDbContext(DbContextOptions<CryptoSyncDbContext> options)
        : base(options)
    {
    }

    public DbSet<ExchangeCredential> ExchangeCredentials => Set<ExchangeCredential>();
    public DbSet<CryptoHolding> CryptoHoldings => Set<CryptoHolding>();
    public DbSet<CryptoTradeRecord> CryptoTrades => Set<CryptoTradeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("crypto_sync");
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ExchangeCredential>(entity =>
        {
            entity.ToTable("ExchangeCredentials");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Provider }).IsUnique();
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.EncryptedApiKey).IsRequired();
            entity.Property(e => e.ApiKeyIv).IsRequired();
            entity.Property(e => e.ApiKeyAuthTag).IsRequired();
            entity.Property(e => e.EncryptedApiSecret).IsRequired();
            entity.Property(e => e.ApiSecretIv).IsRequired();
            entity.Property(e => e.ApiSecretAuthTag).IsRequired();
            entity.Property(e => e.KeyVersion).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.LastSyncError).HasMaxLength(1000);
        });

        modelBuilder.Entity<CryptoHolding>(entity =>
        {
            entity.ToTable("CryptoHoldings");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Provider, e.Asset }).IsUnique();
            entity.Property(e => e.Asset).IsRequired().HasMaxLength(20);
            entity.Property(e => e.FreeQuantity).HasPrecision(30, 10);
            entity.Property(e => e.LockedQuantity).HasPrecision(30, 10);
            entity.Property(e => e.UsdValue).HasPrecision(20, 4);
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CostBasisUsd).HasPrecision(20, 4);
            entity.Property(e => e.AverageBuyPriceUsd).HasPrecision(20, 8);
            entity.Property(e => e.RealizedPnlUsd).HasPrecision(20, 4);
            entity.Property(e => e.TradeCursor).HasMaxLength(200);
            entity.Property(e => e.TradeCount).IsRequired();
            entity.Property(e => e.IsFiat).IsRequired();
            entity.Property(e => e.TrackedQuantity).HasPrecision(30, 10);
            entity.Property(e => e.TrackedCostUsd).HasPrecision(30, 10);
            entity.Property(e => e.UntrackedQuantity).HasPrecision(30, 10);
        });

        modelBuilder.Entity<CryptoTradeRecord>(entity =>
        {
            entity.ToTable("CryptoTrades");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Provider, e.TradeId }).IsUnique();
            entity.Property(e => e.Provider).IsRequired().HasMaxLength(50);
            entity.Property(e => e.TradeId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Asset).IsRequired().HasMaxLength(20);
            entity.Property(e => e.QuoteAsset).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Quantity).HasPrecision(30, 10);
            entity.Property(e => e.PriceUsd).HasPrecision(20, 8);
            entity.Property(e => e.QuoteQuantityUsd).HasPrecision(20, 4);
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.RecordedAt).IsRequired();
        });
    }
}
