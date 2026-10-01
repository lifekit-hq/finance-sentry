namespace FinanceSentry.Modules.Risk.Infrastructure.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Risk.Domain;
using Microsoft.EntityFrameworkCore;

public class RiskDbContext(DbContextOptions<RiskDbContext> options, ICurrentUser currentUser) : DbContext(options)
{
    public DbSet<RiskRuleSet> RiskRuleSets { get; set; } = null!;

    public DbSet<PolicyViolationAck> PolicyViolationAcks { get; set; } = null!;

    public DbSet<HoldingSnapshot> HoldingSnapshots { get; set; } = null!;

    // Read by the Owner query filter on every query this context runs; null (no person in scope) matches no row.
    private Guid? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("risk");
        base.OnModelCreating(modelBuilder);

        var ruleSet = modelBuilder.Entity<RiskRuleSet>();
        ruleSet.ToTable("risk_rule_sets");
        ruleSet.HasKey(x => x.Id);
        ruleSet.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
        ruleSet.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        ruleSet.Property(x => x.UserId).IsRequired();
        ruleSet.Property(x => x.Version).IsRequired();
        ruleSet.Property(x => x.IsCurrent).IsRequired();
        ruleSet.Property(x => x.MaxPositionWeightPct).HasColumnType("numeric(9,6)");
        ruleSet.Property(x => x.MaxSleeveWeightPct).HasColumnType("numeric(9,6)");
        ruleSet.Property(x => x.MinCashBufferPct).HasColumnType("numeric(9,6)");
        ruleSet.Property(x => x.MaxLossPerThesisPct).HasColumnType("numeric(9,6)");
        ruleSet.Property(x => x.MaxNewPositionPct).HasColumnType("numeric(9,6)");
        ruleSet.HasIndex(x => new { x.UserId, x.IsCurrent }).HasDatabaseName("idx_risk_rule_sets_user_current");

        var ack = modelBuilder.Entity<PolicyViolationAck>();
        ack.ToTable("policy_violation_acks");
        ack.HasKey(x => x.Id);
        ack.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
        ack.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        ack.Property(x => x.UserId).IsRequired();
        ack.Property(x => x.RuleKey).IsRequired().HasMaxLength(50);
        ack.Property(x => x.Subject).IsRequired().HasMaxLength(40);
        ack.Property(x => x.RemediationNote).IsRequired().HasMaxLength(1000);
        ack.Property(x => x.WorseningStepPct).HasColumnType("numeric(9,6)");
        ack.Property(x => x.ObservedAtAck).HasColumnType("numeric(9,6)");
        ack.HasIndex(x => new { x.UserId, x.RuleKey, x.Subject, x.IsActive })
            .HasDatabaseName("idx_policy_violation_acks_identity");

        var snapshot = modelBuilder.Entity<HoldingSnapshot>();
        snapshot.ToTable("holding_snapshots");
        snapshot.HasKey(x => x.Id);
        snapshot.HasQueryFilter(OwnerQueryFilter.Name, x => x.UserId == CurrentUserId);
        snapshot.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        snapshot.Property(x => x.UserId).IsRequired();
        snapshot.Property(x => x.Symbol).IsRequired().HasMaxLength(40);
        snapshot.Property(x => x.Sleeve).IsRequired().HasMaxLength(20);
        snapshot.Property(x => x.Quantity).HasColumnType("numeric(24,8)");
        snapshot.Property(x => x.UsdValue).HasColumnType("numeric(18,2)");
        snapshot.HasIndex(x => new { x.UserId, x.Symbol, x.Sleeve, x.CapturedAt })
            .HasDatabaseName("idx_holding_snapshots_identity");
    }
}
