namespace FinanceSentry.Modules.Companion.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Exceptions;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>Stores a device's Web Push subscription for the signed-in user (spec 859, US1). Only called from an explicit
/// user action in the app; refused when the server has no VAPID configuration.</summary>
public record RegisterPushSubscriptionCommand(
    Guid UserId, string Endpoint, string P256dh, string Auth, string? UserAgent) : ICommand<PushSubscriptionDto>;

public class RegisterPushSubscriptionCommandHandler(
    IPushSubscriptionRepository subscriptions, IOptions<WebPushOptions> options)
    : ICommandHandler<RegisterPushSubscriptionCommand, PushSubscriptionDto>
{
    public async Task<PushSubscriptionDto> Handle(RegisterPushSubscriptionCommand cmd, CancellationToken ct)
    {
        if (!options.Value.IsConfigured) throw new PushUnavailableException();

        if (!PushEndpointPolicy.IsAllowed(cmd.Endpoint))
            throw new PushSubscriptionInvalidException();

        var saved = await subscriptions.UpsertByEndpointAsync(
            new PushSubscription
            {
                UserId = cmd.UserId,
                Endpoint = cmd.Endpoint,
                P256dh = cmd.P256dh,
                Auth = cmd.Auth,
                DeviceLabel = PushDeviceLabel.FromUserAgent(cmd.UserAgent),
            },
            ct);

        return new PushSubscriptionDto(saved.Id, saved.DeviceLabel, saved.CreatedAt, saved.LastSuccessAt, saved.DisabledAt is not null);
    }
}

public record RemovePushSubscriptionCommand(Guid UserId, Guid SubscriptionId) : ICommand<bool>;

public class RemovePushSubscriptionCommandHandler(IPushSubscriptionRepository subscriptions)
    : ICommandHandler<RemovePushSubscriptionCommand, bool>
{
    public Task<bool> Handle(RemovePushSubscriptionCommand cmd, CancellationToken ct)
        => subscriptions.RemoveAsync(cmd.UserId, cmd.SubscriptionId, ct);
}

/// <summary>Turns the user's push opt-in on or off. Never touches the agent notification mode (spec 859 FR-001).</summary>
public record SetPushPreferencesCommand(Guid UserId, bool PushEnabled) : ICommand<PushPreferencesDto>;

public class SetPushPreferencesCommandHandler(INotificationSettingRepository settings, IOptions<WebPushOptions> options)
    : ICommandHandler<SetPushPreferencesCommand, PushPreferencesDto>
{
    public async Task<PushPreferencesDto> Handle(SetPushPreferencesCommand cmd, CancellationToken ct)
    {
        if (cmd.PushEnabled && !options.Value.IsConfigured) throw new PushUnavailableException();

        var setting = await settings.GetOrDefaultAsync(cmd.UserId, ct);
        setting.PushEnabled = cmd.PushEnabled;
        await settings.UpsertAsync(setting, ct);
        return new PushPreferencesDto(setting.PushEnabled);
    }
}
