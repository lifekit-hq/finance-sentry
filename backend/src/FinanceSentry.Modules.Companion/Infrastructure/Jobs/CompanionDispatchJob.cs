namespace FinanceSentry.Modules.Companion.Infrastructure.Jobs;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Realtime dispatch relay (feature 031, US2). Wakes the agent for pending events belonging to users
/// in <c>realtime</c> mode whose account passes <see cref="AuthPolicies.RequireAiUse"/> (other users' events are suppressed), honoring quiet-hours + rate-limit (deferred, never dropped) and retrying
/// failures up to the cap. Scan-mode pending events are left for the agent to pull. Overlap-protected.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 120)]
public sealed class CompanionDispatchJob(
    ICompanionEventRepository events,
    INotificationSettingRepository settings,
    IAgentWakeDispatcher dispatcher,
    IUserAuthorizationChecker authorization,
    IOptions<CompanionOptions> options,
    ILogger<CompanionDispatchJob> logger)
{
    private const int BatchLimit = 100;

    private readonly CompanionOptions _options = options.Value;

    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var pending = await events.ListRealtimePendingUnscopedAsync(BatchLimit, ct);
        var now = DateTimeOffset.UtcNow;
        var aiUseByUser = new Dictionary<Guid, bool>();

        foreach (var evt in pending)
        {
            if (!aiUseByUser.TryGetValue(evt.UserId, out var mayUseAi))
            {
                mayUseAi = await authorization.IsAuthorizedAsync(evt.UserId, AuthPolicies.RequireAiUse, ct);
                aiUseByUser[evt.UserId] = mayUseAi;
            }

            if (!mayUseAi)
            {
                await SetDispositionAsync(evt, EventDisposition.SuppressedNonOwner, ct);
                continue;
            }

            var setting = await settings.GetOrDefaultUnscopedAsync(evt.UserId, ct);
            if (setting.Mode != NotificationMode.Realtime)
            {
                // Scan mode: the agent pulls these. Digest/quiet never reach Pending.
                continue;
            }

            if (QuietHours.IsWithin(setting, now))
            {
                await SetDispositionAsync(evt, EventDisposition.DeferredQuietHours, ct);
                continue;
            }

            var dispatchedLastHour = await events.CountDispatchedSinceUnscopedAsync(evt.UserId, now.AddHours(-1), ct);
            if (dispatchedLastHour >= setting.MaxProactivePerHour)
            {
                await SetDispositionAsync(evt, EventDisposition.SuppressedByRateLimit, ct);
                continue;
            }

            var result = await dispatcher.WakeAsync(evt, ct);
            switch (result)
            {
                case WakeResult.NotConfigured:
                case WakeResult.Skipped:
                    continue; // leave Pending for the pull path
                case WakeResult.Sent:
                    evt.Disposition = EventDisposition.Dispatched;
                    evt.DispatchedAt = DateTimeOffset.UtcNow;
                    await events.UpdateAsync(evt, ct);
                    break;
                case WakeResult.Failed:
                    evt.Attempts++;
                    evt.LastError = "agent wake failed";
                    if (evt.Attempts >= _options.MaxDispatchAttempts)
                    {
                        evt.Disposition = EventDisposition.Failed;
                        logger.LogError("Companion event {EventId} failed after {Attempts} attempts", evt.Id, evt.Attempts);
                    }

                    await events.UpdateAsync(evt, ct);
                    break;
            }
        }
    }

    private async Task SetDispositionAsync(CompanionEvent evt, EventDisposition disposition, CancellationToken ct)
    {
        if (evt.Disposition == disposition)
        {
            return;
        }

        evt.Disposition = disposition;
        await events.UpdateAsync(evt, ct);
    }
}
