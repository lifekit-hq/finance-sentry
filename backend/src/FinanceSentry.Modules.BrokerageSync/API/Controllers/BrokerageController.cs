using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Application.Commands;
using FinanceSentry.Modules.BrokerageSync.Application.Connect;
using FinanceSentry.Modules.BrokerageSync.Application.Queries;
using FinanceSentry.Modules.BrokerageSync.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.BrokerageSync.API.Controllers;

public sealed record ConnectIBKRRequest(
    string ConsumerKey,
    string AccessToken,
    string AccessTokenSecret,
    string SignatureKey,
    string EncryptionKey,
    string DhParam);

public sealed record SetInstrumentClassificationRequest(InstrumentClassification? Classification);

[ApiController]
[Route("brokerage")]
public sealed class BrokerageController(
    IIBKRConnector connector,
    ICommandHandler<DisconnectIBKRCommand, Unit> disconnectHandler,
    IQueryHandler<GetBrokerageHoldingsQuery, BrokerageHoldingsResponse> holdingsHandler,
    IQueryHandler<GetBrokerageInstrumentsQuery, BrokerageInstrumentsResponse> instrumentsHandler,
    ICommandHandler<SetInstrumentClassificationCommand, Unit> setClassificationHandler) : ControllerBase
{
    /// <summary>
    /// Persists the user's IBKR OAuth 1.0a artifacts (encrypting the secret
    /// material at rest). Live-session-token derivation and the initial holdings
    /// sync run out of band once the consumer key activates on IBKR's side.
    /// </summary>
    [HttpPost("ibkr/connect")]
    public async Task<IActionResult> Connect([FromBody] ConnectIBKRRequest request, CancellationToken ct)
    {
        try
        {
            var artifacts = new ConnectIBKRArtifacts(
                request.ConsumerKey,
                request.AccessToken,
                request.AccessTokenSecret,
                request.SignatureKey,
                request.EncryptionKey,
                request.DhParam);
            var result = await connector.ConnectAsync(User.RequireUserId(), artifacts, ct);
            return Ok(result);
        }
        catch (IBKRConnectException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, errorMessage = ex.Message });
        }
    }

    [HttpGet("holdings")]
    public async Task<IActionResult> GetHoldings(CancellationToken ct)
    {
        var result = await holdingsHandler.Handle(new GetBrokerageHoldingsQuery(User.RequireUserId()), ct);
        return Ok(result);
    }

    [HttpDelete("ibkr/disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await disconnectHandler.Handle(new DisconnectIBKRCommand(User.RequireUserId()), ct);
        return NoContent();
    }

    /// <summary>Lists the caller's broker instruments with their (possibly null) tax classification.</summary>
    [HttpGet("instruments")]
    public async Task<IActionResult> GetInstruments(CancellationToken ct)
    {
        var result = await instrumentsHandler.Handle(new GetBrokerageInstrumentsQuery(User.RequireUserId()), ct);
        return Ok(result);
    }

    /// <summary>
    /// Sets or clears (via <c>classification: null</c>) the human-set tax classification on one
    /// of the caller's own instruments. 404s if the instrument does not belong to the caller.
    /// </summary>
    [HttpPut("instruments/{id:guid}/classification")]
    public async Task<IActionResult> SetInstrumentClassification(
        Guid id, [FromBody] SetInstrumentClassificationRequest request, CancellationToken ct)
    {
        await setClassificationHandler.Handle(
            new SetInstrumentClassificationCommand(User.RequireUserId(), id, request.Classification), ct);
        return NoContent();
    }
}
