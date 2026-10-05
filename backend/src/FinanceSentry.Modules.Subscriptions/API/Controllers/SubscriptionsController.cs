namespace FinanceSentry.Modules.Subscriptions.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Subscriptions.API.Responses;
using FinanceSentry.Modules.Subscriptions.Application.Commands;
using FinanceSentry.Modules.Subscriptions.Application.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("subscriptions")]
public class SubscriptionsController(
    IQueryHandler<GetSubscriptionsQuery, SubscriptionsListResponse> getSubscriptions,
    IQueryHandler<GetSubscriptionSummaryQuery, SubscriptionSummaryResponse> getSummary,
    IQueryHandler<GetInstallmentFxImpactQuery, InstallmentFxImpactResponse> getFxImpact,
    ICommandHandler<DismissSubscriptionCommand, bool> dismiss,
    ICommandHandler<RestoreSubscriptionCommand, bool> restore,
    ICommandHandler<SetInstallmentTermCommand, bool> setTerm,
    ICommandHandler<CompleteInstallmentCommand, bool> completeInstallment,
    ICommandHandler<DeleteInstallmentCommand, bool> deleteInstallment,
    ICommandHandler<AddCommitmentCommand, Guid> addCommitment,
    ICommandHandler<LinkCommitmentCommand, bool> linkCommitment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] bool includeDismissed = false,
        CancellationToken ct = default)
    {
        var result = await getSubscriptions.Handle(
            new GetSubscriptionsQuery(User.RequireUserId().ToString(), includeDismissed), ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct = default)
    {
        var result = await getSummary.Handle(
            new GetSubscriptionSummaryQuery(User.RequireUserId().ToString()), ct);
        return Ok(result);
    }

    /// <summary>
    /// How exchange-rate movement has changed what foreign-currency installments cost —
    /// the native payment is fixed, its cost in the base currency is not.
    /// </summary>
    [HttpGet("installments/fx-impact")]
    public async Task<IActionResult> GetFxImpact(CancellationToken ct = default)
    {
        var result = await getFxImpact.Handle(
            new GetInstallmentFxImpactQuery(User.RequireUserId().ToString()), ct);
        return Ok(result);
    }

    [HttpPatch("{id:guid}/dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, CancellationToken ct = default)
    {
        await dismiss.Handle(new DismissSubscriptionCommand(User.RequireUserId().ToString(), id), ct);
        return NoContent();
    }

    [HttpPatch("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct = default)
    {
        await restore.Handle(new RestoreSubscriptionCommand(User.RequireUserId().ToString(), id), ct);
        return NoContent();
    }

    [HttpPatch("installments/{id:guid}/term")]
    public async Task<IActionResult> SetTerm(Guid id, [FromBody] SetTermRequest body, CancellationToken ct = default)
    {
        await setTerm.Handle(new SetInstallmentTermCommand(
            User.RequireUserId().ToString(), id, body.TermCount, body.EndDate, body.StartDate), ct);
        return NoContent();
    }

    [HttpPatch("installments/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct = default)
    {
        await completeInstallment.Handle(new CompleteInstallmentCommand(User.RequireUserId().ToString(), id), ct);
        return NoContent();
    }

    [HttpDelete("installments/{id:guid}")]
    public async Task<IActionResult> DeleteInstallment(Guid id, CancellationToken ct = default)
    {
        await deleteInstallment.Handle(new DeleteInstallmentCommand(User.RequireUserId().ToString(), id), ct);
        return NoContent();
    }

    /// <summary>
    /// Adds a subscription or installment from one of the user's transactions; the row then
    /// follows that transaction's later charges.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddCommitmentRequest body, CancellationToken ct = default)
    {
        var id = await addCommitment.Handle(new AddCommitmentCommand(
            User.RequireUserId(),
            body.TransactionId,
            body.Kind,
            body.Merchant,
            body.MonthlyAmount,
            body.TermCount,
            body.Cadence), ct);
        return Ok(new { id });
    }

    /// <summary>
    /// Links a legacy hand-typed row to one of the user's transactions; the row then follows
    /// that transaction's later charges.
    /// </summary>
    [HttpPost("{id:guid}/link")]
    public async Task<IActionResult> Link(Guid id, [FromBody] LinkCommitmentRequest body, CancellationToken ct = default)
    {
        await linkCommitment.Handle(new LinkCommitmentCommand(User.RequireUserId(), id, body.TransactionId, body.Cadence), ct);
        return NoContent();
    }
}

public record SetTermRequest(int? TermCount, DateOnly? EndDate = null, DateOnly? StartDate = null);

public record AddCommitmentRequest(
    Guid TransactionId,
    string Kind,
    string? Merchant = null,
    decimal? MonthlyAmount = null,
    int? TermCount = null,
    string? Cadence = null);

public record LinkCommitmentRequest(Guid TransactionId, string? Cadence = null);
