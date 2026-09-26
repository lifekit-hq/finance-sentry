namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.Application.EventHandlers;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>
/// Issue #686: a new user must get a persisted notification settings row so the policy path (quiet
/// hours, per-hour cap, digest) has something real to read instead of an unsaved default.
/// </summary>
public sealed class UserRegisteredSettingsProvisioningHandlerTests
{
    private sealed class RecordingSettings : INotificationSettingRepository
    {
        public CompanionNotificationSetting? Upserted { get; private set; }

        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(new CompanionNotificationSetting { UserId = userId, Mode = NotificationMode.Scan });

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
        {
            Upserted = setting;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CompanionNotificationSetting>> ListByModeAsync(
            NotificationMode mode, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CompanionNotificationSetting>>([]);
    }

    [Fact]
    public async Task New_user_gets_a_persisted_default_settings_row()
    {
        var userId = Guid.NewGuid();
        var settings = new RecordingSettings();
        var handler = new UserRegisteredSettingsProvisioningHandler(settings);

        await handler.Handle(new UserRegisteredEvent(userId), CancellationToken.None);

        settings.Upserted.Should().NotBeNull();
        settings.Upserted!.UserId.Should().Be(userId);
        settings.Upserted!.Mode.Should().Be(NotificationMode.Scan);
    }
}
