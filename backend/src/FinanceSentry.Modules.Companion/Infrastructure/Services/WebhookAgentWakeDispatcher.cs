namespace FinanceSentry.Modules.Companion.Infrastructure.Services;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Posts a minimal wake payload to the configured agent-trigger URL (feature 031). When no URL is
/// configured the event stays pending for the agent to pull (no realtime push). Payload carries only
/// ids/refs — never secrets or full detail (FR-016) — plus an absolute <c>appUrl</c> to the page the event is about
/// when a public base URL is configured and the event has a target. Waking the agent is an AI feature, so wakes for a
/// user whose account fails <see cref="AuthPolicies.RequireAiUse"/> are skipped (<see cref="WakeResult.Skipped"/>).
/// Authenticates with the configured bearer token and stamps each event wake with <c>Idempotency-Key: &lt;eventId&gt;</c>, so the receiver can dedup
/// the relay's retries (the dispatch job re-posts a failed wake up to <see cref="CompanionOptions.MaxDispatchAttempts"/>).
/// </summary>
public sealed class WebhookAgentWakeDispatcher(
    IHttpClientFactory httpFactory,
    IOptions<CompanionOptions> options,
    IUserAuthorizationChecker authorization,
    ILogger<WebhookAgentWakeDispatcher> logger) : IAgentWakeDispatcher
{
    public const string HttpClientName = "companion-wake";

    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public const string DigestHeldOverrideReason = "daily digest";

    private const string BearerScheme = "Bearer";

    private readonly CompanionOptions _options = options.Value;

    private static readonly HashSet<CompanionEventKind> ProposalKinds =
    [
        CompanionEventKind.RebalanceProposal,
        CompanionEventKind.CashSweepProposal,
        CompanionEventKind.PolicyReview,
    ];

    public async Task<WakeResult> WakeAsync(CompanionEvent evt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AgentTriggerUrl))
        {
            return WakeResult.NotConfigured;
        }

        if (!await authorization.IsAuthorizedAsync(evt.UserId, AuthPolicies.RequireAiUse, ct))
        {
            return WakeResult.Skipped;
        }

        // Proposal events carry acknowledgement metadata so the bot can render inline-keyboard buttons.
        // referenceId is the stable per-user anchor GUID; the bot calls PATCH /alerts/{referenceId}/acknowledge.
        var isProposal = ProposalKinds.Contains(evt.Kind);
        var payload = new Dictionary<string, object?>
        {
            ["eventId"] = evt.Id,
            ["userId"] = evt.UserId,
            ["kind"] = evt.Kind.ToString(),
            ["subject"] = evt.Subject,
            ["severity"] = evt.Severity,
            ["occurredAt"] = evt.OccurredAt,
            ["requiresAcknowledgement"] = isProposal ? true : null,
            ["referenceId"] = isProposal ? evt.ReferenceId : null,
        };

        // The page the event is about, absolute so the agent can put it in a message; left out (not null) when unknown.
        if (CompanionAppUrl.For(_options.PublicBaseUrl, evt.AppPath) is { } appUrl)
        {
            payload["appUrl"] = appUrl;
        }

        return await PostAsync(payload, $"event {evt.Id}", evt.Id.ToString(), ct);
    }

    public async Task<WakeResult> WakeDigestAsync(Guid userId, int heldCount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AgentTriggerUrl))
        {
            return WakeResult.NotConfigured;
        }

        if (!await authorization.IsAuthorizedAsync(userId, AuthPolicies.RequireAiUse, ct))
        {
            return WakeResult.Skipped;
        }

        var payload = new
        {
            kind = "Digest",
            userId,
            count = heldCount,
            includeHeldForDigest = true,
            heldOverrideReason = DigestHeldOverrideReason,
        };
        return await PostAsync(payload, $"digest for {userId}", idempotencyKey: null, ct);
    }

    private async Task<WakeResult> PostAsync(object payload, string label, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.AgentTriggerUrl)
            {
                Content = JsonContent.Create(payload),
            };

            if (!string.IsNullOrWhiteSpace(_options.AgentTriggerToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, _options.AgentTriggerToken);
            }

            if (idempotencyKey is not null)
            {
                request.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
            }

            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return WakeResult.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Agent wake POST failed for {Label}", label);
            return WakeResult.Failed;
        }
    }
}
