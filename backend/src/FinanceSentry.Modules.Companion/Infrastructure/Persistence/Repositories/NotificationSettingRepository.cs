namespace FinanceSentry.Modules.Companion.Infrastructure.Persistence.Repositories;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Reads run under the Owner filter. The capture, dispatch and digest jobs and the registration handler run with no
// person in scope, so they call <c>GetOrDefaultUnscopedAsync</c>, which opts out explicitly and keeps its UserId
// predicate; the upsert is one INSERT ... ON CONFLICT, so provisioning never inserts a duplicate row.
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

    // One INSERT ... ON CONFLICT per user on the unique UserId index, so a registration provisioning the row while the
    // person saves their first setting updates one row instead of the loser failing on the index. It runs at once;
    // the conflict keeps the stored Id and stamps UpdatedAt.
    public async Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO companion.companion_notification_settings
                ("Id", "UserId", "Mode", "QuietHoursStartLocal", "QuietHoursEndLocal", "TimeZoneId",
                 "MaxProactivePerHour", "DigestHourLocal", "PushEnabled", "UpdatedAt")
            VALUES
                ({setting.Id}, {setting.UserId}, {setting.Mode.ToString()}, {setting.QuietHoursStartLocal},
                 {setting.QuietHoursEndLocal}, {setting.TimeZoneId}, {setting.MaxProactivePerHour},
                 {setting.DigestHourLocal}, {setting.PushEnabled}, {now})
            ON CONFLICT ("UserId") DO UPDATE SET
                "Mode" = EXCLUDED."Mode",
                "QuietHoursStartLocal" = EXCLUDED."QuietHoursStartLocal",
                "QuietHoursEndLocal" = EXCLUDED."QuietHoursEndLocal",
                "TimeZoneId" = EXCLUDED."TimeZoneId",
                "MaxProactivePerHour" = EXCLUDED."MaxProactivePerHour",
                "DigestHourLocal" = EXCLUDED."DigestHourLocal",
                "PushEnabled" = EXCLUDED."PushEnabled",
                "UpdatedAt" = EXCLUDED."UpdatedAt"
            """,
            ct);
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
