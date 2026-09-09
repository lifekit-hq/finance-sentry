namespace FinanceSentry.Modules.BankSync.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using FinanceSentry.Modules.BankSync.Domain;

public class BankSyncDbContext(DbContextOptions<BankSyncDbContext> options) : DbContext(options)
{
    public DbSet<BankAccount> BankAccounts { get; set; } = null!;
    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<SyncJob> SyncJobs { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<MonobankCredential> MonobankCredentials { get; set; } = null!;
    public DbSet<TrueLayerConnection> TrueLayerConnections { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<MccCategory> MccCategories { get; set; } = null!;
    public DbSet<MerchantKeyword> MerchantKeywords { get; set; } = null!;
    public DbSet<Counterparty> Counterparties { get; set; } = null!;
    public DbSet<CounterpartyRule> CounterpartyRules { get; set; } = null!;
    public DbSet<CommittedMerchantPin> CommittedMerchantPins { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("bank_sync");
        base.OnModelCreating(modelBuilder);

        var bab = modelBuilder.Entity<BankAccount>();
        bab.HasKey(ba => ba.Id);
        bab.HasIndex(ba => ba.UserId).HasDatabaseName("idx_bank_account_user_id");
        bab.HasIndex(ba => ba.SyncStatus).HasDatabaseName("idx_bank_account_sync_status");
        bab.HasIndex(ba => ba.ExternalAccountId).IsUnique().HasDatabaseName("idx_bank_account_external_account_id_unique");
        bab.Property(ba => ba.ExternalAccountId).IsRequired().HasMaxLength(64);
        bab.Property(ba => ba.Provider).IsRequired().HasMaxLength(20);
        bab.Property(ba => ba.BankName).IsRequired().HasMaxLength(255);
        bab.Property(ba => ba.AccountType).IsRequired().HasMaxLength(50);
        bab.Property(ba => ba.AccountNumberLast4).IsRequired().HasMaxLength(4);
        bab.Property(ba => ba.OwnerName).IsRequired().HasMaxLength(255);
        bab.Property(ba => ba.Currency).IsRequired().HasMaxLength(3).HasDefaultValue("EUR");
        bab.Property(ba => ba.CurrentBalance).HasPrecision(15, 2);
        bab.Property(ba => ba.CreditLimit).HasPrecision(15, 2);
        bab.Property(ba => ba.SyncStatus).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        bab.Property(ba => ba.IsActive).HasDefaultValue(true);
        bab.Property(ba => ba.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        bab.HasMany(ba => ba.Transactions).WithOne(t => t.Account).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        bab.HasMany(ba => ba.SyncJobs).WithOne(sj => sj.Account).HasForeignKey(sj => sj.AccountId).OnDelete(DeleteBehavior.Cascade);
        bab.HasOne(ba => ba.MonobankCredential).WithMany(mc => mc.BankAccounts).HasForeignKey(ba => ba.MonobankCredentialId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);
        bab.HasOne(ba => ba.TrueLayerConnection).WithMany(tc => tc.BankAccounts).HasForeignKey(ba => ba.TrueLayerConnectionId).OnDelete(DeleteBehavior.SetNull).IsRequired(false);

        var mcb = modelBuilder.Entity<MonobankCredential>();
        mcb.HasKey(mc => mc.Id);
        mcb.HasIndex(mc => mc.UserId).HasDatabaseName("idx_monobank_credential_user_id");
        mcb.HasIndex(mc => mc.UserId).IsUnique().HasDatabaseName("idx_monobank_credential_user_unique");
        mcb.Property(mc => mc.EncryptedToken).IsRequired();
        mcb.Property(mc => mc.Iv).IsRequired();
        mcb.Property(mc => mc.AuthTag).IsRequired();
        mcb.Property(mc => mc.KeyVersion).HasDefaultValue(1);
        mcb.Property(mc => mc.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var tlb = modelBuilder.Entity<TrueLayerConnection>();
        tlb.HasKey(tc => tc.Id);
        tlb.HasIndex(tc => tc.UserId).HasDatabaseName("idx_truelayer_connection_user_id");
        tlb.HasIndex(tc => new { tc.UserId, tc.ProviderId }).IsUnique().HasDatabaseName("idx_truelayer_connection_user_provider_unique");
        tlb.HasIndex(tc => tc.Reference).IsUnique().HasDatabaseName("idx_truelayer_connection_reference_unique");
        tlb.Property(tc => tc.ProviderId).IsRequired().HasMaxLength(64);
        tlb.Property(tc => tc.ProviderDisplayName).IsRequired().HasMaxLength(255);
        tlb.Property(tc => tc.Reference).IsRequired().HasMaxLength(64);
        tlb.Property(tc => tc.Status).IsRequired().HasMaxLength(20).HasDefaultValue("CREATED");
        tlb.Property(tc => tc.EncryptedRefreshToken).IsRequired();
        tlb.Property(tc => tc.Iv).IsRequired();
        tlb.Property(tc => tc.AuthTag).IsRequired();
        tlb.Property(tc => tc.KeyVersion).HasDefaultValue(1);
        tlb.Property(tc => tc.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var tb = modelBuilder.Entity<Transaction>();
        tb.HasKey(t => t.Id);
        tb.HasIndex(t => t.AccountId).HasDatabaseName("idx_transaction_account_id");
        tb.HasIndex(t => t.PostedDate).HasDatabaseName("idx_transaction_posted_date");
        tb.HasIndex(t => t.CreatedAt).HasDatabaseName("idx_transaction_created_at");
        tb.HasIndex(t => new { t.AccountId, t.UniqueHash }).IsUnique().HasDatabaseName("idx_transaction_account_unique_hash_unique");
        tb.Property(t => t.Amount).IsRequired().HasPrecision(15, 2);
        tb.Property(t => t.Description).IsRequired();
        tb.Property(t => t.UniqueHash).IsRequired().HasMaxLength(64);
        tb.Property(t => t.TransactionType).HasMaxLength(50);
        tb.Property(t => t.MerchantName).HasMaxLength(255);
        tb.Property(t => t.MerchantCategory).HasMaxLength(100).IsRequired(false);
        tb.Property(t => t.Mcc).IsRequired(false);
        tb.Property(t => t.SourceCategory).HasMaxLength(100).IsRequired(false);
        tb.Property(t => t.IsActive).HasDefaultValue(true);
        tb.Property(t => t.DeletedAt).IsRequired(false);
        tb.Property(t => t.ArchivedReason).HasMaxLength(50).IsRequired(false);
        tb.HasQueryFilter(t => t.IsActive);
        tb.HasIndex(t => new { t.AccountId, t.IsActive }).HasDatabaseName("idx_transaction_account_active");
        tb.Property(t => t.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var cab = modelBuilder.Entity<Category>();
        cab.ToTable("categories");
        cab.HasKey(c => c.Key);
        cab.Property(c => c.Key).HasMaxLength(100);
        cab.Property(c => c.Label).IsRequired().HasMaxLength(100);
        cab.Property(c => c.SortOrder).IsRequired();

        var mccb = modelBuilder.Entity<MccCategory>();
        mccb.ToTable("mcc_categories");
        mccb.HasKey(mc => mc.Mcc);
        mccb.Property(mc => mc.Mcc).ValueGeneratedNever();
        mccb.Property(mc => mc.CategoryKey).IsRequired().HasMaxLength(100);
        mccb.Property(mc => mc.Description).IsRequired().HasMaxLength(255);
        mccb.HasIndex(mc => mc.CategoryKey).HasDatabaseName("idx_mcc_category_category_key");
        mccb.HasOne<Category>().WithMany().HasForeignKey(mc => mc.CategoryKey).OnDelete(DeleteBehavior.Restrict);

        var mkb = modelBuilder.Entity<MerchantKeyword>();
        mkb.ToTable("merchant_keywords");
        mkb.HasKey(mk => mk.Id);
        mkb.Property(mk => mk.Keyword).IsRequired().HasMaxLength(100);
        mkb.Property(mk => mk.CategoryKey).IsRequired().HasMaxLength(100);
        mkb.HasIndex(mk => mk.Keyword).IsUnique().HasDatabaseName("idx_merchant_keyword_keyword_unique");
        mkb.HasIndex(mk => mk.CategoryKey).HasDatabaseName("idx_merchant_keyword_category_key");
        mkb.HasOne<Category>().WithMany().HasForeignKey(mk => mk.CategoryKey).OnDelete(DeleteBehavior.Restrict);

        var cpb = modelBuilder.Entity<Counterparty>();
        cpb.ToTable("counterparties");
        cpb.HasKey(cp => cp.Id);
        cpb.Property(cp => cp.Name).IsRequired().HasMaxLength(255);
        cpb.Property(cp => cp.FlowRole).IsRequired().HasMaxLength(50);
        cpb.Property(cp => cp.UserId).IsRequired();
        cpb.HasIndex(cp => cp.UserId).HasDatabaseName("idx_counterparty_user_id");
        cpb.HasMany(cp => cp.Rules).WithOne(r => r.Counterparty).HasForeignKey(r => r.CounterpartyId).OnDelete(DeleteBehavior.Cascade);

        var crb = modelBuilder.Entity<CounterpartyRule>();
        crb.ToTable("counterparty_rules");
        crb.HasKey(r => r.Id);
        crb.Property(r => r.CounterpartyId).IsRequired();
        crb.Property(r => r.MatchType).IsRequired().HasMaxLength(50);
        crb.Property(r => r.Pattern).IsRequired().HasMaxLength(255);
        crb.Property(r => r.Currency).HasMaxLength(3);
        crb.HasIndex(r => r.CounterpartyId).HasDatabaseName("idx_counterparty_rule_counterparty_id");

        var cmp = modelBuilder.Entity<CommittedMerchantPin>();
        cmp.ToTable("committed_merchant_pins");
        cmp.HasKey(p => p.Id);
        cmp.Property(p => p.UserId).IsRequired();
        cmp.Property(p => p.MerchantKey).IsRequired().HasMaxLength(255);
        cmp.Property(p => p.DisplayName).IsRequired().HasMaxLength(255);
        // Unique per user: a pin is set membership, so a second row for the same key would be a
        // duplicate the policy could never tell apart.
        cmp.HasIndex(p => new { p.UserId, p.MerchantKey }).IsUnique()
           .HasDatabaseName("idx_committed_merchant_pin_user_merchant_unique");

        var sjb = modelBuilder.Entity<SyncJob>();
        sjb.HasKey(sj => sj.Id);
        sjb.HasIndex(sj => sj.AccountId).HasDatabaseName("idx_sync_job_account_id");
        sjb.HasIndex(sj => sj.Status).HasDatabaseName("idx_sync_job_status");
        sjb.HasIndex(sj => sj.CreatedAt).HasDatabaseName("idx_sync_job_created_at");
        sjb.Property(sj => sj.Status).IsRequired().HasMaxLength(50).HasDefaultValue("pending");
        sjb.Property(sj => sj.UserId).IsRequired();
        sjb.Property(sj => sj.CorrelationId).HasMaxLength(100).IsRequired(false);
        sjb.Property(sj => sj.ErrorMessage).HasMaxLength(1000);
        sjb.Property(sj => sj.ErrorCode).HasMaxLength(50);
        sjb.Property(sj => sj.TransactionCountFetched).HasDefaultValue(0);
        sjb.Property(sj => sj.TransactionCountDeduped).HasDefaultValue(0);
        sjb.Property(sj => sj.RetryCount).HasDefaultValue(0);
        sjb.HasIndex(sj => new { sj.AccountId, sj.Status }).HasDatabaseName("idx_sync_job_account_status");
        sjb.Property(sj => sj.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

        var alb = modelBuilder.Entity<AuditLog>();
        alb.ToTable("audit_logs");
        alb.HasKey(al => al.AuditId);
        alb.HasIndex(al => new { al.UserId, al.PerformedAt }).HasDatabaseName("idx_audit_log_user_performed_at");
        alb.HasIndex(al => new { al.ResourceType, al.ResourceId }).HasDatabaseName("idx_audit_log_resource");
        alb.Property(al => al.Action).IsRequired().HasMaxLength(50);
        alb.Property(al => al.ResourceType).IsRequired().HasMaxLength(50);
        alb.Property(al => al.IpAddress).HasMaxLength(45).IsRequired(false);
        alb.Property(al => al.CorrelationId).HasMaxLength(64).IsRequired(false);
        alb.Property(al => al.PerformedAt).IsRequired();
    }
}
