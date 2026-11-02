namespace FinanceSentry.Modules.BankSync.API.Controllers;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The payday transfer: how much EUR covers the Monobank card's amount owed plus a fixed monthly outflow at the bank's
/// EUR buy rate, and the two settings (outflow, rounding step) that shape it.
/// </summary>
[ApiController]
[Authorize]
[Route("payday-transfer")]
public class PaydayTransferController(
    IQueryHandler<GetPaydayTransferQuery, PaydayTransferDto> get,
    ICommandHandler<UpdatePaydayTransferSettingsCommand, PaydayTransferSettingsDto> update) : ControllerBase
{
    // ── GET /api/v1/payday-transfer ──────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Ok(await get.Handle(new GetPaydayTransferQuery(User.RequireUserId()), ct));

    // ── PUT /api/v1/payday-transfer/settings ─────────────────────────────────

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdatePaydayTransferSettingsRequest body, CancellationToken ct)
        => Ok(await update.Handle(
            new UpdatePaydayTransferSettingsCommand(User.RequireUserId(), body.FixedOutflowUah, body.RoundingStepEur), ct));
}

public record UpdatePaydayTransferSettingsRequest(
    [Range(0, (double)PaydayTransferDefaults.MaxAmount)] decimal FixedOutflowUah,
    [Range(0.01, (double)PaydayTransferDefaults.MaxAmount)] decimal RoundingStepEur);
