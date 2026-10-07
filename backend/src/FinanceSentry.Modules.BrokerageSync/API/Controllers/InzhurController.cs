using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.BrokerageSync.API.Controllers;

/// <summary>Phone and password are optional: omitted, the saved ones are used and only the SMS code is asked for.</summary>
public sealed record StartInzhurLoginRequest(string? Phone, string? Password);

public sealed record VerifyInzhurLoginRequest(string Code);

/// <summary>
/// The owner's Inzhur cabinet connection. Starting a sign-in can make Inzhur send the owner an SMS, so only these
/// owner-initiated endpoints sign in; the daily sync only refreshes the stored session.
/// </summary>
[ApiController]
[Authorize]
[Route("brokerage/inzhur")]
public sealed class InzhurController(IInzhurConnector connector) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => Ok(await connector.GetStatusAsync(User.RequireUserId(), ct));

    /// <summary>Signs in; answers <c>connected</c> or <c>code_required</c> (an SMS is on its way).</summary>
    [Authorize(Policy = AuthPolicies.RequireConnectionsManage)]
    [HttpPost("login/start")]
    public async Task<IActionResult> Start([FromBody] StartInzhurLoginRequest request, CancellationToken ct)
        => Ok(await connector.StartAsync(User.RequireUserId(), request.Phone, request.Password, ct));

    /// <summary>Submits the SMS code; answers <c>connected</c> or <c>invalid_code</c> with the attempts left.</summary>
    [Authorize(Policy = AuthPolicies.RequireConnectionsManage)]
    [HttpPost("login/verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyInzhurLoginRequest request, CancellationToken ct)
        => Ok(await connector.VerifyAsync(User.RequireUserId(), request.Code, ct));

    [Authorize(Policy = AuthPolicies.RequireConnectionsManage)]
    [HttpDelete("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await connector.DisconnectAsync(User.RequireUserId(), ct);
        return NoContent();
    }
}
