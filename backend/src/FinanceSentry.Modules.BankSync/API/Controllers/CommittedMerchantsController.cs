namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The merchants the user declared committed — rule (d) of the committed-outflow policy. Its own
/// controller rather than another verb on <see cref="BankSyncController"/>: pins are a user
/// preference over the whole book, not an operation on a bank account.
/// </summary>
[ApiController]
[Route("committed-merchants")]
public class CommittedMerchantsController(
    IQueryHandler<ListCommittedMerchantPinsQuery, IReadOnlyList<CommittedMerchantPinDto>> listPins,
    ICommandHandler<PinCommittedMerchantCommand, PinCommittedMerchantResult> pinMerchant,
    ICommandHandler<UnpinCommittedMerchantCommand, bool> unpinMerchant) : ControllerBase
{
    // ── GET /api/v1/committed-merchants ──────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await listPins.Handle(new ListCommittedMerchantPinsQuery(User.RequireUserId()), ct));

    // ── POST /api/v1/committed-merchants ─────────────────────────────────────

    /// <summary>
    /// 201 on a new pin, 200 when the merchant was already pinned — re-pinning is the same
    /// state, not a conflict.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Pin([FromBody] PinCommittedMerchantRequest body, CancellationToken ct)
    {
        var result = await pinMerchant.Handle(
            new PinCommittedMerchantCommand(User.RequireUserId(), body.Merchant), ct);

        // Location is the collection: a pin has no per-id route, since its identity is the
        // merchant key and callers address it by that everywhere else.
        return result.AlreadyPinned
            ? Ok(result.Pin)
            : CreatedAtAction(nameof(List), result.Pin);
    }

    // ── DELETE /api/v1/committed-merchants?merchant=… ────────────────────────

    /// <summary>
    /// The merchant rides the query string rather than the path: a pin key is free text
    /// ("mobile top-up 0057", "mario scalas") whose spaces and slashes make a brittle path
    /// segment, and it is the pin's identity, so unpinning mirrors pinning exactly.
    /// </summary>
    [HttpDelete]
    public async Task<IActionResult> Unpin([FromQuery] string merchant, CancellationToken ct)
    {
        var removed = await unpinMerchant.Handle(
            new UnpinCommittedMerchantCommand(User.RequireUserId(), merchant), ct);

        return removed
            ? NoContent()
            : NotFound(new ApiErrorBody($"'{merchant}' is not pinned as committed.", "COMMITTED_PIN_NOT_FOUND"));
    }
}

public record PinCommittedMerchantRequest(string Merchant);
