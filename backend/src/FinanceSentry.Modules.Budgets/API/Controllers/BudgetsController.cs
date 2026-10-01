namespace FinanceSentry.Modules.Budgets.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Budgets.API.Responses;
using FinanceSentry.Modules.Budgets.Application.Commands;
using FinanceSentry.Modules.Budgets.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("budgets")]
public class BudgetsController(
    IQueryHandler<GetBudgetsQuery, BudgetsListResponse> getBudgets,
    IQueryHandler<GetBudgetSummaryQuery, BudgetSummaryResponse> getBudgetSummary,
    ICommandHandler<CreateBudgetCommand, BudgetDto> createBudget,
    ICommandHandler<UpdateBudgetCommand, BudgetDto> updateBudget,
    ICommandHandler<DeleteBudgetCommand, Unit> deleteBudget) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBudgets(CancellationToken ct)
    {
        var result = await getBudgets.Handle(new GetBudgetsQuery(User.RequireUserId()), ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        var result = await getBudgetSummary.Handle(
            new GetBudgetSummaryQuery(User.RequireUserId(), year, month), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBudget([FromBody] CreateBudgetRequest request, CancellationToken ct)
    {
        var result = await createBudget.Handle(
            new CreateBudgetCommand(User.RequireUserId(), request.Category, request.MonthlyLimit), ct);
        return Created($"/api/v1/budgets/{result.Id}", result);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBudget(Guid id, [FromBody] UpdateBudgetRequest request, CancellationToken ct)
    {
        var result = await updateBudget.Handle(
            new UpdateBudgetCommand(User.RequireUserId(), id, request.MonthlyLimit), ct);
        return Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBudget(Guid id, CancellationToken ct)
    {
        await deleteBudget.Handle(new DeleteBudgetCommand(User.RequireUserId(), id), ct);
        return NoContent();
    }
}

public record CreateBudgetRequest(string Category, decimal MonthlyLimit);
public record UpdateBudgetRequest(decimal MonthlyLimit);
