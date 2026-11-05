namespace FinanceSentry.Infrastructure.Auditing;

using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Puts every save of an <see cref="AuditedProfileDataAttribute"/> entity on the audit trail, once per row, before the
/// save commits: no write of declared profile data goes unrecorded, and the handler that makes it does not have to
/// remember to. Records the entity's resource type, its id and the NAMES of the fields it wrote - for a new row every
/// field, for an edit the fields whose value changed, for a delete none. It never reads a value out of the entity.
/// <para>
/// A Modified row with no changed field (a re-save, or a key rotation rewriting ciphertext) records nothing: it wrote
/// no new fact. Bookkeeping columns (<c>CreatedAt</c>, <c>UpdatedAt</c>) are not fields of the profile.
/// </para>
/// <para>
/// The audit row is written first, in its own context, so a failed audit stops the write. The reverse failure - the
/// audit row is stored and the write then fails - leaves a trail entry for an attempted write, which is the safe side.
/// </para>
/// </summary>
public sealed class AuditedWriteSaveChangesInterceptor(IAuditTrail trail, IAuditActor actor) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Bookkeeping = new(StringComparer.Ordinal) { "CreatedAt", "UpdatedAt" };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        // EF's synchronous save has no async hook; the audit store call is awaited on the calling thread, which has no
        // synchronization context in ASP.NET Core or a job.
        RecordAsync(eventData.Context, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await RecordAsync(eventData.Context, cancellationToken);
        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private Task RecordAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null)
            return Task.CompletedTask;

        context.ChangeTracker.DetectChanges();
        var entries = new List<AuditEntry>();
        foreach (var tracked in context.ChangeTracker.Entries())
        {
            if (tracked.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            var marker = (AuditedProfileDataAttribute?)Attribute.GetCustomAttribute(
                tracked.Metadata.ClrType, typeof(AuditedProfileDataAttribute), inherit: false);
            if (marker is null)
                continue;

            var entry = ToAuditEntry(tracked, marker);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries.Count == 0 ? Task.CompletedTask : trail.RecordAsync(entries, ct);
    }

    private AuditEntry? ToAuditEntry(EntityEntry tracked, AuditedProfileDataAttribute marker)
    {
        if (tracked.Entity is not IUserOwned owned)
        {
            throw new InvalidOperationException(
                $"{tracked.Metadata.DisplayName()} is marked [AuditedProfileData] but is not IUserOwned; the audit trail needs to know whose data it is.");
        }

        var fields = tracked.State switch
        {
            EntityState.Added => tracked.Properties.Where(IsField),
            EntityState.Modified => tracked.Properties.Where(p => IsField(p) && p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)),
            _ => [],
        };

        var names = fields.Select(p => p.Metadata.Name).ToList();
        if (tracked.State == EntityState.Modified && names.Count == 0)
            return null;

        var action = tracked.State switch
        {
            EntityState.Added => AuditAction.Create,
            EntityState.Modified => AuditAction.Update,
            _ => AuditAction.Delete,
        };

        return new AuditEntry(owned.UserId, actor.UserId, actor.Kind, action, marker.ResourceType, ResourceIdOf(tracked), names);
    }

    private static bool IsField(PropertyEntry property)
        => !property.Metadata.IsPrimaryKey()
            && !property.Metadata.IsShadowProperty()
            && property.Metadata.Name != nameof(IUserOwned.UserId)
            && !Bookkeeping.Contains(property.Metadata.Name);

    private static Guid? ResourceIdOf(EntityEntry tracked)
        => tracked.Metadata.FindPrimaryKey()?.Properties is [{ ClrType: var type } key] && type == typeof(Guid)
            ? (Guid)tracked.Property(key.Name).CurrentValue!
            : null;
}

public static class AuditedWriteExtensions
{
    /// <summary>Registers the interceptor; it is scoped because the trail and the actor belong to the unit of work.</summary>
    public static IServiceCollection AddAuditedWrites(this IServiceCollection services)
        => services.AddScoped<AuditedWriteSaveChangesInterceptor>();

    /// <summary>Attaches it to a context holding <see cref="AuditedProfileDataAttribute"/> entities; call it from that context's <c>AddDbContext</c> factory.</summary>
    public static DbContextOptionsBuilder UseAuditedWrites(this DbContextOptionsBuilder options, IServiceProvider serviceProvider)
        => options.AddInterceptors(serviceProvider.GetRequiredService<AuditedWriteSaveChangesInterceptor>());
}
