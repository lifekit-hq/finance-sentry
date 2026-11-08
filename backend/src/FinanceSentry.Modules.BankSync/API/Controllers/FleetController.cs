namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Core.Api;
using FinanceSentry.Core.Auditing;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.API.Extensions;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.API.Validation;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services.CategoryMapping;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// The endpoints the fleet-agent identity (a machine-to-machine token of the org identity provider) may call, each
/// behind a fleet scope policy that authenticates with that scheme alone: a person's session token is refused here,
/// and a fleet token is refused everywhere else. The fleet acts as the Owner account; what it can do is exactly this
/// list. Reads, and one reversible recategorisation between spending categories. Nothing that moves money, changes
/// an account or its connection, or changes who owns what is reachable, and there is no scope that would allow it.
/// </summary>
[ApiController]
[Route("fleet")]
public class FleetController(
    IQueryHandler<GetAllTransactionsQuery, AllTransactionsResult> allTransactions,
    ICategoryReadService categories,
    ICommandHandler<RecategorizeTransactionCommand, RecategorizeTransactionResult> recategorize) : ControllerBase
{
    private const int MinTransactionsLimit = 1;
    private const int MaxTransactionsLimit = 200;

    // ── GET /api/v1/fleet/transactions ───────────────────────────────────────

    [HttpGet("transactions")]
    [Authorize(Policy = AuthPolicies.RequireFleetRead)]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 50,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] List<string>? category = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var fromDateError = TransactionFilterValidator.ValidateDate(from, out var fromDate);
        if (fromDateError != null)
            return BadRequest(fromDateError);
        var toDateError = TransactionFilterValidator.ValidateDate(to, out var toDate);
        if (toDateError != null)
            return BadRequest(toDateError);

        var fromDateTime = fromDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toDateTime = toDate?.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var validationError = TransactionFilterValidator.ValidateDateRange(fromDateTime, toDateTime)
            ?? TransactionFilterValidator.ValidateCategories(category)
            ?? TransactionFilterValidator.ValidateSearch(search);
        if (validationError != null)
            return BadRequest(validationError);

        limit = Math.Clamp(limit, MinTransactionsLimit, MaxTransactionsLimit);

        var result = await allTransactions.Handle(
            new GetAllTransactionsQuery(
                User.RequireUserId(), new PagedRequest(offset, limit), fromDateTime, toDateTime,
                Categories: category, Search: search), ct);

        return Ok(PaginationExtensions.CreatePaginatedResponse(
            result.Transactions, result.TotalCount, result.Offset, result.Limit));
    }

    // ── GET /api/v1/fleet/categories ─────────────────────────────────────────

    [HttpGet("categories")]
    [Authorize(Policy = AuthPolicies.RequireFleetRead)]
    public async Task<IActionResult> GetCategories(CancellationToken ct)
    {
        var items = await categories.GetAllAsync(ct);
        return Ok(items.Select(c => new CategoryDto(c.Key, c.Label, c.SortOrder)).ToList());
    }

    // ── PUT /api/v1/fleet/transactions/{id}/category ─────────────────────────

    /// <summary>
    /// Files the transaction under another spending category. The response names the previous category: PUT it back to
    /// undo. Moves into or out of income, transfer, loan-payment and rent-and-utilities categories, and moves across
    /// the day-to-day spend set (food, transport, merchandise, entertainment, personal care) the coverage sentinel
    /// reads, are refused (409): they change money figures, not a label. Not durable: an operator recategorise pass
    /// can put the previous category back with no audit entry; PUT again to redo it.
    /// </summary>
    [HttpPut("transactions/{id:guid}/category")]
    [Authorize(Policy = AuthPolicies.RequireFleetRecategorise)]
    public async Task<IActionResult> Recategorize(Guid id, [FromBody] FleetRecategorizeRequest body, CancellationToken ct)
    {
        var result = await recategorize.Handle(
            new RecategorizeTransactionCommand(User.RequireUserId(), id, body.Category, AuditActorKind.FleetAgent), ct);

        return result.Outcome switch
        {
            RecategorizeOutcome.Changed or RecategorizeOutcome.Unchanged =>
                Ok(new FleetRecategorizeResponse(id, result.PreviousCategory, result.Category!, result.Outcome == RecategorizeOutcome.Changed)),
            RecategorizeOutcome.NotFound =>
                NotFound(new ApiErrorBody("Transaction not found.", "TRANSACTION_NOT_FOUND")),
            RecategorizeOutcome.UnknownCategory =>
                BadRequest(new ApiErrorBody("Unknown category.", "UNKNOWN_CATEGORY")),
            _ => Conflict(new ApiErrorBody(
                "Moves into or out of income, transfer, loan-payment and rent-and-utilities categories, or across the day-to-day spend categories, change money figures and are not a recategorisation.",
                "CATEGORY_AFFECTS_MONEY_FLOW")),
        };
    }
}

public record FleetRecategorizeRequest(string Category);

/// <param name="PreviousCategory">The category before the call (null when it had none); PUT it back to undo.</param>
/// <param name="Changed">False when the transaction already carried the category.</param>
public record FleetRecategorizeResponse(Guid TransactionId, string? PreviousCategory, string Category, bool Changed);
