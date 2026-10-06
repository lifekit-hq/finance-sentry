namespace FinanceSentry.Modules.Companion.API.Requests;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Modules.Companion.Domain;

/// <summary>The browser's <c>PushSubscription.toJSON()</c> shape.</summary>
public record RegisterPushSubscriptionRequest(
    [Required, MaxLength(PushSubscriptionLimits.EndpointMaxLength)] string Endpoint,
    [Required] PushSubscriptionKeys Keys);

public record PushSubscriptionKeys(
    [Required, MaxLength(PushSubscriptionLimits.P256dhMaxLength)] string P256dh,
    [Required, MaxLength(PushSubscriptionLimits.AuthMaxLength)] string Auth);

public record SetPushPreferencesRequest(bool PushEnabled);
