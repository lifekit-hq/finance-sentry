namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Application.Commands;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Mutations on the caller's own counterparties. Currently just the expected-monthly-inflow
/// (rent) expectation that <c>GetFamilyClearingStatementQuery</c> reads to populate its rent
/// fields — see docs/money-semantics.md §5.1.
/// </summary>
[ApiController]
[Route("counterparties")]
public class CounterpartyController(
    ICommandHandler<SetCounterpartyExpectedInflowCommand, SetCounterpartyExpectedInflowResult> setExpectedInflow)
    : ControllerBase
{
    // ── PUT /api/v1/counterparties/{id}/expected-inflow ──────────────────────

    /// <summary>
    /// Sets the expected monthly inflow when both fields are supplied, or clears it when both
    /// are null.
    /// </summary>
    [HttpPut("{id:guid}/expected-inflow")]
    public async Task<IActionResult> SetExpectedInflow(
        Guid id, [FromBody] SetExpectedInflowRequest body, CancellationToken ct)
    {
        var result = await setExpectedInflow.Handle(
            new SetCounterpartyExpectedInflowCommand(User.RequireUserId(), id, body.Amount, body.Currency), ct);

        return Ok(result);
    }
}

public record SetExpectedInflowRequest(decimal? Amount, string? Currency);
