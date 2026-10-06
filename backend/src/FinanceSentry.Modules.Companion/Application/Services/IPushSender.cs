namespace FinanceSentry.Modules.Companion.Application.Services;

using FinanceSentry.Modules.Companion.Domain;

/// <summary>Delivers one encrypted Web Push message to one subscription (spec 859).</summary>
public interface IPushSender
{
    Task<PushSendResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct = default);
}

/// <summary>What the push service said, reduced to what the job acts on.</summary>
public enum PushSendOutcome
{
    /// <summary>2xx: accepted for delivery.</summary>
    Sent,

    /// <summary>404 or 410: the subscription no longer exists; remove it.</summary>
    Gone,

    /// <summary>429, 5xx, timeout or network error: try again later.</summary>
    Transient,

    /// <summary>400, 413 or another 4xx: this message cannot be delivered; do not retry it.</summary>
    Rejected,

    /// <summary>401 or 403: the push service does not accept our VAPID identity for this subscription.</summary>
    Unauthorized,
}

public readonly record struct PushSendResult(PushSendOutcome Outcome, int? StatusCode = null);
