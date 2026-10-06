namespace FinanceSentry.Modules.Companion.Infrastructure.Services;

using System.Net;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PushSubscription = FinanceSentry.Modules.Companion.Domain.PushSubscription;

/// <summary>
/// Sends an encrypted, VAPID-signed Web Push message with <c>Lib.Net.Http.WebPush</c> (spec 859). The endpoint was
/// allow-listed when the subscription was registered; the named client also refuses redirects so an allowed push
/// service cannot bounce the request to an internal host. The payload is never logged, and neither are the keys.
/// </summary>
public sealed class WebPushSender(
    IHttpClientFactory httpFactory,
    IOptions<WebPushOptions> options,
    ILogger<WebPushSender> logger) : IPushSender
{
    public const string HttpClientName = "companion-webpush";

    private const int NotFound = (int)HttpStatusCode.NotFound;
    private const int Gone = (int)HttpStatusCode.Gone;
    private const int TooManyRequests = (int)HttpStatusCode.TooManyRequests;
    private const int ServerErrorFloor = 500;

    private readonly WebPushOptions _options = options.Value;

    public async Task<PushSendResult> SendAsync(PushSubscription subscription, string payload, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return new PushSendResult(PushSendOutcome.Unauthorized);

        var client = new PushServiceClient(httpFactory.CreateClient(HttpClientName))
        {
            DefaultAuthentication = new VapidAuthentication(_options.PublicKey!, _options.PrivateKey!)
            {
                Subject = _options.Subject,
            },
        };

        var target = new Lib.Net.Http.WebPush.PushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        var message = new PushMessage(payload)
        {
            TimeToLive = (int)PushDeliveryPolicy.TimeToLive.TotalSeconds,
            Urgency = PushMessageUrgency.Normal,
        };

        try
        {
            await client.RequestPushMessageDeliveryAsync(target, message, ct);
            return new PushSendResult(PushSendOutcome.Sent);
        }
        catch (PushServiceClientException ex)
        {
            var status = (int)ex.StatusCode;
            var outcome = Classify(status);
            logger.LogWarning("Web Push to subscription {SubscriptionId} answered {StatusCode} ({Outcome})",
                subscription.Id, status, outcome);
            return new PushSendResult(outcome, status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Web Push to subscription {SubscriptionId} failed in transport", subscription.Id);
            return new PushSendResult(PushSendOutcome.Transient);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Web Push to subscription {SubscriptionId} could not be built or encrypted", subscription.Id);
            return new PushSendResult(PushSendOutcome.Rejected);
        }
    }

    /// <summary>The status-code matrix (spec 859 FR-007).</summary>
    public static PushSendOutcome Classify(int statusCode) => statusCode switch
    {
        NotFound or Gone => PushSendOutcome.Gone,
        (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden => PushSendOutcome.Unauthorized,
        TooManyRequests => PushSendOutcome.Transient,
        >= ServerErrorFloor => PushSendOutcome.Transient,
        _ => PushSendOutcome.Rejected,
    };
}
