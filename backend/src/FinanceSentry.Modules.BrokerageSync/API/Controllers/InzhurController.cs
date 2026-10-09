using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.BrokerageSync.API.Controllers;

/// <summary>The value of the cabinet's <c>refreshToken</c> cookie. A secret: <see cref="ToString"/> is redacted.</summary>
public sealed record ConnectInzhurSessionRequest(string? RefreshToken)
{
    public override string ToString() => "ConnectInzhurSessionRequest { RefreshToken = [redacted] }";
}

/// <summary>
/// The owner's Inzhur cabinet connection. finance-sentry never signs in to Inzhur: the owner signs in on inzhur.reit
/// and hands over the session here; the daily sync only refreshes it.
/// </summary>
[ApiController]
[Authorize]
[Route("brokerage/inzhur")]
public sealed class InzhurController(IInzhurConnector connector) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
        => Ok(await connector.GetStatusAsync(User.RequireUserId(), ct));

    /// <summary>Takes over the pasted session once one refresh proves it; answers <c>connected</c>.</summary>
    [Authorize(Policy = AuthPolicies.RequireConnectionsManage)]
    [HttpPost("session")]
    public async Task<IActionResult> ConnectSession([FromBody] ConnectInzhurSessionRequest request, CancellationToken ct)
        => Ok(await connector.ConnectSessionAsync(User.RequireUserId(), request.RefreshToken, ct));

    [Authorize(Policy = AuthPolicies.RequireConnectionsManage)]
    [HttpDelete("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await connector.DisconnectAsync(User.RequireUserId(), ct);
        return NoContent();
    }
}
