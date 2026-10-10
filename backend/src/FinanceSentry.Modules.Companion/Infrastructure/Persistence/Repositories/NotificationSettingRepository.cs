namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Reads run under the Owner filter. The capture, dispatch and digest jobs and the registration handler run with no
// person in scope, so they call <c>GetOrDefaultUnscopedAsync</c>, which opts out explicitly and keeps its UserId
// predicate; the upsert's existence check opts out too, so provisioning never inserts a duplicate row.
public class NotificationSettingRepository(CompanionDbContext db, IOptions<CompanionOptions> options)
    : INotificationSettingRepository
{
    private readonly CompanionOptions _options = options.Value;

    public async Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
        => await db.NotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct) ?? Default(userId);

    public async Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
        => await db.NotificationSettings.IgnoreQueryFilters([OwnerQueryFilter.Name]).AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct) ?? Default(userId);

    public async Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
    {
        var existing = await db.NotificationSettings.IgnoreQueryFilters([OwnerQueryFilter.Name])
            .FirstOrDefaultAsync(s => s.UserId == setting.UserId, ct);
        if (existing is null)
        {
            setting.UpdatedAt = DateTimeOffset.UtcNow;
            db.NotificationSettings.Add(setting);
        }
        else
        {
            existing.Mode = setting.Mode;
            existing.QuietHoursStartLocal = setting.QuietHoursStartLocal;
            existing.QuietHoursEndLocal = setting.QuietHoursEndLocal;
            existing.TimeZoneId = setting.TimeZoneId;
            existing.MaxProactivePerHour = setting.MaxProactivePerHour;
            existing.DigestHourLocal = setting.DigestHourLocal;
            existing.PushEnabled = setting.PushEnabled;
        }

        await db.SaveChangesAsync(ct);
    }

    private CompanionNotificationSetting Default(Guid userId)
        => new()
        {
            UserId = userId,
            Mode = NotificationMode.Scan,
            TimeZoneId = _options.DefaultTimeZoneId,
            QuietHoursStartLocal = _options.QuietHoursStartLocal,
            QuietHoursEndLocal = _options.QuietHoursEndLocal,
            MaxProactivePerHour = _options.MaxProactivePerHour,
            DigestHourLocal = _options.DigestHourLocal,
        };
}
