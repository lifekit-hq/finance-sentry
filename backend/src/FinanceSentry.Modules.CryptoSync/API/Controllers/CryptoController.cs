using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Application.Queries;
using FinanceSentry.Modules.CryptoSync.Domain;
using Microsoft.AspNetCore.Mvc;

namespace FinanceSentry.Modules.CryptoSync.API.Controllers;

[ApiController]
[Route("crypto")]
public sealed class CryptoController(
    ICommandHandler<ConnectExchangeCommand, ConnectExchangeResult> connectHandler,
    ICommandHandler<DisconnectExchangeCommand, Unit> disconnectHandler,
    IQueryHandler<GetCryptoHoldingsQuery, CryptoHoldingsResponse> holdingsHandler) : ControllerBase
{
    [HttpPost("binance/connect")]
    public Task<IActionResult> ConnectBinance([FromBody] ConnectBinanceRequest request, CancellationToken ct) =>
        ConnectAsync(CryptoExchangeProvider.Binance, request.ApiKey, request.ApiSecret, ct);

    [HttpDelete("binance/disconnect")]
    public Task<IActionResult> DisconnectBinance(CancellationToken ct) =>
        DisconnectAsync(CryptoExchangeProvider.Binance, ct);

    /// <summary>Connects Revolut X with a read-only API key and its Ed25519 private key (PEM).</summary>
    [HttpPost("revolut-x/connect")]
    public Task<IActionResult> ConnectRevolutX([FromBody] ConnectRevolutXRequest request, CancellationToken ct) =>
        ConnectAsync(CryptoExchangeProvider.RevolutX, request.ApiKey, request.PrivateKey, ct);

    [HttpDelete("revolut-x/disconnect")]
    public Task<IActionResult> DisconnectRevolutX(CancellationToken ct) =>
        DisconnectAsync(CryptoExchangeProvider.RevolutX, ct);

    [HttpGet("holdings")]
    public async Task<IActionResult> GetHoldings(CancellationToken ct)
    {
        var result = await holdingsHandler.Handle(new GetCryptoHoldingsQuery(User.RequireUserId()), ct);
        return Ok(result);
    }

    private async Task<IActionResult> ConnectAsync(string provider, string? apiKey, string? apiSecret, CancellationToken ct)
    {
        var result = await connectHandler.Handle(
            new ConnectExchangeCommand(User.RequireUserId(), provider, apiKey ?? string.Empty, apiSecret ?? string.Empty),
            ct);

        return StatusCode(201, result);
    }

    private async Task<IActionResult> DisconnectAsync(string provider, CancellationToken ct)
    {
        await disconnectHandler.Handle(new DisconnectExchangeCommand(User.RequireUserId(), provider), ct);
        return NoContent();
    }
}
