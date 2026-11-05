using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Modules.Auth.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

// The Owner query filter covers the per-user credential rows (RefreshToken, McpAuthorizationCode). Identity's own
// tables (users, roles, user claims/roles/logins/tokens) stay unfiltered: sign-in, token refresh, invite acceptance,
// the per-request principal load and the role seed read them before a person is in scope, and managing people reads
// them across users. McpServiceToken is not filtered yet: its revocation check runs while the MCP host is still
// authenticating, before a person is in scope. AuditEvent is filtered to the person it concerns for reads; the trail
// writes through its own context and the purge job opts out. AccountClosure is the closure audit record, read by the purge job and by
// Owners managing people, never by the person it describes, so it is unfiltered. DataProtectionKeys is the platform's key ring, shared by every
// instance and read by the key manager with no person in scope, so it is unfiltered too.
public class AuthDbContext(DbContextOptions<AuthDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<McpAuthorizationCode> McpAuthorizationCodes => Set<McpAuthorizationCode>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<McpServiceToken> McpServiceTokens => Set<McpServiceToken>();
    public DbSet<AccountClosure> AccountClosures => Set<AccountClosure>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // Read by the Owner query filter on every query this context runs. Identity user ids are strings holding
    // Guid.ToString() (lower-case "D" format), so the acting person's id is compared in that same form; null (no
    // person in scope) matches no row.
    private string? CurrentUserKey => currentUser.UserId?.ToString();

    // The same person as a Guid, for the audit trail's subject column.
    private Guid? CurrentUserId => currentUser.UserId;

    // Not an OwnerScopedDbContext: it derives from IdentityDbContext<ApplicationUser>, and the person is keyed by string
    // (Identity's id), not the Guid that base scopes. The Owner filters above stay hand-written; the column conventions
    // are the shared ones. The Guid-key default and decimal precision stay off: this schema has neither.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        PersistenceConventions.Apply(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("auth");
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasQueryFilter(OwnerQueryFilter.Name, t => t.UserId == CurrentUserKey);
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.UserId).IsRequired();
        });

        builder.Entity<McpAuthorizationCode>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasQueryFilter(OwnerQueryFilter.Name, t => t.UserId == CurrentUserKey);
            entity.HasIndex(t => t.CodeHash).IsUnique();
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.UserId).IsRequired();
            entity.Property(t => t.Email).IsRequired();
            entity.Property(t => t.CodeHash).HasMaxLength(64).IsRequired();
            entity.Property(t => t.RedirectUri).IsRequired();
        });

        builder.Entity<McpServiceToken>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.UserId);
            entity.Property(t => t.UserId).IsRequired();
            entity.Property(t => t.Label).HasMaxLength(200).IsRequired();
        });

        // No foreign key to the user: the record survives the purge that deletes the account. At most one pending
        // closure per person, so a repeated close request finds the first instead of opening a second.
        builder.Entity<AccountClosure>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.UserId)
                .IsUnique()
                .HasFilter("\"ReopenedAt\" IS NULL AND \"PurgedAt\" IS NULL")
                .HasDatabaseName("IX_AccountClosures_UserId_Pending");
            entity.HasIndex(c => c.PurgeDueAt);
            entity.Property(c => c.CredentialRevocation).HasColumnType("jsonb");
            entity.Property(c => c.ErasedRows).HasColumnType("jsonb");
        });

        // Append-only: nothing updates a row. Filtered to the person the event concerns, so a request never lists
        // someone else's; the eraser opts out and names the subject. No foreign key to the user for the same reason as
        // AccountClosure. ResourceType and TraceId are identifiers, never values (AuditEntry guarantees the former).
        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(e => e.Id);
            entity.HasQueryFilter(OwnerQueryFilter.Name, e => e.SubjectUserId == CurrentUserId);
            // The column is the person key every module's export and erasure walk (and their tests) look for: UserId.
            entity.Property(e => e.SubjectUserId).HasColumnName("UserId");
            entity.HasIndex(e => new { e.SubjectUserId, e.OccurredAt });
            entity.HasIndex(e => e.OccurredAt);
            entity.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(16);
            entity.Property(e => e.ResourceType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.FieldNames).HasColumnType("text[]");
            entity.Property(e => e.TraceId).HasMaxLength(64).IsRequired();
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.ThemeSeed).HasMaxLength(7);
            entity.Property(u => u.SafeWithdrawalRate).HasDefaultValue(0.04m);
            entity.Property(u => u.RealAnnualReturn).HasDefaultValue(0.05m);
            entity.Property(u => u.HideEmptyBalances).HasDefaultValue(true);
            entity.Property(u => u.SmallBalanceThreshold).HasDefaultValue(1m);
        });
    }
}
