namespace FinanceSentry.Modules.BankSync.API.Controllers;

using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("dashboard")]
public class DashboardController(
    IDashboardQueryService dashboard,
    ITransactionRepository transactions,
    IBankAccountRepository accounts,
    ITransferDetectionService transferDetection,
    IFlowBreakdownService flowBreakdown) : ControllerBase
{
    private readonly IDashboardQueryService _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
    private readonly IFlowBreakdownService _flowBreakdown = flowBreakdown ?? throw new ArgumentNullException(nameof(flowBreakdown));
    private readonly ITransactionRepository _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
    private readonly IBankAccountRepository _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
    private readonly ITransferDetectionService _transferDetection = transferDetection ?? throw new ArgumentNullException(nameof(transferDetection));

    // ── GET /api/dashboard/aggregated ── T408 ─────────────────────────────────

    [HttpGet("aggregated")]
    public async Task<IActionResult> GetAggregated([FromQuery] int months = 6, CancellationToken ct = default)
    {
        var data = await _dashboard.GetDashboardDataAsync(User.RequireUserId(), months, ct);
        return Ok(data);
    }

    // ── GET /api/dashboard/flow-breakdown ─────────────────────────────────────

    /// <summary>
    /// Every credit/debit of one month labelled with the bucket the flow statistics put it
    /// in — the audit view behind the dashboard tiles. <paramref name="months"/> must be the
    /// window the dashboard was rendered with so pair detection sees the same neighbours.
    /// </summary>
    [HttpGet("flow-breakdown")]
    public async Task<IActionResult> GetFlowBreakdown(
        [FromQuery] string month,
        [FromQuery] int months = 6,
        CancellationToken ct = default)
    {
        if (!DateTime.TryParseExact(
                month, "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
        {
            return BadRequest(new { errorCode = "INVALID_MONTH", message = "month must be yyyy-MM" });
        }

        var data = await _flowBreakdown.GetBreakdownAsync(User.RequireUserId(), month, months, ct);
        return Ok(data);
    }

    // ── GET /api/dashboard/transfers ── T410 ──────────────────────────────────

    [HttpGet("transfers")]
    public async Task<IActionResult> GetTransfers(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var allTx = (await _transactions.GetByUserIdAsync(userId, ct)).ToList();
        var accountCurrencies = (await _accounts.GetByUserIdAsync(userId, ct))
            .ToDictionary(a => a.Id, a => a.Currency);
        var transferIds = _transferDetection.DetectTransferTransactionIds(allTx, accountCurrencies);

        var byId = allTx.ToDictionary(t => t.Id);
        var consumedCredits = new HashSet<Guid>();
        var pairs = new List<TransferPairDto>();

        foreach (var debit in allTx.Where(t => t.TransactionType == "debit" && transferIds.Contains(t.Id)))
        {
            foreach (var credit in allTx.Where(t => t.TransactionType == "credit" && transferIds.Contains(t.Id)))
            {
                if (consumedCredits.Contains(credit.Id)) continue;
                if (!_transferDetection.IsLikelyTransfer(
                        debit, credit,
                        accountCurrencies.GetValueOrDefault(debit.AccountId),
                        accountCurrencies.GetValueOrDefault(credit.AccountId))) continue;

                pairs.Add(new TransferPairDto(
                    new TransferItemDto(
                        debit.Id, debit.AccountId, debit.Amount,
                        debit.PostedDate ?? debit.TransactionDate, debit.Description),
                    new TransferItemDto(
                        credit.Id, credit.AccountId, credit.Amount,
                        credit.PostedDate ?? credit.TransactionDate, credit.Description)));
                consumedCredits.Add(credit.Id);
                break;
            }
        }

        return Ok(new TransferPairsResponse(pairs, pairs.Count));
    }
}
