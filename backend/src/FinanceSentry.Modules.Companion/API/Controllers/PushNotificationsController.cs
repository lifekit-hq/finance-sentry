namespace FinanceSentry.Modules.Companion.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Companion.API.Requests;
using FinanceSentry.Modules.Companion.API.Responses;
using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Queries;
using FinanceSentry.Modules.Companion.Application.Services;
using FinanceSentry.Modules.Companion.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

/// <summary>Web Push subscription storage and the user's push opt-in (spec 859). Sending is a separate job.</summary>
[ApiController]
[Authorize]
[Route("notifications/push")]
public class PushNotificationsController(
    IOptions<WebPushOptions> options,
    IQueryHandler<ListPushSubscriptionsQuery, IReadOnlyList<PushSubscriptionDto>> listSubscriptions,
    IQueryHandler<GetPushPreferencesQuery, PushPreferencesDto> getPreferences,
    ICommandHandler<RegisterPushSubscriptionCommand, PushSubscriptionDto> register,
    ICommandHandler<RemovePushSubscriptionCommand, bool> remove,
    ICommandHandler<SetPushPreferencesCommand, PushPreferencesDto> setPreferences) : ControllerBase
{
    [HttpGet("public-key")]
    public IActionResult GetPublicKey()
    {
        var o = options.Value;
        return Ok(new PushPublicKeyDto(o.IsConfigured, o.IsConfigured ? o.PublicKey : null));
    }

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions(CancellationToken ct)
        => Ok(await listSubscriptions.Handle(new ListPushSubscriptionsQuery(User.RequireUserId()), ct));

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe([FromBody] RegisterPushSubscriptionRequest body, CancellationToken ct)
    {
        var dto = await register.Handle(
            new RegisterPushSubscriptionCommand(
                User.RequireUserId(), body.Endpoint, body.Keys.P256dh, body.Keys.Auth, Request.Headers.UserAgent.ToString()),
            ct);
        return Ok(dto);
    }

    [HttpDelete("subscriptions/{id:guid}")]
    public async Task<IActionResult> Unsubscribe(Guid id, CancellationToken ct)
    {
        var ok = await remove.Handle(new RemovePushSubscriptionCommand(User.RequireUserId(), id), ct);
        if (!ok) throw new PushSubscriptionNotFoundException();
        return NoContent();
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken ct)
        => Ok(await getPreferences.Handle(new GetPushPreferencesQuery(User.RequireUserId()), ct));

    [HttpPut("preferences")]
    public async Task<IActionResult> PutPreferences([FromBody] SetPushPreferencesRequest body, CancellationToken ct)
        => Ok(await setPreferences.Handle(new SetPushPreferencesCommand(User.RequireUserId(), body.PushEnabled), ct));
}
