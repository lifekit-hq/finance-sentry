namespace FinanceSentry.Modules.Risk.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Modules.Risk.Domain;
using Microsoft.EntityFrameworkCore;

public class RiskDbContext(DbContextOptions<RiskDbContext> options, ICurrentUser currentUser)
    : OwnerScopedDbContext(options, currentUser)
{
    // The weight/percentage columns; the two money-like columns declare their own precision below.
    private const int PctPrecision = 9;
    private const int PctScale = 6;
    private const int QuantityPrecision = 24;
    private const int QuantityScale = 8;
    private const int UsdPrecision = 18;
    private const int UsdScale = 2;

    public DbSet<RiskRuleSet> RiskRuleSets { get; set; } = null!;

    public DbSet<PolicyViolationAck> PolicyViolationAcks { get; set; } = null!;

    public DbSet<HoldingSnapshot> HoldingSnapshots { get; set; } = null!;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.HaveGeneratedGuidKeyDefault().HaveDecimalPrecision(PctPrecision, PctScale);
    }

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("risk");

        var ruleSet = modelBuilder.Entity<RiskRuleSet>();
        ruleSet.ToTable("risk_rule_sets");
        ruleSet.HasKey(x => x.Id);
        ruleSet.Property(x => x.UserId).IsRequired();
        ruleSet.Property(x => x.Version).IsRequired();
        ruleSet.Property(x => x.IsCurrent).IsRequired();
        ruleSet.HasIndex(x => new { x.UserId, x.IsCurrent }).HasDatabaseName("idx_risk_rule_sets_user_current");

        var ack = modelBuilder.Entity<PolicyViolationAck>();
        ack.ToTable("policy_violation_acks");
        ack.HasKey(x => x.Id);
        ack.Property(x => x.UserId).IsRequired();
        ack.Property(x => x.RuleKey).IsRequired().HasMaxLength(50);
        ack.Property(x => x.Subject).IsRequired().HasMaxLength(40);
        ack.Property(x => x.RemediationNote).IsRequired().HasMaxLength(1000);
        ack.HasIndex(x => new { x.UserId, x.RuleKey, x.Subject, x.IsActive })
            .HasDatabaseName("idx_policy_violation_acks_identity");

        var snapshot = modelBuilder.Entity<HoldingSnapshot>();
        snapshot.ToTable("holding_snapshots");
        snapshot.HasKey(x => x.Id);
        snapshot.Property(x => x.UserId).IsRequired();
        snapshot.Property(x => x.Symbol).IsRequired().HasMaxLength(40);
        snapshot.Property(x => x.Sleeve).IsRequired().HasMaxLength(20);
        snapshot.Property(x => x.Quantity).HasPrecision(QuantityPrecision, QuantityScale);
        snapshot.Property(x => x.UsdValue).HasPrecision(UsdPrecision, UsdScale);
        snapshot.HasIndex(x => new { x.UserId, x.Symbol, x.Sleeve, x.CapturedAt })
            .HasDatabaseName("idx_holding_snapshots_identity");
    }
}
