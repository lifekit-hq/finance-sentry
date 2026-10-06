namespace FinanceSentry.Modules.Companion.API.Responses;

/// <summary>Whether the server can send push, and the VAPID public key the browser subscribes with.</summary>
public record PushPublicKeyDto(bool Available, string? PublicKey);

public record PushSubscriptionDto(
    Guid Id,
    string? DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSuccessAt,
    bool Disabled);

public record PushPreferencesDto(bool PushEnabled);
