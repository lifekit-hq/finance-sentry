using System.Text.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanceSentry.Modules.Auth.Infrastructure.Persistence;

/// <summary>
/// Exports the person's account: the profile and preferences on the Identity user, their roles and their per-person
/// permission grants. Never the password hash or security stamps, the sign-in links to the identity provider, or any
/// refresh token, MCP code or MCP service token (the credential rows <see cref="AuthUserDataEraser"/> deletes): those
/// let the holder act as the person, they are not data about them. Written column by column, not by serialising the
/// Identity entity, so a column Identity adds later is not exported by accident.
/// </summary>
internal sealed class AuthUserDataExporter(AuthDbContext db) : IUserDataExporter
{
    public string Module => "Auth";

    public async Task ExportAsync(Guid userId, Utf8JsonWriter writer, CancellationToken ct = default)
    {
        var key = userId.ToString();

        var users = await db.Users.AsNoTracking()
            .Where(u => u.Id == key)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.EmailConfirmed,
                u.FirstName,
                u.LastName,
                u.BaseCurrency,
                u.Theme,
                u.ThemeSeed,
                u.ThemeIntensity,
                u.EmailAlerts,
                u.LowBalanceAlerts,
                u.LowBalanceThreshold,
                u.SyncFailureAlerts,
                u.WatchlistAnalystAlerts,
                u.SafeWithdrawalRate,
                u.RealAnnualReturn,
                u.HideEmptyBalances,
                u.HideSmallBalances,
                u.SmallBalanceThreshold,
            })
            .ToListAsync(ct);
        writer.WritePropertyName("users");
        OwnedRowExporter.WriteJson(writer, users, users.GetType(), ExportJson.Options);

        var roles = await (from userRole in db.UserRoles.AsNoTracking()
                           join role in db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                           where userRole.UserId == key
                           select role.Name)
            .ToListAsync(ct);
        writer.WritePropertyName("roles");
        OwnedRowExporter.WriteJson(writer, roles, roles.GetType(), ExportJson.Options);

        var grants = await db.UserClaims.AsNoTracking()
            .Where(c => c.UserId == key)
            .Select(c => new { c.ClaimType, c.ClaimValue })
            .ToListAsync(ct);
        writer.WritePropertyName("permissionGrants");
        OwnedRowExporter.WriteJson(writer, grants, grants.GetType(), ExportJson.Options);

        // The trail of access to their own declared data: ids and field names, as stored. Never other people's events.
        var auditEvents = await db.AuditEvents.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .Where(e => e.SubjectUserId == userId)
            .OrderBy(e => e.OccurredAt)
            .Select(e => new
            {
                e.Id,
                e.OccurredAt,
                ActorKind = e.ActorKind.ToString(),
                Action = e.Action.ToString(),
                e.ResourceType,
                e.ResourceId,
                e.FieldNames,
                e.TraceId,
            })
            .ToListAsync(ct);
        writer.WritePropertyName("audit_events");
        OwnedRowExporter.WriteJson(writer, auditEvents, auditEvents.GetType(), ExportJson.Options);
    }
}
