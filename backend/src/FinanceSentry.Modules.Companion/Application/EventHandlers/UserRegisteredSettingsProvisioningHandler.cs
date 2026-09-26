namespace FinanceSentry.Modules.Companion.Application.EventHandlers;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.Domain.Repositories;

/// <summary>
/// Provisions a notification settings row on user registration (issue #686), so the policy path
/// (mode, quiet hours, proactive cap, digest) executes from a user's first run instead of relying on
/// <see cref="INotificationSettingRepository.GetOrDefaultAsync"/>'s unsaved default. Idempotent: if a
/// row already exists (e.g. the event redelivers), <see cref="INotificationSettingRepository.UpsertAsync"/>
/// re-saves the same values it would already hold, since nothing else has had a chance to change them
/// between registration and this handler running.
/// </summary>
public sealed class UserRegisteredSettingsProvisioningHandler(INotificationSettingRepository settings)
    : IEventHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent @event, CancellationToken cancellationToken)
    {
        var defaults = await settings.GetOrDefaultAsync(@event.UserId, cancellationToken);
        await settings.UpsertAsync(defaults, cancellationToken);
    }
}
