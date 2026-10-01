namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Radar.Domain;
using FinanceSentry.Modules.Radar.Domain.Regime;
using Microsoft.EntityFrameworkCore;

public class RadarDbContext(DbContextOptions<RadarDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no
    // holder-scoped signal.
    private Guid? CurrentUserId => currentUser.UserId;

    public DbSet<DailyBar> DailyBars { get; set; } = null!;

    public DbSet<RadarSignal> RadarSignals { get; set; } = null!;

    public DbSet<RadarUniverseMember> UniverseMembers { get; set; } = null!;

    public DbSet<RegimeReading> RegimeReadings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("radar");
        base.OnModelCreating(modelBuilder);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var bar = modelBuilder.Entity<DailyBar>();
        bar.ToTable("daily_bars");
        bar.HasKey(x => x.Id);
        bar.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        bar.Property(x => x.Ticker).IsRequired().HasMaxLength(20);
        bar.Property(x => x.Date).IsRequired();
        bar.Property(x => x.Open).HasColumnType("numeric(18,6)");
        bar.Property(x => x.High).HasColumnType("numeric(18,6)");
        bar.Property(x => x.Low).HasColumnType("numeric(18,6)");
        bar.Property(x => x.Close).HasColumnType("numeric(18,6)");
        bar.Property(x => x.AdjClose).HasColumnType("numeric(18,6)");
        bar.Property(x => x.Volume);
        bar.HasIndex(x => new { x.Ticker, x.Date }).IsUnique().HasDatabaseName("idx_daily_bars_ticker_date");

        var sig = modelBuilder.Entity<RadarSignal>();
        sig.ToTable("radar_signals");
        sig.HasKey(x => x.Id);
        // A null UserId is a global signal (breadth, rotation), readable by every person; a holder-scoped signal
        // only by its holder. Bars, universe members and regime readings are market data with no owner.
        sig.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == null || x.UserId == CurrentUserId);
        sig.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        sig.Property(x => x.Timestamp).IsRequired();
        sig.Property(x => x.Scanner).IsRequired().HasMaxLength(50);
        sig.Property(x => x.SignalType).IsRequired().HasMaxLength(50);
        sig.Property(x => x.Severity).IsRequired().HasConversion<string>().HasMaxLength(20);
        sig.Property(x => x.SubjectType).IsRequired().HasMaxLength(20);
        sig.Property(x => x.Subject).IsRequired().HasMaxLength(40);
        sig.Property(x => x.UserId);
        sig.Property(x => x.DedupKey).IsRequired().HasMaxLength(200);
        sig.Property(x => x.PayloadVersion).IsRequired().HasDefaultValue(1);
        sig.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, jsonOptions) ?? new());
        sig.HasIndex(x => x.Timestamp).HasDatabaseName("idx_radar_signals_timestamp");
        sig.HasIndex(x => new { x.Scanner, x.SignalType }).HasDatabaseName("idx_radar_signals_scanner_type");
        sig.HasIndex(x => x.Subject).HasDatabaseName("idx_radar_signals_subject");
        sig.HasIndex(x => x.DedupKey).HasDatabaseName("idx_radar_signals_dedup");

        var mem = modelBuilder.Entity<RadarUniverseMember>();
        mem.ToTable("radar_universe_members");
        mem.HasKey(x => x.Id);
        mem.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        mem.Property(x => x.Ticker).IsRequired().HasMaxLength(20);
        mem.Property(x => x.Kind).IsRequired().HasConversion<string>().HasMaxLength(20);
        mem.Property(x => x.Source).IsRequired().HasConversion<string>().HasMaxLength(10);
        mem.Property(x => x.Active).IsRequired();
        mem.HasIndex(x => x.Ticker).IsUnique().HasDatabaseName("idx_radar_universe_ticker");

        var reg = modelBuilder.Entity<RegimeReading>();
        reg.ToTable("regime_readings");
        reg.HasKey(x => x.Id);
        reg.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        reg.Property(x => x.ComputedAt).IsRequired();
        reg.Property(x => x.VolatilityAvailable).IsRequired();
        reg.Property(x => x.VolatilityRegime).HasConversion<string>().HasMaxLength(20);
        reg.Property(x => x.VixLevel).HasColumnType("numeric(10,4)");
        reg.Property(x => x.VixSma).HasColumnType("numeric(10,4)");
        reg.Property(x => x.VixTrend).HasConversion<string>().HasMaxLength(20);
        reg.Property(x => x.RatesAvailable).IsRequired();
        reg.Property(x => x.RatesRegime).HasConversion<string>().HasMaxLength(20);
        reg.Property(x => x.Dgs10).HasColumnType("numeric(10,4)");
        reg.Property(x => x.Dgs2).HasColumnType("numeric(10,4)");
        reg.Property(x => x.Spread).HasColumnType("numeric(10,4)");
        reg.Property(x => x.RecessionWarning).IsRequired();
        reg.Property(x => x.GrowthValueTilt).HasMaxLength(80);
        reg.HasIndex(x => x.ComputedAt).IsDescending().HasDatabaseName("idx_regime_readings_computed_at");
    }
}
