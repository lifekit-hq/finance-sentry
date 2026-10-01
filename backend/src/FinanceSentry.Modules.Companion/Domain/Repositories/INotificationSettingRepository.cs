namespace FinanceSentry.Modules.Companion.Domain.Repositories;

using FinanceSentry.Modules.Companion.Domain;

public interface INotificationSettingRepository
{
    /// <summary>The user's setting, or a defaulted (unsaved) instance when no row exists yet.</summary>
    Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's setting or its default, for the capture, dispatch and digest jobs and the registration
    /// handler, which run with no person in scope. Opts out of the Owner query filter.</summary>
    Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Insert or update the user's setting. Registration provisions it with no person in scope, so the
    /// existence check opts out of the Owner query filter.</summary>
    Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default);

    /// <summary>Users currently set to the given mode (persisted rows only).</summary>
    Task<IReadOnlyList<CompanionNotificationSetting>> ListByModeAsync(
        NotificationMode mode, CancellationToken ct = default);
}
