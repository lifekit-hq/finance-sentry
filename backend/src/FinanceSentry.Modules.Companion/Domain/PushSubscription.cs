namespace FinanceSentry.Modules.Companion.Domain;

/// <summary>
/// One browser/device Web Push subscription (spec 859). The <see cref="Endpoint"/> is unique across users: a device
/// that changes signed-in user re-registers and the row moves to the newer owner.
/// </summary>
public sealed class PushSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Base64url client public key.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Base64url auth secret.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>Short label derived from the User-Agent, shown in the device list.</summary>
    public string? DeviceLabel { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastSuccessAt { get; set; }

    public DateTimeOffset? LastFailureAt { get; set; }

    public int FailureCount { get; set; }

    /// <summary>Set when repeated configuration failures soft-disable the subscription.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
}
